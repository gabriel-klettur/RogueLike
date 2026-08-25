// Fills two catalog-scale gaps the existing suite doesn't reach: TileCatalogAndRegistryTests
// only ever builds catalogs with a handful of synthetic entries (2-4 per test); no test file
// exercises category-partition correctness or the cache mechanics against the REAL project
// scale — 3,045 entries across 14 categories, the largest (castle_pandora) alone holding
// 2,688.
//
//   1. TileCatalog.BuildFromResources() at real scale still partitions entries by category
//      correctly, returns an independent list per call, and every entry ends up in exactly
//      one category bucket (no drops, no double-counts).
//
//   2. The `_byCategory` / `_categoriesCache` index — TileCatalog.cs:35's doc comment says
//      it exists specifically "to avoid the O(total) scan... every time GetTilesForCategory
//      is called" — is actually REUSED across repeated queries instead of silently rebuilt
//      every call (which would reintroduce exactly that per-call cost with nothing left to
//      catch it), and is correctly invalidated — not merely left stale — the instant
//      PopulateFromAssets mutates `entries`.
//
// No Stopwatch/wall-clock assertions here on purpose. At N≈3,000 even a naive per-call
// re-scan is comfortably fast enough that a timing budget generous enough to avoid CI
// flakiness would not reliably catch that regression anyway. The reflection-based identity
// checks below are deterministic and target the actual mechanism (cache object reuse)
// instead of its side effect (wall-clock speed) — see the "no quadratic cost found" note in
// the delivery report for the audit trail of what was checked.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Catalog
{
    [TestFixture]
    public class TileCatalogScaleAndCacheIntegrityTests
    {
        private const string CASTLE_PANDORA_FOLDER = "Assets/_Project/Resources/Tiles/castle_pandora";

        private static readonly FieldInfo ByCategoryField =
            typeof(TileCatalog).GetField("_byCategory", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CategoriesCacheField =
            typeof(TileCatalog).GetField("_categoriesCache", BindingFlags.NonPublic | BindingFlags.Instance);

        // Real, full-scale catalog — built once (Resources.LoadAll of ~3,077 sprites) and
        // shared read-only across every [Test] in this fixture; none of them mutate it.
        private TileCatalog _realCatalog;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Assert.IsNotNull(ByCategoryField, "Reflection target '_byCategory' missing on TileCatalog — rename?");
            Assert.IsNotNull(CategoriesCacheField, "Reflection target '_categoriesCache' missing on TileCatalog — rename?");
            _realCatalog = TileCatalog.BuildFromResources();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_realCatalog == null) return;
            foreach (var entry in _realCatalog.Entries)
                if (entry.tile != null) Object.DestroyImmediate(entry.tile);
            Object.DestroyImmediate(_realCatalog);
            _realCatalog = null;
        }

        // ── 1. Correctness at real scale ─────────────────────────────────────────────

        [Test]
        public void RealCatalog_IsActuallyAtFullProjectScale()
        {
            // Sanity guard for every other test in this file: if this ever drops to a
            // handful of entries, Resources/Tiles/ fixture data went missing and the
            // "at scale" tests below would be silently testing nothing.
            Assert.GreaterOrEqual(_realCatalog.Entries.Count, 3000,
                "Expected ~3,045 entries from the real Resources/Tiles/ tree; got far fewer — " +
                "fixture data is missing, or BuildFromResources() regressed.");
        }

        [Test]
        public void RealCatalog_SumOfPerCategoryQueries_EqualsTotalEntryCount()
        {
            int sum = _realCatalog.GetCategories().Sum(cat => _realCatalog.GetTilesForCategory(cat).Count);
            Assert.AreEqual(_realCatalog.Entries.Count, sum,
                "Every entry must belong to exactly one category bucket in the cached index — a " +
                "mismatch means EnsureIndex() is dropping or double-counting entries at scale.");
        }

        [Test]
        public void RealCatalog_CastlePandoraCategory_MatchesIndependentDiskSpriteCount()
        {
            int diskCount = AssetDatabase.FindAssets("t:Sprite", new[] { CASTLE_PANDORA_FOLDER }).Length;
            Assert.Greater(diskCount, 2000, "Sanity: castle_pandora must be the ~2,688-sprite tilesheet.");

            int catalogCount = _realCatalog.GetTilesForCategory("castle_pandora").Count;
            Assert.AreEqual(diskCount, catalogCount,
                "castle_pandora is processed first in TileCategoryManifestBuilder's descending-" +
                "sprite-count order (it is by far the largest category), so it can never lose an " +
                "entry to a cross-category name collision — its catalog count must equal its disk " +
                "sprite count exactly.");
        }

        [TestCase("castle_pandora")]
        [TestCase("grass_rock")]
        [TestCase("basics")]
        public void RealCatalog_GetTilesForCategory_ReturnsOnlyThatCategory_AtScale(string category)
        {
            var tiles = _realCatalog.GetTilesForCategory(category);
            Assert.Greater(tiles.Count, 0, $"Sanity: '{category}' must be non-empty.");
            Assert.IsTrue(tiles.All(t => t.category == category),
                $"GetTilesForCategory('{category}') returned entr(y/ies) from a different category — " +
                "index partitioning is broken at scale.");
        }

        [Test]
        public void RealCatalog_GetTilesForCategory_ReturnsIndependentListPerCall_AtScale()
        {
            var first = _realCatalog.GetTilesForCategory("castle_pandora");
            int expectedCount = first.Count;
            first.Clear(); // mutate the list handed back by the first call

            var second = _realCatalog.GetTilesForCategory("castle_pandora");
            Assert.AreEqual(expectedCount, second.Count,
                "GetTilesForCategory must return a fresh defensive copy each call — mutating the " +
                "list one caller received (e.g. the picker's PopulateTileGrid, which does its own " +
                "in-place Sort() on tilesheet categories) must not corrupt what the next caller sees.");
        }

        // ── 2. Index cache reuse across repeated queries ─────────────────────────────

        [Test]
        public void EnsureIndex_BuildsCategoryDictionaryOnce_ReusedAcrossManyRepeatedQueries()
        {
            _realCatalog.GetCategories(); // force EnsureIndex()
            var dictRef = ByCategoryField.GetValue(_realCatalog);
            Assert.IsNotNull(dictRef, "EnsureIndex() must populate _byCategory on first query.");

            var categories = _realCatalog.GetCategories();
            for (int i = 0; i < 5; i++)
                foreach (var cat in categories)
                    _realCatalog.GetTilesForCategory(cat);

            var dictRefAfter = ByCategoryField.GetValue(_realCatalog);
            Assert.AreSame(dictRef, dictRefAfter,
                "The cached _byCategory dictionary must be the SAME object across ~70 repeated " +
                "category queries — if this fails, EnsureIndex() is rebuilding on every call, which " +
                "reintroduces exactly the O(total)-per-call cost the cache exists to avoid (see the " +
                "TileCatalog.cs:35 doc comment).");
        }

        // ── 3. Invalidation on mutation ───────────────────────────────────────────────

        private static List<TileCatalog.TileEntry> MakeEntries(string category, int count, List<Object> trash)
        {
            var list = new List<TileCatalog.TileEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = $"{category}_{i}";
                trash.Add(tile);
                list.Add(new TileCatalog.TileEntry
                {
                    category = category,
                    tileName = tile.name,
                    tile = tile,
                });
            }
            return list;
        }

        private static void PopulateCatalog(TileCatalog cat, List<TileCatalog.TileEntry> entries)
        {
#if UNITY_EDITOR
            cat.PopulateFromAssets(entries);
#else
            var field = typeof(TileCatalog).GetField("entries",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(cat, entries);
#endif
        }

        [Test]
        public void PopulateFromAssets_InvalidatesCache_SoRepopulatedCatalogNeverLeaksStaleCategories()
        {
            var trash = new List<Object>();
            var cat = ScriptableObject.CreateInstance<TileCatalog>();
            trash.Add(cat);
            try
            {
                var before = new List<TileCatalog.TileEntry>();
                for (int c = 0; c < 5; c++)
                    before.AddRange(MakeEntries($"catA{c}", 50, trash));
                PopulateCatalog(cat, before);

                Assert.AreEqual(50, cat.GetTilesForCategory("catA0").Count); // forces EnsureIndex()
                var dictBefore = ByCategoryField.GetValue(cat);
                Assert.IsNotNull(dictBefore);

                var after = new List<TileCatalog.TileEntry>();
                for (int c = 0; c < 5; c++)
                    after.AddRange(MakeEntries($"catB{c}", 80, trash));
                PopulateCatalog(cat, after);

                // Invalidation must be immediate — before any query forces a rebuild.
                Assert.IsNull(ByCategoryField.GetValue(cat),
                    "PopulateFromAssets must invalidate _byCategory synchronously (InvalidateIndex()), " +
                    "not lazily on next read.");
                Assert.IsNull(CategoriesCacheField.GetValue(cat),
                    "PopulateFromAssets must invalidate _categoriesCache synchronously.");

                Assert.AreEqual(0, cat.GetTilesForCategory("catA0").Count,
                    "A category from before PopulateFromAssets must not leak into the rebuilt index.");
                Assert.AreEqual(80, cat.GetTilesForCategory("catB0").Count,
                    "A category from the new PopulateFromAssets call must be queryable after invalidation.");

                var dictAfter = ByCategoryField.GetValue(cat);
                Assert.IsNotNull(dictAfter);
                Assert.AreNotSame(dictBefore, dictAfter,
                    "The rebuilt index must be a NEW dictionary object, not the old one mutated in " +
                    "place — reusing the old object risks leftover buckets from stale categories.");

                CollectionAssert.DoesNotContain(cat.GetCategories(), "catA0",
                    "GetCategories() must not list a category that no longer has any entries.");
            }
            finally
            {
                foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            }
        }
    }
}
