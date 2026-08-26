#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Buildings
{
    /// <summary>
    /// Batch-creates <see cref="BuildingTemplateData"/> assets for prop sprites that were
    /// sliced out of a multi-object sheet by <c>tools/atlas/slice_prop_sheet.py</c> and
    /// staged into <c>Resources/Buildings/&lt;category&gt;/</c> by
    /// <c>tools/atlas/build_building_props.py</c>.
    ///
    /// The Python side owns the pixels and the per-sprite metadata; this side owns the
    /// ScriptableObjects. The contract between them is every generated manifest matching
    /// <see cref="MANIFEST_SEARCH_PATTERN"/> under <see cref="MANIFEST_DIR_RELATIVE"/>
    /// (repo-relative, versioned in git) — one per wave of sheets, all read together.
    ///
    /// The import is IDEMPOTENT and keyed on <see cref="BuildingTemplateData.assetPath"/>:
    ///   * an entry whose assetPath already has a template updates that template in place
    ///     and keeps its templateId, so world instances that reference it never break;
    ///   * an entry with no template gets the next free templateId.
    /// Re-running after re-slicing therefore refreshes data instead of duplicating it.
    ///
    /// Neither menu item opens a dialog: both are driven from the MCP bridge as often as
    /// from the menu bar, and a modal dialog there hangs the calling tool.
    /// </summary>
    public static class BuildingPropImporter
    {
        private const string MENU_DRY_RUN = "Valkur/Buildings/Import Prop Sprites (Dry Run)";
        private const string MENU_APPLY   = "Valkur/Buildings/Import Prop Sprites (Apply)";

        private const string MANIFEST_DIR_RELATIVE = "../../../tools/atlas/generated";
        private const string MANIFEST_SEARCH_PATTERN = "building_props_manifest*.json";
        private const string CATALOG_PATH  = "Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset";
        private const string TEMPLATE_DIR  = "Assets/_Project/Data/Catalogs/Buildings";
        private const string LOG_PREFIX    = "[BuildingPropImporter]";

        /// <summary>Keys <c>Data/LightPresetCatalog.asset</c> defines.</summary>
        private static readonly string[] LIGHT_PRESET_KEYS = { "Lamp", "Torch", "Magic", "Candle" };

        /// <summary>Flame height used when an entry names a preset but no offset.</summary>
        private const float DEFAULT_LIGHT_OFFSET_Y = 0.75f;

        // ── Manifest schema (JsonUtility needs concrete serializable types) ────────────

        [Serializable]
        private class Manifest
        {
            public string generator;
            public string generatedFrom;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        private class Entry
        {
            public string name;
            public string category;
            public string resourcePath;      // e.g. "Buildings/lights/lamp_post_ornate"
            public string sourceImagePath;   // e.g. "assets/buildings/lights/lamp_post_ornate.png"
            public bool solid;
            public float splitRatio;
            public string colliderScope;
            public int width;
            public int height;
            public string sheet;
            public int sheetIndex;
            public string lightPresetKey;   // "" = this prop emits no light
            public float lightOffsetY;      // flame height as a fraction of the bounds
        }

        // ── Menu entry points ─────────────────────────────────────────────────────────

        [MenuItem(MENU_DRY_RUN)]
        public static void DryRun() => Run(apply: false);

        [MenuItem(MENU_APPLY)]
        public static void Apply() => Run(apply: true);

        // ── Implementation ────────────────────────────────────────────────────────────

        private static void Run(bool apply)
        {
            // Every manifest in the folder, not one fixed file: each wave of sheets
            // writes its own, and the sheets an older wave was cut from are deleted
            // once imported. Reading them all keeps every wave reproducible instead
            // of making the newest one clobber the record of the last.
            if (!TryLoadManifests(out Manifest manifest, out List<string> sources))
                return;

            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                Debug.LogError($"{LOG_PREFIX} BuildingCatalog not found at {CATALOG_PATH}.");
                return;
            }

            if (!ValidateManifest(manifest, out int rejected))
            {
                Debug.LogError($"{LOG_PREFIX} Aborting: {rejected} manifest entries are invalid (see above).");
                return;
            }

            Dictionary<string, BuildingTemplateData> byAssetPath =
                IndexExistingTemplates(out int nextId, out HashSet<string> sharedAssetPaths);

            var created = new List<string>();
            var updated = new List<string>();
            var missingSprite = new List<string>();

            foreach (Entry entry in manifest.entries)
            {
                var sprite = Resources.Load<Sprite>(entry.resourcePath);
                if (sprite == null)
                {
                    missingSprite.Add(entry.resourcePath);
                    continue;
                }

                if (sharedAssetPaths.Contains(entry.resourcePath))
                {
                    Debug.LogWarning($"{LOG_PREFIX} '{entry.resourcePath}' is already claimed by more than one " +
                                     "template. Updating the lowest-id one; the others keep stale data.");
                }

                bool isNew = !byAssetPath.TryGetValue(entry.resourcePath, out BuildingTemplateData tpl);
                if (isNew)
                {
                    created.Add($"#{nextId} {entry.resourcePath}");
                    if (apply)
                    {
                        tpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
                        tpl.templateId = nextId;
                        string assetFile = $"{TEMPLATE_DIR}/BuildingTemplate_{nextId}.asset";
                        AssetDatabase.CreateAsset(tpl, assetFile);
                        byAssetPath[entry.resourcePath] = tpl;
                    }
                    nextId++;
                }
                else
                {
                    updated.Add($"#{tpl.templateId} {entry.resourcePath}");
                }

                if (!apply) continue;

                // Deliberately NOT Undo.RecordObject. A bulk import writes ~200 assets in
                // one pass and lands them on the global editor undo stack; the first thing
                // that pops that stack — a runtime-editor undo test, a stray Ctrl+Z —
                // reverts every one of them IN MEMORY to its empty creation state while the
                // correct data sits on disk. The objects then stay dirty-and-empty, and the
                // next SaveAssets writes the emptiness over the good data. Observed: the
                // EditMode suite reverted all 193 templates this way. SetDirty alone is the
                // right tool for data an operator re-runs rather than undoes.
                tpl.assetPath       = entry.resourcePath;
                tpl.previewSprite   = sprite;
                tpl.solid           = entry.solid;
                tpl.splitRatio      = Mathf.Clamp01(entry.splitRatio);
                tpl.colliderScope   = string.IsNullOrEmpty(entry.colliderScope) ? "CG" : entry.colliderScope;
                tpl.originalScale   = new Vector2Int(entry.width, entry.height);
                tpl.sourceImagePath = entry.sourceImagePath;

                // Only written when the manifest actually names a preset. A manifest
                // predating this field deserializes lightPresetKey as null, and
                // clearing the key on every re-import would silently unlight the 33
                // fixtures the first wave authored by hand.
                if (!string.IsNullOrEmpty(entry.lightPresetKey))
                {
                    tpl.lightPresetKey = entry.lightPresetKey;
                    float offsetY = entry.lightOffsetY > 0f ? entry.lightOffsetY : DEFAULT_LIGHT_OFFSET_Y;
                    tpl.lightOffsetNormalized = new Vector2(0.5f, Mathf.Clamp01(offsetY));
                }

                EditorUtility.SetDirty(tpl);

                catalog.UpsertTemplate(tpl);
            }

            if (apply)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Report(apply, manifest, sources, created, updated, missingSprite);
        }

        /// <summary>
        /// Reads every <c>building_props_manifest*.json</c> in the generated folder and
        /// concatenates their entries. Returns false (having logged why) when the folder
        /// is missing, holds no manifest, or one of them will not parse — a half-read set
        /// would look like "these templates were dropped" to the caller.
        /// </summary>
        private static bool TryLoadManifests(out Manifest merged, out List<string> sources)
        {
            merged = new Manifest { generator = LOG_PREFIX, generatedFrom = MANIFEST_SEARCH_PATTERN };
            sources = new List<string>();

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, MANIFEST_DIR_RELATIVE));
            if (!Directory.Exists(dir))
            {
                Debug.LogError($"{LOG_PREFIX} Manifest folder not found at {dir}. " +
                               "Run tools/atlas/build_building_props.py first.");
                return false;
            }

            string[] files = Directory.GetFiles(dir, MANIFEST_SEARCH_PATTERN);
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0)
            {
                Debug.LogError($"{LOG_PREFIX} No {MANIFEST_SEARCH_PATTERN} under {dir}. " +
                               "Run tools/atlas/build_building_props.py first.");
                return false;
            }

            foreach (string file in files)
            {
                Manifest m;
                try
                {
                    m = JsonUtility.FromJson<Manifest>(File.ReadAllText(file));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} Manifest at {file} is not readable: {ex.Message}");
                    return false;
                }

                if (m?.entries == null || m.entries.Count == 0)
                {
                    Debug.LogError($"{LOG_PREFIX} Manifest at {file} has no entries.");
                    return false;
                }

                merged.entries.AddRange(m.entries);
                sources.Add($"{Path.GetFileName(file)} ({m.entries.Count})");
            }

            return true;
        }

        /// <summary>
        /// Rejects a manifest that would produce broken or colliding template data.
        /// Runs before anything is written so a bad manifest never half-imports.
        /// </summary>
        private static bool ValidateManifest(Manifest manifest, out int rejected)
        {
            rejected = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Entry e in manifest.entries)
            {
                var problems = new List<string>();

                if (string.IsNullOrWhiteSpace(e.resourcePath))
                    problems.Add("empty resourcePath");
                else if (!e.resourcePath.StartsWith("Buildings/", StringComparison.Ordinal))
                    problems.Add($"resourcePath '{e.resourcePath}' is not under Buildings/");
                else if (!seen.Add(e.resourcePath))
                    problems.Add($"duplicate resourcePath '{e.resourcePath}'");

                if (Path.HasExtension(e.resourcePath))
                    problems.Add("resourcePath must not carry a file extension");

                if (e.width <= 0 || e.height <= 0)
                    problems.Add($"non-positive size {e.width}x{e.height}");

                if (e.splitRatio < 0f || e.splitRatio > 1f)
                    problems.Add($"splitRatio {e.splitRatio} outside [0,1]");

                if (e.colliderScope != "CG" && e.colliderScope != "CU" && !string.IsNullOrEmpty(e.colliderScope))
                    problems.Add($"colliderScope '{e.colliderScope}' is neither CG nor CU");

                // A key the catalog does not define imports cleanly and then lights
                // nothing at runtime, which reads as "the brazier is broken".
                if (!string.IsNullOrEmpty(e.lightPresetKey) &&
                    Array.IndexOf(LIGHT_PRESET_KEYS, e.lightPresetKey) < 0)
                    problems.Add($"lightPresetKey '{e.lightPresetKey}' is not one of " +
                                 string.Join("/", LIGHT_PRESET_KEYS));

                if (problems.Count > 0)
                {
                    rejected++;
                    Debug.LogError($"{LOG_PREFIX} entry '{e.name}' ({e.sheet}#{e.sheetIndex}): " +
                                   string.Join("; ", problems));
                }
            }

            return rejected == 0;
        }

        /// <summary>
        /// Every existing template indexed by its Resources path, plus the first template
        /// id that nothing in the project is already using.
        ///
        /// Several legacy templates deliberately share one image with different split /
        /// solid data, so a shared assetPath is a normal steady state here and is only
        /// reported when the manifest actually targets one of those paths.
        /// </summary>
        private static Dictionary<string, BuildingTemplateData> IndexExistingTemplates(
            out int nextId, out HashSet<string> sharedAssetPaths)
        {
            var byAssetPath = new Dictionary<string, BuildingTemplateData>(StringComparer.Ordinal);
            sharedAssetPaths = new HashSet<string>(StringComparer.Ordinal);
            int maxId = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BuildingTemplateData)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tpl = AssetDatabase.LoadAssetAtPath<BuildingTemplateData>(path);
                if (tpl == null) continue;

                maxId = Mathf.Max(maxId, tpl.templateId);
                if (string.IsNullOrEmpty(tpl.assetPath)) continue;

                if (byAssetPath.TryGetValue(tpl.assetPath, out BuildingTemplateData clash))
                {
                    sharedAssetPaths.Add(tpl.assetPath);
                    if (tpl.templateId >= clash.templateId) continue;
                }
                byAssetPath[tpl.assetPath] = tpl;
            }

            nextId = maxId + 1;
            return byAssetPath;
        }

        private static void Report(bool apply, Manifest manifest, List<string> sources,
                                   List<string> created, List<string> updated,
                                   List<string> missingSprite)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{LOG_PREFIX} {(apply ? "APPLIED" : "DRY RUN")} — " +
                          $"{manifest.entries.Count} entries from {string.Join(", ", sources)}");
            sb.AppendLine($"  created : {created.Count}");
            sb.AppendLine($"  updated : {updated.Count}");
            sb.AppendLine($"  missing sprite : {missingSprite.Count}");

            foreach (string group in new[] { "created", "updated" })
            {
                List<string> list = group == "created" ? created : updated;
                foreach (string line in list.Take(8))
                    sb.AppendLine($"    {group}: {line}");
                if (list.Count > 8)
                    sb.AppendLine($"    {group}: … and {list.Count - 8} more");
            }

            if (missingSprite.Count > 0)
            {
                foreach (string path in missingSprite)
                    Debug.LogError($"{LOG_PREFIX} no sprite at Resources/{path} — " +
                                   "did build_building_props.py write it, and did Unity import it?");
            }

            Debug.Log(sb.ToString());
        }
    }
}
#endif
