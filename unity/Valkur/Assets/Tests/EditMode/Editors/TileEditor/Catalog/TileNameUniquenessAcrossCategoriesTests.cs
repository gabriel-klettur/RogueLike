// Pins the collision-drop behaviour of TileCatalog.BuildFromResources()'s single GLOBAL
// `HashSet<string> seen` deduplication pass (TileCatalog.cs:108): when two sprite files in
// DIFFERENT category folders share the same file name (no extension), the second one
// encountered is silently discarded from `entries` — no log, no warning, no way for an
// author to know that tile vanished from the F8 picker.
//
// At the project's real scale this is not theoretical: Resources/Tiles/ ships 3,077 PNG
// files but TileCatalog.BuildFromResources() produces exactly 3,045 entries — the "3,045
// entries" figure this audit's own scale requirement cites is ALREADY net of 32 silently
// dropped collisions, every one of them a "rock_grass_32_*" file name that exists
// identically in both grass_rock/ and rock_grass/. grass_rock sorts first (larger folder →
// earlier in TileCategoryManifestBuilder's descending sprite-count order), so every
// rock_grass/rock_grass_32_*.png is the one that silently disappears.
//
// This suite does not "fix" the collision — that is production code, out of scope here —
// it pins the CURRENT, MEASURED shape of the problem so a future change that makes it
// WORSE (more collisions, or — far more serious — a collision INSIDE a tilesheet category
// where it would corrupt the gridR/gridC/uniqueId join instead of merely hiding a legacy
// tile) fails loudly instead of shipping silently, which is exactly the failure mode this
// regression-tests against.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Catalog
{
    [TestFixture]
    public class TileNameUniquenessAcrossCategoriesTests
    {
        private const string TILES_ROOT = "Assets/_Project/Resources/Tiles";

        // Measured against the real Resources/Tiles/ tree (2026-08-25, see class doc): 32
        // sprite names collide across categories, all "rock_grass_32_*" vs "grass_rock/*".
        // A regression that makes this WORSE (more collisions appear) fails this test; a
        // fix that makes it BETTER (fewer/zero) does not — lower this constant when that
        // happens so the baseline stays meaningful.
        private const int KnownMaxCollisionCount = 32;

        private TileCatalog _catalog;

        [OneTimeSetUp]
        public void OneTimeSetUp() => _catalog = TileCatalog.BuildFromResources();

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_catalog == null) return;
            foreach (var entry in _catalog.Entries)
                if (entry.tile != null) UnityEngine.Object.DestroyImmediate(entry.tile);
            UnityEngine.Object.DestroyImmediate(_catalog);
            _catalog = null;
        }

        /// <summary>
        /// Replays TileCatalog.BuildFromResources()'s exact global-dedup algorithm against a
        /// fresh AssetDatabase scan: same category order (read from the SAME baked manifest
        /// via Resources.Load — the exact source production reads, not re-derived, so a test
        /// bug can't diverge from what BuildFromResources actually iterates), same
        /// "first-occurrence-wins" rule. Returns the surviving count and every (category,
        /// name) pair that lost its collision.
        /// </summary>
        private static (int survivingCount, List<(string category, string name)> dropped) SimulateGlobalDedup()
        {
            var manifestAsset = Resources.Load<TextAsset>("Tiles/_categories");
            Assert.IsTrue(manifestAsset != null, "Tiles/_categories manifest missing — fixture broken.");
            var manifest = JsonUtility.FromJson<TileCategoryManifest>(manifestAsset.text);
            Assert.IsNotNull(manifest);

            var seen = new HashSet<string>();
            var dropped = new List<(string category, string name)>();
            int surviving = 0;

            foreach (var cat in manifest.folderCategories ?? Array.Empty<string>())
            {
                string folder = $"{TILES_ROOT}/{cat}";
                var names = AssetDatabase.FindAssets("t:Sprite", new[] { folder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.Ordinal); // deterministic within-category order

                foreach (var name in names)
                {
                    if (!seen.Add(name)) { dropped.Add((cat, name)); continue; }
                    surviving++;
                }
            }

            string synthetic = string.IsNullOrEmpty(manifest.syntheticRootCategory)
                ? "basics" : manifest.syntheticRootCategory;
            foreach (var rootFile in manifest.rootFiles ?? Array.Empty<string>())
            {
                if (!seen.Add(rootFile)) { dropped.Add((synthetic, rootFile)); continue; }
                surviving++;
            }

            return (surviving, dropped);
        }

        [Test]
        public void BuildFromResources_EntryCount_MatchesIndependentlySimulatedGlobalDedup()
        {
            var (survivingCount, dropped) = SimulateGlobalDedup();

            Assert.AreEqual(survivingCount, _catalog.Entries.Count,
                "TileCatalog.BuildFromResources() produced a different entry count than an " +
                "independent replay of its own documented dedup algorithm (first-seen sprite name " +
                "wins, globally across every category in the manifest's order). Either the " +
                $"algorithm changed (update this test) or something else is silently " +
                $"dropping/duplicating entries. Simulated {dropped.Count} collision(s).");
        }

        [Test]
        public void BuildFromResources_CrossCategoryNameCollisions_DoNotExceedKnownBaseline()
        {
            var (_, dropped) = SimulateGlobalDedup();

            string details = string.Join("\n", dropped.Take(40).Select(d => $"  {d.category}/{d.name}"));
            Assert.LessOrEqual(dropped.Count, KnownMaxCollisionCount,
                $"{dropped.Count} sprite file names now collide across different Resources/Tiles/ " +
                $"categories (baseline was {KnownMaxCollisionCount}). Every collision silently " +
                "removes a tile from TileCatalog.entries with no log — it becomes unreachable from " +
                "the F8 picker AND, if resolved by bare file name elsewhere (OverlayLoader's legacy " +
                "no-folder-prefix path), can resolve to the WRONG category's sprite. New/changed " +
                "collision set:\n" + details);
        }

        [Test]
        public void BuildFromResources_WithinCastlePandora_NoInternalNameCollisions()
        {
            // castle_pandora is by far the largest category (2,688 sprites) and is processed
            // FIRST (largest sprite count sorts first) — it can never lose an entry to another
            // category, but a WITHIN-folder collision would still be silently dropped by the
            // same global `seen` set, and would corrupt the tilesheet join (two grid cells
            // claiming one gridR/gridC), not just hide a legacy tile. Checked separately from
            // the cross-category baseline above because this failure mode is worse.
            var tiles = _catalog.GetTilesForCategory("castle_pandora");
            var names = new HashSet<string>();
            var dupes = new List<string>();
            foreach (var t in tiles)
                if (!names.Add(t.tileName)) dupes.Add(t.tileName);

            Assert.IsEmpty(dupes,
                "castle_pandora has internal duplicate sprite file name(s), which the catalog's " +
                "global dedup would silently drop and corrupt the tilesheet grid join: " +
                string.Join(", ", dupes));
        }
    }
}
