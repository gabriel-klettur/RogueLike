#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Monsters
{
    /// <summary>
    /// Batch-creates / updates <see cref="MonsterDefinition"/> assets from the animation
    /// frames <c>tools/atlas/build_monster_frames.py</c> slices, aligns and (optionally)
    /// mirrors out of a character sheet, staging them under
    /// <c>Art/NPC/monsters/&lt;monsterKey&gt;/</c>.
    ///
    /// Mirrors <see cref="Valkur.Editor.Buildings.BuildingPropImporter"/> deliberately: the
    /// Python side owns the pixels and the per-sprite geometry; this side owns the
    /// ScriptableObjects. The contract between them is every generated manifest matching
    /// <see cref="MANIFEST_SEARCH_PATTERN"/> under <see cref="MANIFEST_DIR_RELATIVE"/>
    /// (repo-relative, versioned in git) - one per wave of monsters, all read together, so a
    /// second wave never clobbers the record of the first. The source sheets themselves are
    /// not versioned (<c>unity/downloads/</c> is gitignored, same as the building pipeline).
    ///
    /// The import is IDEMPOTENT and keyed on <see cref="MonsterDefinition.monsterKey"/>:
    ///   * a key with no existing definition gets a new asset at
    ///     <c>Data/Catalogs/Monsters/&lt;monsterKey&gt;.asset</c> and is registered via
    ///     <see cref="MonsterCatalog.UpsertDefinition"/>;
    ///   * a key that already resolves to a definition has its <see cref="EntityAssetConfig"/>
    ///     sprite slots refreshed in place, so a re-run after re-slicing updates frames without
    ///     disturbing anything a designer authored by hand on the same asset (stats, fsmSet,
    ///     loot, boss data - none of that is this importer's business; see the "What this
    ///     importer does NOT do" note below).
    ///
    /// Only the state slots the manifest actually names are written: a manifest that supplies
    /// just <c>idle</c> and <c>walk</c> leaves <c>chase</c>/<c>cast</c>/<c>attack</c>/
    /// <c>damage</c>/<c>death</c> exactly as they were (empty on a new asset, whatever a
    /// designer set on an existing one) - <see cref="Valkur.Gameplay.EntityAnimationBinder"/> already falls
    /// each of those back to a neighbour when empty, so leaving them alone is correct, not
    /// merely convenient.
    ///
    /// What this importer does NOT do: it never touches <see cref="MonsterDefinition.stats"/>,
    /// <c>fsmSet</c>, <c>autoCastList</c>, <c>xpReward</c> or <c>lootTable</c>. Those are a
    /// design decision per monster, not a pixel-pipeline output - CLAUDE.md's asset-pipeline
    /// skill draws that same line for buildings (solid/splitRatio/light come from the sheet;
    /// nothing about a building's gameplay role does).
    ///
    /// Neither menu item opens a dialog: both are driven from the MCP bridge as often as from
    /// the menu bar, and a modal dialog there hangs the calling tool.
    /// </summary>
    public static class MonsterFramesImporter
    {
        private const string MENU_DRY_RUN = "Valkur/Monsters/Import Frame Sheets (Dry Run)";
        private const string MENU_APPLY   = "Valkur/Monsters/Import Frame Sheets (Apply)";

        private const string MANIFEST_DIR_RELATIVE = "../../../tools/atlas/generated";
        private const string MANIFEST_SEARCH_PATTERN = "monster_frames_manifest*.json";
        private const string CATALOG_PATH = "Assets/_Project/Data/Catalogs/Monsters/MonsterCatalog.asset";
        private const string TEMPLATE_DIR = "Assets/_Project/Data/Catalogs/Monsters";
        private const string LOG_PREFIX  = "[MonsterFramesImporter]";

        /// <summary>The seven states EntityAssetConfig actually has slots for.</summary>
        private static readonly string[] KNOWN_STATES =
            { "idle", "walk", "chase", "cast", "attack", "damage", "death" };

        private static readonly string[] DIRECTIONS =
            { "south", "southEast", "east", "northEast", "north", "northWest", "west", "southWest" };

        // ── Manifest schema (JsonUtility needs concrete serializable types, no Dictionary) ──

        [Serializable]
        private class Manifest
        {
            public string generator;
            public string generatedFrom;
            public List<MonsterEntry> monsters = new List<MonsterEntry>();
        }

        [Serializable]
        private class MonsterEntry
        {
            public string monsterKey;
            public string displayName;
            public List<DirectionalFrameEntry> idle = new List<DirectionalFrameEntry>();
            public List<StateSheetEntry> states = new List<StateSheetEntry>();
        }

        [Serializable]
        private class DirectionalFrameEntry
        {
            public string direction;   // one of DIRECTIONS
            public string path;        // "Assets/_Project/Art/NPC/monsters/<key>/<key>_idle_<dir>.png"
        }

        [Serializable]
        private class StateSheetEntry
        {
            public string state;              // one of KNOWN_STATES
            public int framesPerDirection;
            public List<string> sprites = new List<string>();  // framesPerDirection * 8, S,SE,E,NE,N,NW,W,SW order
        }

        // ── Menu entry points ─────────────────────────────────────────────────────────

        [MenuItem(MENU_DRY_RUN)]
        public static void DryRun() => RunProduction(apply: false);

        [MenuItem(MENU_APPLY)]
        public static void Apply() => RunProduction(apply: true);

        /// <summary>Result of one import pass, returned so tests can assert on it directly
        /// instead of scraping console output.</summary>
        public sealed class ImportSummary
        {
            public bool Aborted;
            public readonly List<string> Created = new List<string>();
            public readonly List<string> Updated = new List<string>();
            public readonly List<string> MissingSprites = new List<string>();
            public readonly List<string> Sources = new List<string>();
        }

        // ── Implementation ────────────────────────────────────────────────────────────

        private static void RunProduction(bool apply)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, MANIFEST_DIR_RELATIVE));
            var catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                Debug.LogError($"{LOG_PREFIX} MonsterCatalog not found at {CATALOG_PATH}.");
                return;
            }

            ImportSummary summary = Import(dir, catalog, TEMPLATE_DIR, apply, refreshAssetDatabase: apply);
            if (!summary.Aborted)
                Report(apply, summary);
        }

        /// <summary>
        /// Reads every <c>monster_frames_manifest*.json</c> under <paramref name="manifestDir"/>,
        /// validates them, and (when <paramref name="apply"/>) creates/updates a
        /// <see cref="MonsterDefinition"/> per entry under <paramref name="templateDir"/> and
        /// registers each one on <paramref name="catalog"/> via
        /// <see cref="MonsterCatalog.UpsertDefinition"/>.
        ///
        /// Exposed as the seam tests use: production runs it against the real manifest folder
        /// and the real catalog (see <see cref="RunProduction"/>); a test runs it against a
        /// scratch manifest directory and an in-memory <see cref="MonsterCatalog"/> instance so
        /// nothing shipped is ever at risk. The manifest-discovery, validation, create/update
        /// and registration logic is identical either way.
        /// </summary>
        public static ImportSummary Import(string manifestDir, MonsterCatalog catalog, string templateDir,
                                           bool apply, bool refreshAssetDatabase = true)
        {
            var summary = new ImportSummary();

            if (!TryLoadManifests(manifestDir, out Manifest manifest, out List<string> sources))
            {
                summary.Aborted = true;
                return summary;
            }
            summary.Sources.AddRange(sources);

            if (!ValidateManifest(manifest, out int rejected))
            {
                Debug.LogError($"{LOG_PREFIX} Aborting: {rejected} manifest entries are invalid (see above).");
                summary.Aborted = true;
                return summary;
            }

            // Newly-written PNGs on disk are invisible to AssetDatabase until a refresh -
            // the Python step runs outside the Editor, so nothing else triggers one here.
            if (apply && refreshAssetDatabase)
                AssetDatabase.Refresh();

            Dictionary<string, MonsterDefinition> byKey = IndexExistingDefinitions(out HashSet<string> dupKeys);
            foreach (string dup in dupKeys)
                Debug.LogWarning($"{LOG_PREFIX} monsterKey '{dup}' is claimed by more than one " +
                                 "MonsterDefinition asset. Updating the first one found; the others keep stale data.");

            List<string> created = summary.Created;
            List<string> updated = summary.Updated;
            List<string> missingSprites = summary.MissingSprites;

            foreach (MonsterEntry entry in manifest.monsters)
            {
                bool isNew = !byKey.TryGetValue(entry.monsterKey, out MonsterDefinition def);
                if (isNew) created.Add(entry.monsterKey);
                else updated.Add(entry.monsterKey);

                // Resolved regardless of apply, so a dry run reports missing sprites too -
                // matching BuildingPropImporter, which resolves every Resources.Load before
                // deciding new-vs-update.
                DirectionalSprites resolvedIdle = ResolveIdle(entry.idle, entry.monsterKey, missingSprites);
                var resolvedStates = ResolveStates(entry.states, entry.monsterKey, missingSprites);

                if (!apply) continue;

                if (isNew)
                {
                    def = ScriptableObject.CreateInstance<MonsterDefinition>();
                    def.monsterKey = entry.monsterKey;
                    string assetFile = AssetDatabase.GenerateUniqueAssetPath(
                        $"{templateDir}/{entry.monsterKey}.asset");
                    AssetDatabase.CreateAsset(def, assetFile);
                    byKey[entry.monsterKey] = def;
                }

                // Deliberately NOT Undo.RecordObject - see BuildingPropImporter's incident
                // note (.github incident BUILDINGS_SAVE_POSITION_COLLAPSE-adjacent: a bulk
                // import lands ~dozens of assets on the global editor undo stack, and the
                // first thing that pops it reverts every one of them IN MEMORY to its empty
                // creation state while the correct data sits on disk). SetDirty alone is the
                // right tool for data an operator re-runs rather than undoes.
                def.displayName = string.IsNullOrEmpty(entry.displayName) ? def.displayName : entry.displayName;
                if (def.assetConfig == null)
                    def.assetConfig = new EntityAssetConfig();

                // Only the slots the manifest actually named are written - see the class doc.
                if (entry.idle != null && entry.idle.Count > 0)
                    def.assetConfig.idle = resolvedIdle;
                foreach ((string state, List<Sprite> frames) in resolvedStates)
                    AssignStateSheet(def.assetConfig, state, frames);

                EditorUtility.SetDirty(def);
                catalog.UpsertDefinition(def);
            }

            if (apply)
            {
                EditorUtility.SetDirty(catalog);
                if (refreshAssetDatabase)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }

            return summary;
        }

        /// <summary>Resolves every authored direction to a <see cref="Sprite"/>, reporting (not
        /// throwing on) any path that fails to load - a dry run needs to see these too.</summary>
        private static DirectionalSprites ResolveIdle(List<DirectionalFrameEntry> idle,
                                                       string monsterKey, List<string> missingSprites)
        {
            var d = default(DirectionalSprites);
            if (idle == null) return d;

            foreach (DirectionalFrameEntry f in idle)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(f.path);
                if (sprite == null)
                {
                    missingSprites.Add($"{monsterKey} idle.{f.direction}: {f.path}");
                    continue;
                }
                AssignDirection(ref d, f.direction, sprite);
            }
            return d;
        }

        private static List<(string state, List<Sprite> frames)> ResolveStates(
            List<StateSheetEntry> states, string monsterKey, List<string> missingSprites)
        {
            var result = new List<(string, List<Sprite>)>();
            if (states == null) return result;

            foreach (StateSheetEntry s in states)
            {
                var frames = new List<Sprite>(s.sprites?.Count ?? 0);
                if (s.sprites != null)
                {
                    foreach (string path in s.sprites)
                    {
                        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                        if (sprite == null)
                        {
                            missingSprites.Add($"{monsterKey} {s.state}: {path}");
                            continue;
                        }
                        frames.Add(sprite);
                    }
                }
                result.Add((s.state, frames));
            }
            return result;
        }

        private static void AssignDirection(ref DirectionalSprites d, string direction, Sprite sprite)
        {
            switch (direction)
            {
                case "south": d.south = sprite; break;
                case "southEast": d.southEast = sprite; break;
                case "east": d.east = sprite; break;
                case "northEast": d.northEast = sprite; break;
                case "north": d.north = sprite; break;
                case "northWest": d.northWest = sprite; break;
                case "west": d.west = sprite; break;
                case "southWest": d.southWest = sprite; break;
            }
        }

        private static void AssignStateSheet(EntityAssetConfig cfg, string state, List<Sprite> frames)
        {
            switch (state)
            {
                case "idle": cfg.idleSheets = frames; break;
                case "walk": cfg.walkSheets = frames; break;
                case "chase": cfg.chaseSheets = frames; break;
                case "cast": cfg.castSheets = frames; break;
                case "attack": cfg.attackSheets = frames; break;
                case "damage": cfg.damageSheets = frames; break;
                case "death": cfg.deathSheets = frames; break;
            }
        }

        /// <summary>
        /// Reads every <c>monster_frames_manifest*.json</c> in the generated folder and
        /// concatenates their monster lists. Returns false (having logged why) when the
        /// folder is missing, holds no manifest, or one of them will not parse.
        /// </summary>
        private static bool TryLoadManifests(string manifestDir, out Manifest merged, out List<string> sources)
        {
            merged = new Manifest { generator = LOG_PREFIX, generatedFrom = MANIFEST_SEARCH_PATTERN };
            sources = new List<string>();

            string dir = manifestDir;
            if (!Directory.Exists(dir))
            {
                Debug.LogError($"{LOG_PREFIX} Manifest folder not found at {dir}. " +
                               "Run tools/atlas/build_monster_frames.py first.");
                return false;
            }

            string[] files = Directory.GetFiles(dir, MANIFEST_SEARCH_PATTERN);
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0)
            {
                Debug.LogError($"{LOG_PREFIX} No {MANIFEST_SEARCH_PATTERN} under {dir}. " +
                               "Run tools/atlas/build_monster_frames.py first.");
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

                if (m?.monsters == null || m.monsters.Count == 0)
                {
                    Debug.LogError($"{LOG_PREFIX} Manifest at {file} has no monsters.");
                    return false;
                }

                merged.monsters.AddRange(m.monsters);
                sources.Add($"{Path.GetFileName(file)} ({m.monsters.Count})");
            }

            return true;
        }

        /// <summary>
        /// Rejects a manifest that would produce broken definitions. Runs before anything is
        /// written so a bad manifest never half-imports.
        /// </summary>
        private static bool ValidateManifest(Manifest manifest, out int rejected)
        {
            rejected = 0;
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (MonsterEntry e in manifest.monsters)
            {
                var problems = new List<string>();

                if (string.IsNullOrWhiteSpace(e.monsterKey))
                    problems.Add("empty monsterKey");
                else
                {
                    if (e.monsterKey.Any(c => !(char.IsLower(c) || char.IsDigit(c) || c == '_')))
                        problems.Add($"monsterKey '{e.monsterKey}' must be lowercase snake_case");
                    if (!seenKeys.Add(e.monsterKey))
                        problems.Add($"duplicate monsterKey '{e.monsterKey}' within this batch");
                }

                if (e.idle != null && e.idle.Count > 0)
                {
                    var seenDirs = new HashSet<string>(StringComparer.Ordinal);
                    foreach (DirectionalFrameEntry f in e.idle)
                    {
                        if (!DIRECTIONS.Contains(f.direction))
                            problems.Add($"idle names unknown direction '{f.direction}'");
                        else if (!seenDirs.Add(f.direction))
                            problems.Add($"idle names direction '{f.direction}' more than once");

                        if (string.IsNullOrWhiteSpace(f.path))
                            problems.Add($"idle.{f.direction}: empty path");
                        else if (!f.path.StartsWith("Assets/", StringComparison.Ordinal))
                            problems.Add($"idle.{f.direction}: path '{f.path}' is not under Assets/");
                    }
                }

                if (e.states != null)
                {
                    var seenStates = new HashSet<string>(StringComparer.Ordinal);
                    foreach (StateSheetEntry s in e.states)
                    {
                        if (!KNOWN_STATES.Contains(s.state))
                            problems.Add($"state '{s.state}' is not one of {string.Join("/", KNOWN_STATES)}");
                        else if (!seenStates.Add(s.state))
                            problems.Add($"state '{s.state}' appears more than once");

                        int expected = s.framesPerDirection * DIRECTIONS.Length;
                        if (s.framesPerDirection <= 0)
                            problems.Add($"state '{s.state}': framesPerDirection {s.framesPerDirection} must be positive");
                        else if (s.sprites == null || s.sprites.Count != expected)
                            problems.Add($"state '{s.state}': {s.sprites?.Count.ToString() ?? "null"} sprites, " +
                                         $"want {s.framesPerDirection} x 8 = {expected}");

                        if (s.sprites != null)
                        {
                            foreach (string path in s.sprites)
                            {
                                if (string.IsNullOrWhiteSpace(path))
                                    problems.Add($"state '{s.state}': empty sprite path");
                                else if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                                    problems.Add($"state '{s.state}': path '{path}' is not under Assets/");
                            }
                        }
                    }
                }

                if (problems.Count > 0)
                {
                    rejected++;
                    Debug.LogError($"{LOG_PREFIX} entry '{e.monsterKey}': {string.Join("; ", problems)}");
                }
            }

            return rejected == 0;
        }

        /// <summary>
        /// Every existing definition indexed by its monsterKey, plus which keys are claimed
        /// by more than one asset (a state that should never happen but is worth surfacing
        /// rather than silently picking one).
        /// </summary>
        private static Dictionary<string, MonsterDefinition> IndexExistingDefinitions(out HashSet<string> dupKeys)
        {
            var byKey = new Dictionary<string, MonsterDefinition>(StringComparer.Ordinal);
            dupKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(MonsterDefinition)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
                if (def == null || string.IsNullOrEmpty(def.monsterKey)) continue;

                if (byKey.ContainsKey(def.monsterKey))
                    dupKeys.Add(def.monsterKey);
                else
                    byKey[def.monsterKey] = def;
            }

            return byKey;
        }

        private static void Report(bool apply, ImportSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{LOG_PREFIX} {(apply ? "APPLIED" : "DRY RUN")} - " +
                          $"monster(s) from {string.Join(", ", summary.Sources)}");
            sb.AppendLine($"  created : {summary.Created.Count}");
            sb.AppendLine($"  updated : {summary.Updated.Count}");
            sb.AppendLine($"  missing sprites : {summary.MissingSprites.Count}");

            foreach (string group in new[] { "created", "updated" })
            {
                List<string> list = group == "created" ? summary.Created : summary.Updated;
                foreach (string line in list.Take(8))
                    sb.AppendLine($"    {group}: {line}");
                if (list.Count > 8)
                    sb.AppendLine($"    {group}: ... and {list.Count - 8} more");
            }

            if (summary.MissingSprites.Count > 0)
            {
                foreach (string line in summary.MissingSprites)
                    Debug.LogError($"{LOG_PREFIX} no sprite at {line} - " +
                                   "did build_monster_frames.py write it, and did Unity import it?");
            }

            Debug.Log(sb.ToString());
        }
    }
}
#endif
