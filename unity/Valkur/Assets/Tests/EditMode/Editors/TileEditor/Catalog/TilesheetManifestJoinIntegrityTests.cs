// TilesheetManifest (Resources/Tiles/<cat>/_manifest.json) has zero test coverage anywhere
// in the suite before this file — grep for the type across Tests/ turns up nothing. It is
// the join key that lets TileCatalog.BuildFromResources() enrich each entry of a sliced
// tilesheet category with real gridR/gridC/uniqueId/transparent instead of the -1/-1/-1/
// false default, which is what drives the F8 picker's "tileset view" (original sheet
// layout) and the "HIDE DUPS" toggle (TileEditorUI.TilesetView.cs).
//
// castle_pandora is the only category this join has ever had to prove itself at real scale
// on: 2,688 cells, a 56x48 grid, 154 declared unique tiles. A parsing regression or a subtle
// off-by-one in the join would degrade the picker silently — cells landing at gridR/gridC =
// -1 (falling back to the legacy flat-list layout) or landing at the WRONG coordinate
// (scrambling the sheet's visual layout) without throwing anything.
//
// Every assertion here is checked against the RAW _manifest.json file, re-parsed
// independently of TileCatalog's own cellLookup construction, for every one of the 2,688
// cells — not a handful of spot checks.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Catalog
{
    [TestFixture]
    public class TilesheetManifestJoinIntegrityTests
    {
        private const string CASTLE_PANDORA_MANIFEST_PATH =
            "Assets/_Project/Resources/Tiles/castle_pandora/_manifest.json";

        private TileCatalog _catalog;
        private TilesheetManifest _rawManifest;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _catalog = TileCatalog.BuildFromResources();

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(CASTLE_PANDORA_MANIFEST_PATH);
            Assert.IsTrue(asset != null, $"{CASTLE_PANDORA_MANIFEST_PATH} is missing — fixture broken.");
            _rawManifest = JsonUtility.FromJson<TilesheetManifest>(asset.text);
            Assert.IsNotNull(_rawManifest, "Failed to parse castle_pandora/_manifest.json.");
            Assert.IsNotNull(_rawManifest.cells, "Manifest has no 'cells' array.");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_catalog == null) return;
            foreach (var entry in _catalog.Entries)
                if (entry.tile != null) Object.DestroyImmediate(entry.tile);
            Object.DestroyImmediate(_catalog);
            _catalog = null;
        }

        // ── Full-scale join correctness (2,688 cells) ────────────────────────────────

        [Test]
        public void CastlePandora_EveryManifestCell_JoinsToACatalogEntry_WithMatchingGridCoordinates()
        {
            var byFileName = _catalog.GetTilesForCategory("castle_pandora")
                .ToDictionary(e => e.tileName, e => e);

            Assert.AreEqual(_rawManifest.cells.Length, byFileName.Count,
                "TileCatalog's castle_pandora category entry count must match the manifest's cell " +
                "count exactly — a mismatch means the join silently dropped or duplicated cells at " +
                "the 2,688-cell scale.");

            var mismatches = new List<string>();
            foreach (var cell in _rawManifest.cells)
            {
                if (!byFileName.TryGetValue(cell.file, out var entry))
                {
                    mismatches.Add($"{cell.file}: missing from catalog");
                    continue;
                }
                if (entry.gridR != cell.r || entry.gridC != cell.c ||
                    entry.uniqueId != cell.uniqueId || entry.transparent != cell.transparent)
                {
                    mismatches.Add($"{cell.file}: catalog=(r={entry.gridR},c={entry.gridC}," +
                        $"id={entry.uniqueId},transparent={entry.transparent}) manifest=(r={cell.r}," +
                        $"c={cell.c},id={cell.uniqueId},transparent={cell.transparent})");
                }
            }

            Assert.IsEmpty(mismatches,
                $"{mismatches.Count} of {_rawManifest.cells.Length} castle_pandora cells joined " +
                "incorrectly (showing up to 20):\n" + string.Join("\n", mismatches.Take(20)));
        }

        [Test]
        public void CastlePandora_GridCoordinates_StayWithinManifestDeclaredBounds()
        {
            var tiles = _catalog.GetTilesForCategory("castle_pandora");
            Assert.Greater(tiles.Count, 0);

            foreach (var t in tiles)
            {
                Assert.That(t.gridR, Is.InRange(0, _rawManifest.rows - 1),
                    $"{t.tileName}: gridR {t.gridR} outside manifest rows [0,{_rawManifest.rows - 1}].");
                Assert.That(t.gridC, Is.InRange(0, _rawManifest.cols - 1),
                    $"{t.tileName}: gridC {t.gridC} outside manifest cols [0,{_rawManifest.cols - 1}].");
            }
        }

        [Test]
        public void CastlePandora_DistinctUniqueIds_MatchManifestDeclaredUniquesCount()
        {
            var tiles = _catalog.GetTilesForCategory("castle_pandora");
            var distinctIds = new HashSet<int>(tiles.Select(t => t.uniqueId).Where(id => id >= 0));

            Assert.IsNotNull(_rawManifest.uniques,
                "Sanity: castle_pandora manifest must declare its 'uniques' list.");
            Assert.AreEqual(_rawManifest.uniques.Length, distinctIds.Count,
                "The distinct uniqueId set the catalog join produces must match the manifest's own " +
                "declared unique-tile count — this is what drives the F8 'HIDE DUPS' toggle; a " +
                "mismatch means some duplicates would render as real slots (or vice versa) at scale.");
        }

        // ── Negative case: a category without a manifest must not inherit stale coordinates ──

        [Test]
        public void SyntheticRootCategory_HasNoManifest_SoEveryEntryDefaultsToMinusOne()
        {
            var basics = _catalog.GetTilesForCategory("basics");
            Assert.Greater(basics.Count, 0, "Sanity: 'basics' synthetic category must be non-empty.");

            foreach (var t in basics)
            {
                Assert.AreEqual(-1, t.gridR,
                    $"{t.tileName}: 'basics' has no _manifest.json — gridR must default to -1.");
                Assert.AreEqual(-1, t.gridC,
                    $"{t.tileName}: 'basics' has no _manifest.json — gridC must default to -1.");
                Assert.AreEqual(-1, t.uniqueId,
                    $"{t.tileName}: 'basics' has no _manifest.json — uniqueId must default to -1.");
                Assert.IsFalse(t.transparent,
                    $"{t.tileName}: 'basics' has no _manifest.json — transparent must default to false.");
            }
        }
    }
}
