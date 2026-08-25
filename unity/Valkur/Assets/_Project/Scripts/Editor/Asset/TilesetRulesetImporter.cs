#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Converts the offline pixel analysis at
    /// <c>tools/atlas/generated/tile_rulesets.json</c> into
    /// <see cref="TilesetRuleset"/> Corner16 slot data.
    ///
    /// <para>
    /// <b>Why Corner16 and not Blob16.</b> The generator samples a band just inside each
    /// tile edge and a small square at each corner, then labels every region by material.
    /// Across the 13 packs under <c>Resources/Tiles/</c>, edges came back genuinely
    /// ambiguous (grass_dirt 28, sand_grass 56, grass_rock 56 — a diagonal cut is
    /// legitimately half one material, half the other) while corners were never
    /// ambiguous. The art is corner-Wang, not the cardinal-neighbor Blob16 the runtime
    /// solver already had, hence <see cref="Corner16Slot"/> / <see cref="AutoTileModel.Corner16"/>.
    /// </para>
    ///
    /// <para>
    /// The JSON only covers the 5 packs whose 16 corner signatures ("0000".."1111",
    /// <c>cornerOrder</c> "NW,NE,SE,SW") are completely accounted for: grass_dirt,
    /// grass_rock, rock_water, sand_grass, sand_rock. The remaining packs
    /// (ocean_grass, sand_ocean, sand_ocean_2, ...) are simply absent from
    /// <c>packs</c> and are therefore never touched by this importer — nothing here
    /// invents or backfills a missing combination. A pack's <c>extraSignatures</c>
    /// (leftover tiles touching a third material, outside the binary corner16 model)
    /// are reported but intentionally not imported — out of scope for this pass.
    /// </para>
    ///
    /// <para>
    /// A 4-character signature key parses directly to the <see cref="Corner16Slot"/>
    /// byte via <c>Convert.ToByte(key, 2)</c> — see that enum's own doc comment for the
    /// bit layout. Every ruleset targeted here already exists next to its sprites and is
    /// already registered in <c>TerrainCatalog.asset</c> with the right
    /// TerrainPrimary/TerrainSecondary; this importer looks the asset up by folder name
    /// and preserves those fields, only writing FolderName, Model and the 16 slots. The
    /// "create a brand-new ruleset" path exists for a future pack that isn't cataloged
    /// yet, but the JSON gives no terrain names, so a freshly created ruleset is left
    /// with empty TerrainPrimary/TerrainSecondary and a loud warning to fill them in.
    /// </para>
    ///
    /// <para>
    /// <b>All-or-nothing per pack.</b> If even one listed sprite fails to resolve under
    /// its <c>Resources/Tiles/&lt;pack&gt;/</c> folder, the whole pack is aborted and
    /// nothing is written for it — a half-written slot table reads in-game as a missing
    /// tile in exactly the configuration an author is trying to paint, which looks like
    /// a bug rather than an incomplete import.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotent.</b> <see cref="TilesetRuleset.EditorSetSlot(Corner16Slot, Sprite[])"/>
    /// upserts by slot value (update in place, never re-append), and
    /// <see cref="TerrainCatalog.EditorAdd"/> no-ops if the ruleset is already listed —
    /// running this twice in a row does not duplicate catalog entries or reorder slots.
    /// Never uses <c>Undo.RecordObject</c>: a bulk importer that lands ~80 slot writes on
    /// the global undo stack is exactly the failure mode documented for
    /// <c>BuildingPropImporter</c> (see its own header) — <see cref="EditorUtility.SetDirty"/>
    /// is the correct tool for data an operator re-runs rather than undoes.
    /// </para>
    ///
    /// Neither menu item opens a dialog — both are as likely to be driven from the MCP
    /// bridge as from the menu bar, and a modal dialog there hangs the calling tool.
    /// </summary>
    public static class TilesetRulesetImporter
    {
        private const string MENU_DRY_RUN = "Valkur/Tiles/Import Corner16 Rulesets (Dry Run)";
        private const string MENU_APPLY   = "Valkur/Tiles/Import Corner16 Rulesets (Apply)";

        private const string MANIFEST_RELATIVE_PATH = "../../../tools/atlas/generated/tile_rulesets.json";
        private const string TILES_ROOT     = "Assets/_Project/Resources/Tiles";
        private const string CATALOG_PATH   = "Assets/_Project/Resources/TerrainCatalog.asset";
        private const string RULESET_FILE   = "ruleset.asset";
        private const string LOG_PREFIX     = "[TilesetRulesetImporter]";
        private const string EXPECTED_CORNER_ORDER = "NW,NE,SE,SW";
        private const string EXPECTED_MODEL = "corner16";
        private const int SLOT_COUNT = 16;
        private const int SIGNATURE_LENGTH = 4;

        /// <summary>Everything needed to write one pack, computed before any asset is touched.</summary>
        private readonly struct ResolvedPack
        {
            public readonly string PackName;
            public readonly string PackFolder;
            public readonly TilesetRuleset ExistingRuleset; // null => a new asset will be created on Apply
            public readonly Dictionary<byte, Sprite[]> SlotVariants; // exactly 16 entries, keyed by Corner16Slot value
            public readonly int ExtraSignatureCount;

            public ResolvedPack(string packName, string packFolder, TilesetRuleset existingRuleset,
                                 Dictionary<byte, Sprite[]> slotVariants, int extraSignatureCount)
            {
                PackName = packName;
                PackFolder = packFolder;
                ExistingRuleset = existingRuleset;
                SlotVariants = slotVariants;
                ExtraSignatureCount = extraSignatureCount;
            }
        }

        // ── Menu entry points ─────────────────────────────────────────────────────────

        [MenuItem(MENU_DRY_RUN)]
        public static void DryRun() => Run(apply: false);

        [MenuItem(MENU_APPLY)]
        public static void Apply() => Run(apply: true);

        // ── Implementation ────────────────────────────────────────────────────────────

        private static void Run(bool apply)
        {
            string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, MANIFEST_RELATIVE_PATH));
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"{LOG_PREFIX} manifest not found at '{manifestPath}'. " +
                               "Run tools/atlas/analyze_tile_edges.py first.");
                return;
            }

            Dictionary<string, object> root;
            try
            {
                root = MiniJson.Deserialize(File.ReadAllText(manifestPath)) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} manifest at '{manifestPath}' is not readable: {ex.Message}");
                return;
            }

            if (root == null)
            {
                Debug.LogError($"{LOG_PREFIX} manifest did not parse to a JSON object.");
                return;
            }

            if (root.TryGetValue("model", out object modelObj) && !string.Equals(modelObj as string, EXPECTED_MODEL, StringComparison.Ordinal))
                Debug.LogWarning($"{LOG_PREFIX} manifest 'model' is '{modelObj}', expected '{EXPECTED_MODEL}' — continuing, but double-check the generator version.");

            if (!root.TryGetValue("packs", out object packsObj) || !(packsObj is Dictionary<string, object> packs) || packs.Count == 0)
            {
                Debug.LogError($"{LOG_PREFIX} manifest has no non-empty 'packs' object.");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<TerrainCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                Debug.LogError($"{LOG_PREFIX} TerrainCatalog not found at '{CATALOG_PATH}'.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{LOG_PREFIX} {(apply ? "APPLIED" : "DRY RUN")} — {packs.Count} pack(s) in manifest");

            int ok = 0, failed = 0;
            foreach (var kv in packs.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string packName = kv.Key;
                if (!TryResolvePack(packName, kv.Value, catalog, out ResolvedPack resolved, out string error))
                {
                    Debug.LogError($"{LOG_PREFIX} [{packName}] ABORTED — {error}");
                    sb.AppendLine($"  {packName}: ABORTED — {error}");
                    failed++;
                    continue;
                }

                if (apply)
                    ApplyPack(resolved, catalog);

                AppendPackSummary(sb, resolved, apply);
                ok++;
            }

            if (apply)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            sb.AppendLine($"  totals: {ok} ok, {failed} aborted");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Validates one pack entry and resolves every sprite it lists, WITHOUT writing
        /// anything. Returns false (with a human-readable reason) the moment anything
        /// about the pack can't be trusted, so the caller can abort it wholesale.
        /// </summary>
        private static bool TryResolvePack(string packName, object packDataObj, TerrainCatalog catalog,
                                            out ResolvedPack resolved, out string error)
        {
            resolved = default;
            error = null;

            if (!(packDataObj is Dictionary<string, object> packData))
            {
                error = "pack entry is not a JSON object.";
                return false;
            }

            if (!packData.TryGetValue("cornerOrder", out object cornerOrderObj) ||
                !string.Equals(cornerOrderObj as string, EXPECTED_CORNER_ORDER, StringComparison.Ordinal))
            {
                error = $"cornerOrder is '{cornerOrderObj}', expected '{EXPECTED_CORNER_ORDER}' — " +
                        "refusing to guess a different corner-bit layout.";
                return false;
            }

            if (!packData.TryGetValue("slots", out object slotsObj) || !(slotsObj is Dictionary<string, object> slotsJson))
            {
                error = "missing 'slots' object.";
                return false;
            }

            if (slotsJson.Count != SLOT_COUNT)
            {
                error = $"has {slotsJson.Count} slot keys, expected exactly {SLOT_COUNT} " +
                        "(an incomplete Corner16 cover) — not one of the finished packs.";
                return false;
            }

            string packFolder = $"{TILES_ROOT}/{packName}";
            if (!AssetDatabase.IsValidFolder(packFolder))
            {
                error = $"folder '{packFolder}' does not exist under Resources/Tiles/.";
                return false;
            }

            var slotVariants = new Dictionary<byte, Sprite[]>(SLOT_COUNT);
            var missingSprites = new List<string>();

            foreach (var slotKv in slotsJson)
            {
                if (!TryParseSignature(slotKv.Key, out byte slotValue))
                {
                    error = $"slot key '{slotKv.Key}' is not a 4-character binary signature.";
                    return false;
                }

                if (slotVariants.ContainsKey(slotValue))
                {
                    error = $"slot key '{slotKv.Key}' collides with another key mapping to the same " +
                            $"signature value {slotValue} — the manifest is internally inconsistent.";
                    return false;
                }

                if (!(slotKv.Value is List<object> spriteNames) || spriteNames.Count == 0)
                {
                    error = $"slot '{slotKv.Key}' lists no sprite variants.";
                    return false;
                }

                var variants = new Sprite[spriteNames.Count];
                for (int i = 0; i < spriteNames.Count; i++)
                {
                    string spriteName = spriteNames[i] as string;
                    Sprite sprite = string.IsNullOrEmpty(spriteName) ? null : FindSpriteInFolder(packFolder, spriteName);
                    if (sprite == null)
                        missingSprites.Add($"slot {slotKv.Key}: '{spriteName ?? "<null>"}'");
                    else
                        variants[i] = sprite;
                }

                slotVariants[slotValue] = variants;
            }

            if (missingSprites.Count > 0)
            {
                error = $"{missingSprites.Count} sprite(s) not found under '{packFolder}' (searched recursively) — " +
                        string.Join("; ", missingSprites);
                return false;
            }

            if (slotVariants.Count != SLOT_COUNT)
            {
                error = $"resolved only {slotVariants.Count}/{SLOT_COUNT} distinct signature values.";
                return false;
            }

            int extraSignatureCount = packData.TryGetValue("extraSignatures", out object extraObj) &&
                                       extraObj is Dictionary<string, object> extraDict
                ? extraDict.Count
                : 0;

            TilesetRuleset existing = FindExistingRuleset(packName, packFolder, catalog);
            resolved = new ResolvedPack(packName, packFolder, existing, slotVariants, extraSignatureCount);
            return true;
        }

        /// <summary>
        /// "0110" -&gt; 0b0110, matching <see cref="Corner16Slot"/>'s own
        /// <c>Convert.ToByte(key, 2)</c> contract exactly.
        /// </summary>
        private static bool TryParseSignature(string key, out byte value)
        {
            value = 0;
            if (string.IsNullOrEmpty(key) || key.Length != SIGNATURE_LENGTH) return false;
            for (int i = 0; i < SIGNATURE_LENGTH; i++)
                if (key[i] != '0' && key[i] != '1') return false;
            value = Convert.ToByte(key, 2);
            return true;
        }

        /// <summary>
        /// Looks up a sprite by exact file base-name, searched recursively under
        /// <paramref name="folder"/> (packs keep a legacy "*_slices" subfolder whose
        /// files are still valid variant sources).
        /// </summary>
        private static Sprite FindSpriteInFolder(string folder, string spriteName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{spriteName} t:Sprite", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), spriteName, StringComparison.Ordinal))
                    continue;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) return sprite;
            }
            return null;
        }

        /// <summary>
        /// The ruleset this pack targets: the conventional <c>&lt;pack&gt;/ruleset.asset</c>
        /// path first, then a fallback scan of the catalog by <see cref="TilesetRuleset.FolderName"/>
        /// in case an existing asset was named differently. Null means "create new" on Apply.
        /// </summary>
        private static TilesetRuleset FindExistingRuleset(string packName, string packFolder, TerrainCatalog catalog)
        {
            var atConventionalPath = AssetDatabase.LoadAssetAtPath<TilesetRuleset>($"{packFolder}/{RULESET_FILE}");
            if (atConventionalPath != null) return atConventionalPath;

            foreach (var r in catalog.Rulesets)
                if (r != null && string.Equals(r.FolderName, packName, StringComparison.Ordinal))
                    return r;

            return null;
        }

        private static void ApplyPack(ResolvedPack resolved, TerrainCatalog catalog)
        {
            TilesetRuleset ruleset = resolved.ExistingRuleset;
            bool isNew = ruleset == null;
            if (isNew)
            {
                ruleset = ScriptableObject.CreateInstance<TilesetRuleset>();
                AssetDatabase.CreateAsset(ruleset, $"{resolved.PackFolder}/{RULESET_FILE}");
            }

            // Preserve whatever terrains/priority the ruleset already declares (or leave
            // them empty for a brand-new one — the JSON has no terrain names to draw from).
            ruleset.EditorSetMetadata(
                resolved.PackName,
                ruleset.TerrainPrimary,
                ruleset.TerrainSecondary,
                ruleset.Priority,
                AutoTileModel.Corner16);

            foreach (var kv in resolved.SlotVariants)
                ruleset.EditorSetSlot((Corner16Slot)kv.Key, kv.Value);

            EditorUtility.SetDirty(ruleset);

            if (isNew)
            {
                catalog.EditorAdd(ruleset);
                Debug.LogWarning($"{LOG_PREFIX} [{resolved.PackName}] created new ruleset at " +
                                 $"'{resolved.PackFolder}/{RULESET_FILE}' with EMPTY TerrainPrimary/TerrainSecondary " +
                                 "— the JSON does not name terrains, set them in the Inspector.");
            }
        }

        private static void AppendPackSummary(StringBuilder sb, ResolvedPack resolved, bool apply)
        {
            int totalVariants = 0, minVariants = int.MaxValue, maxVariants = 0;
            foreach (var variants in resolved.SlotVariants.Values)
            {
                totalVariants += variants.Length;
                minVariants = Math.Min(minVariants, variants.Length);
                maxVariants = Math.Max(maxVariants, variants.Length);
            }

            string verb = apply ? "wrote" : "would write";
            string target = resolved.ExistingRuleset != null
                ? AssetDatabase.GetAssetPath(resolved.ExistingRuleset)
                : $"{resolved.PackFolder}/{RULESET_FILE} (new)";
            string variantRange = minVariants == maxVariants ? $"{minVariants}" : $"{minVariants}-{maxVariants}";

            sb.AppendLine($"  {resolved.PackName}: {verb} {resolved.SlotVariants.Count}/{SLOT_COUNT} slots, " +
                          $"{variantRange} variant(s)/slot ({totalVariants} sprite refs total) -> '{target}'" +
                          (resolved.ExtraSignatureCount > 0
                              ? $" [{resolved.ExtraSignatureCount} extraSignatures ignored, outside corner16 scope]"
                              : string.Empty));
        }
    }
}
#endif
