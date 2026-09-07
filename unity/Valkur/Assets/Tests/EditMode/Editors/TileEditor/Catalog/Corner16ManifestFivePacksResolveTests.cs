using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Editor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Catalog
{
    /// <summary>
    /// Validates the offline pixel-analysis manifest at
    /// <c>tools/atlas/generated/tile_rulesets.json</c> — the data
    /// <see cref="TilesetRulesetImporter"/> converts into <c>TilesetRuleset</c>
    /// Corner16 slots — against the REAL Resources/Tiles/ sprite files on disk,
    /// the same way <c>TileCategoryDiskParityTests</c> re-derives ground truth
    /// from AssetDatabase rather than trusting production code's own claims.
    ///
    /// Does NOT invoke <see cref="TilesetRulesetImporter.Apply"/>: that writes
    /// new <c>ruleset.asset</c> files and mutates the shared
    /// <c>TerrainCatalog.asset</c> on disk, which a test suite must never do as
    /// a side effect of merely being run. <see cref="TilesetRulesetImporter.DryRun"/>
    /// is safe (read-only — <c>ApplyPack</c> and <c>AssetDatabase.SaveAssets</c>
    /// only run when <c>apply == true</c>) and is exercised directly below as an
    /// integration smoke test on top of the independent manifest/disk check.
    /// </summary>
    [TestFixture]
    public class Corner16ManifestFivePacksResolveTests
    {
        private const string TILES_ROOT = "Assets/_Project/Resources/Tiles";
        private const string EXPECTED_CORNER_ORDER = "NW,NE,SE,SW";

        private static readonly string[] KnownFivePacks =
            { "grass_dirt", "grass_rock", "rock_water", "sand_grass", "sand_rock" };

        private static string ManifestPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../../tools/atlas/generated/tile_rulesets.json"));

        private static Dictionary<string, object> LoadManifest()
        {
            Assert.IsTrue(File.Exists(ManifestPath), $"Manifest not found at '{ManifestPath}'.");
            var root = MiniJsonRuntime.Deserialize(File.ReadAllText(ManifestPath)) as Dictionary<string, object>;
            Assert.IsNotNull(root, "Manifest did not parse to a JSON object.");
            return root;
        }

        private static Dictionary<string, object> GetPacks(Dictionary<string, object> root)
        {
            Assert.IsTrue(root.TryGetValue("packs", out var packsObj), "Manifest has no 'packs' key.");
            var packs = packsObj as Dictionary<string, object>;
            Assert.IsNotNull(packs, "'packs' is not a JSON object.");
            Assert.Greater(packs.Count, 0, "Manifest declares zero packs.");
            return packs;
        }

        /// <summary>Same lookup <see cref="TilesetRulesetImporter"/> itself uses:
        /// search recursively under each of the pack's folders for an exact-basename Sprite.</summary>
        private static bool SpriteExistsOnDisk(string[] searchFolders, string spriteName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{spriteName} t:Sprite", searchFolders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), spriteName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The folders a pack's sprites may live in: its own, plus every folder its optional
        /// <c>sheetFolders</c> array names. A pack cut from several sheets shows one picker
        /// CATEGORY per sheet — a category is a folder — so its sheets sit beside it at the
        /// Tiles root while the ruleset stays single, because FindPaintRuleset resolves a
        /// terrain NAME to exactly one ruleset and a second pack claiming the same primary
        /// would be unreachable in silence.
        /// </summary>
        private static string[] SearchFoldersFor(string packName, Dictionary<string, object> packData)
        {
            var folders = new List<string> { $"{TILES_ROOT}/{packName}" };
            if (!packData.TryGetValue("sheetFolders", out object declaredObj)) return folders.ToArray();

            var declared = declaredObj as List<object>;
            Assert.IsNotNull(declared, $"[{packName}] 'sheetFolders' is present but is not an array.");

            foreach (var entry in declared)
            {
                string name = entry as string;
                Assert.IsFalse(string.IsNullOrWhiteSpace(name),
                    $"[{packName}] 'sheetFolders' holds an empty entry.");

                string folder = $"{TILES_ROOT}/{name}";
                Assert.IsTrue(AssetDatabase.IsValidFolder(folder),
                    $"[{packName}] 'sheetFolders' names '{name}', which is not a folder under " +
                    "Resources/Tiles/. The importer aborts the whole pack on this, and the message " +
                    "it prints names the 98 missing sprites rather than the typo.");

                if (!folders.Contains(folder)) folders.Add(folder);
            }
            return folders.ToArray();
        }

        [Test]
        public void Manifest_DeclaresTheFiveKnownPacks()
        {
            var packs = GetPacks(LoadManifest());
            var missing = KnownFivePacks.Where(p => !packs.ContainsKey(p)).ToList();
            Assert.IsEmpty(missing, "Expected pack(s) missing from the manifest: " + string.Join(", ", missing) +
                ". If this is intentional (a pack was dropped), update this test's KnownFivePacks list " +
                "deliberately — don't let it go stale silently.");
        }

        [Test]
        public void EveryPackInManifest_HasExactlySixteenUniqueCornerSignatures_InTheDocumentedOrder()
        {
            var packs = GetPacks(LoadManifest());
            foreach (var kv in packs)
            {
                var packData = kv.Value as Dictionary<string, object>;
                Assert.IsNotNull(packData, $"[{kv.Key}] pack entry is not a JSON object.");

                Assert.AreEqual(EXPECTED_CORNER_ORDER, packData["cornerOrder"] as string,
                    $"[{kv.Key}] cornerOrder must be '{EXPECTED_CORNER_ORDER}' to match Corner16Slot's bit layout.");

                var slots = packData["slots"] as Dictionary<string, object>;
                Assert.IsNotNull(slots, $"[{kv.Key}] missing 'slots' object.");
                Assert.AreEqual(16, slots.Count, $"[{kv.Key}] must declare exactly 16 slot keys.");

                var seenValues = new HashSet<byte>();
                foreach (var key in slots.Keys)
                {
                    Assert.AreEqual(4, key.Length, $"[{kv.Key}] slot key '{key}' must be a 4-character binary signature.");
                    byte value = Convert.ToByte(key, 2);
                    Assert.IsTrue(seenValues.Add(value), $"[{kv.Key}] slot key '{key}' collides with another key.");
                }
                CollectionAssert.AreEquivalent(Enumerable.Range(0, 16), seenValues.Select(v => (int)v),
                    $"[{kv.Key}] the 16 slot keys must cover every signature value 0..15 exactly once.");
            }
        }

        [Test]
        public void EveryPackInManifest_EverySpriteNameResolvesToARealAssetOnDisk()
        {
            var packs = GetPacks(LoadManifest());
            var failures = new List<string>();

            foreach (var kv in packs)
            {
                string packName = kv.Key;
                var packData = (Dictionary<string, object>)kv.Value;
                string packFolder = $"{TILES_ROOT}/{packName}";

                Assert.IsTrue(AssetDatabase.IsValidFolder(packFolder),
                    $"[{packName}] folder '{packFolder}' does not exist under Resources/Tiles/.");

                var searchFolders = SearchFoldersFor(packName, packData);

                var slots = (Dictionary<string, object>)packData["slots"];
                foreach (var slotKv in slots)
                {
                    var spriteNames = slotKv.Value as List<object>;
                    Assert.IsNotNull(spriteNames, $"[{packName}] slot '{slotKv.Key}' does not list an array of names.");
                    Assert.Greater(spriteNames.Count, 0, $"[{packName}] slot '{slotKv.Key}' lists zero sprite variants.");

                    foreach (var nameObj in spriteNames)
                    {
                        string name = nameObj as string;
                        if (string.IsNullOrEmpty(name) || !SpriteExistsOnDisk(searchFolders, name))
                            failures.Add($"[{packName}] slot {slotKv.Key}: '{name ?? "<null>"}'");
                    }
                }
            }

            Assert.IsEmpty(failures,
                $"{failures.Count} sprite reference(s) in the manifest do not resolve to a real Sprite asset " +
                "under Resources/Tiles/ (searched recursively): " + string.Join("; ", failures));
        }

        [Test]
        public void TilesetRulesetImporter_DryRun_AllPacksResolveWithoutAborting()
        {
            // Real production entry point, read-only (apply:false never writes an
            // asset or touches TerrainCatalog.asset). Any pack that fails to
            // resolve calls Debug.LogError, which fails this test automatically
            // via Unity's unhandled-error-log rule — no explicit assertion is
            // needed for the failure path, only for the expected success summary.
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(
                @"\[TilesetRulesetImporter\][\s\S]*totals: \d+ ok, 0 aborted"));
            TilesetRulesetImporter.DryRun();
        }
    }
}
