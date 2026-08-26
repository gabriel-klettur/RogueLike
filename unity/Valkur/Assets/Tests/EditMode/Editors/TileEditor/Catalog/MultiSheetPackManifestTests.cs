// A pack cut from SEVERAL sheets of the same terrain pair is a shape nothing pinned before
// this file. TilesheetManifestJoinIntegrityTests proves the join for castle_pandora, which is
// one sheet in one folder; grass_rock has shipped three sheets for months with no manifest at
// all, so it silently renders through the legacy flat-list path.
//
// The 2026-08-26 blob-island packs (dirt_sand, grass_sand, sand_ocean_3) are the first to hold
// several sheets AND a manifest. Their root _manifest.json is not written by the slicer — it is
// MERGED from the per-sheet ones by tools/atlas/migrate_tilesheet.py::rebuild_pack_manifest,
// which stacks sheet k into rows [offset, offset + rows_k) and re-keys uniqueId across the whole
// pack. Two things can go wrong there and neither throws:
//
//   * a row offset that fails to advance makes two sheets occupy the same rows. The picker then
//     draws them on top of each other and half the pack is unreachable, looking like missing art.
//   * a uniqueId re-key that collides across sheets makes "HIDE DUPS" hide a tile that is not a
//     duplicate — a variant vanishes from the picker while its file sits on disk.
//
// Both are invisible until an author goes looking for a tile that is not there, so they are
// asserted here against the RAW merged manifest and the live catalog, for every cell of every
// multi-sheet pack, discovered from disk rather than hardcoded so a fourth sheet is covered the
// day it lands.

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
    public class MultiSheetPackManifestTests
    {
        private const string TILES_ROOT = "Assets/_Project/Resources/Tiles";
        private const string MANIFEST_FILE = "_manifest.json";

        /// <summary>A pack whose manifest was merged from more than one `*_slices/` subfolder.</summary>
        private sealed class MultiSheetPack
        {
            public string Name;
            public TilesheetManifest Merged;
            public List<TilesheetManifest> Sheets;
        }

        private TileCatalog _catalog;
        private List<MultiSheetPack> _packs;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _catalog = TileCatalog.BuildFromResources();
            _packs = new List<MultiSheetPack>();

            foreach (string packFolder in AssetDatabase.GetSubFolders(TILES_ROOT))
            {
                var sheets = new List<TilesheetManifest>();
                foreach (string sub in AssetDatabase.GetSubFolders(packFolder))
                {
                    var sheet = LoadManifest(sub + "/" + MANIFEST_FILE);
                    if (sheet != null) sheets.Add(sheet);
                }
                if (sheets.Count < 2) continue; // single-sheet packs are covered elsewhere

                var merged = LoadManifest(packFolder + "/" + MANIFEST_FILE);
                Assert.IsNotNull(merged,
                    $"'{packFolder}' holds {sheets.Count} sheet manifests but has no merged " +
                    $"{MANIFEST_FILE} at its root. Re-run migrate_tilesheet.py so the pack gets one — " +
                    "without it the F8 picker falls back to the legacy flat list and loses the grid " +
                    "view, the (r, c) coordinates and the dedup toggle.");

                _packs.Add(new MultiSheetPack
                {
                    Name = Path.GetFileName(packFolder),
                    Merged = merged,
                    Sheets = sheets,
                });
            }

            Assert.IsNotEmpty(_packs,
                "No multi-sheet tile pack found under Resources/Tiles/. This fixture exists to guard " +
                "the merged-manifest path; if every pack really became single-sheet, delete the file " +
                "rather than leaving it passing vacuously.");
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

        private static TilesheetManifest LoadManifest(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset == null) return null;
            var parsed = JsonUtility.FromJson<TilesheetManifest>(asset.text);
            return parsed != null && parsed.cells != null ? parsed : null;
        }

        // ── The merge accounts for every sheet, losing and inventing nothing ─────────

        [Test]
        public void MergedManifest_CellCount_EqualsTheSumOfItsSheets()
        {
            foreach (var pack in _packs)
            {
                int expected = pack.Sheets.Sum(s => s.cells.Length);
                Assert.AreEqual(expected, pack.Merged.cells.Length,
                    $"{pack.Name}: merged manifest has {pack.Merged.cells.Length} cells but its " +
                    $"{pack.Sheets.Count} sheets hold {expected}. A merge that drops cells hides tiles " +
                    "from the picker; one that duplicates them draws the same tile twice.");
            }
        }

        [Test]
        public void MergedManifest_RowCount_EqualsTheSumOfItsSheets_AndColsAgree()
        {
            foreach (var pack in _packs)
            {
                Assert.AreEqual(pack.Sheets.Sum(s => s.rows), pack.Merged.rows,
                    $"{pack.Name}: merged 'rows' must be the sheets stacked, not one sheet's height.");

                foreach (var sheet in pack.Sheets)
                    Assert.AreEqual(pack.Merged.cols, sheet.cols,
                        $"{pack.Name}: a sheet declares {sheet.cols} cols but the pack is " +
                        $"{pack.Merged.cols}. Mixed widths misalign every row below the first sheet.");
            }
        }

        // ── No two cells share a slot, which is what a bad row offset produces ───────

        [Test]
        public void MergedManifest_EveryCell_OccupiesADistinctGridSlot()
        {
            foreach (var pack in _packs)
            {
                var occupant = new Dictionary<(int r, int c), string>();
                foreach (var cell in pack.Merged.cells)
                {
                    var key = (cell.r, cell.c);
                    Assert.IsFalse(occupant.ContainsKey(key),
                        $"{pack.Name}: '{cell.file}' and '{(occupant.TryGetValue(key, out var other) ? other : "?")}' " +
                        $"both claim slot r{cell.r} c{cell.c}. The sheets were stacked without advancing " +
                        "the row offset, so one of them is unreachable in the picker.");
                    occupant[key] = cell.file;
                }
            }
        }

        [Test]
        public void MergedManifest_EveryCell_StaysInsideTheDeclaredGrid()
        {
            foreach (var pack in _packs)
            {
                foreach (var cell in pack.Merged.cells)
                {
                    Assert.That(cell.r, Is.InRange(0, pack.Merged.rows - 1),
                        $"{pack.Name}: '{cell.file}' sits at row {cell.r}, outside the declared {pack.Merged.rows}.");
                    Assert.That(cell.c, Is.InRange(0, pack.Merged.cols - 1),
                        $"{pack.Name}: '{cell.file}' sits at col {cell.c}, outside the declared {pack.Merged.cols}.");
                }
            }
        }

        // ── uniqueId is the dedup toggle's whole contract ────────────────────────────

        [Test]
        public void MergedManifest_UniqueIds_AreContiguousAndMatchTheDeclaredUniquesCount()
        {
            foreach (var pack in _packs)
            {
                var ids = pack.Merged.cells.Select(c => c.uniqueId).Distinct().OrderBy(i => i).ToList();
                Assert.AreEqual(pack.Merged.uniques.Length, ids.Count,
                    $"{pack.Name}: cells reference {ids.Count} distinct uniqueIds but the manifest " +
                    $"declares {pack.Merged.uniques.Length} uniques.");
                for (int i = 0; i < ids.Count; i++)
                    Assert.AreEqual(i, ids[i],
                        $"{pack.Name}: uniqueIds must be 0..n-1 with no gaps; found {ids[i]} at index {i}.");
            }
        }

        [Test]
        public void MergedManifest_CellsSharingAUniqueId_ReallyAreTheSameTile()
        {
            foreach (var pack in _packs)
            {
                // The merger re-keys uniqueId from the per-cell SHA-256 the sheet manifests carry.
                // Re-derive the grouping from those hashes and require the two agree: a collision
                // would make "HIDE DUPS" hide a variant that is not a duplicate.
                var hashOf = new Dictionary<string, string>();
                foreach (var sheet in pack.Sheets)
                {
                    var idToHash = sheet.uniques.ToDictionary(u => u.id, u => u.hash);
                    foreach (var cell in sheet.cells)
                        if (idToHash.TryGetValue(cell.uniqueId, out string h))
                            hashOf[cell.file] = h;
                }

                var hashById = new Dictionary<int, string>();
                foreach (var cell in pack.Merged.cells)
                {
                    Assert.IsTrue(hashOf.TryGetValue(cell.file, out string hash),
                        $"{pack.Name}: merged cell '{cell.file}' is in no sheet manifest — the merge " +
                        "and the slices have diverged.");

                    if (!hashById.TryGetValue(cell.uniqueId, out string known))
                    {
                        hashById[cell.uniqueId] = hash;
                        continue;
                    }
                    Assert.AreEqual(known, hash,
                        $"{pack.Name}: '{cell.file}' shares uniqueId {cell.uniqueId} with a tile of a " +
                        "different hash. HIDE DUPS would hide one of them as a duplicate it is not.");
                }
            }
        }

        // ── The catalog actually consumes the merged manifest ────────────────────────

        [Test]
        public void Catalog_EveryTileOfAMultiSheetPack_CarriesTheMergedGridCoordinates()
        {
            foreach (var pack in _packs)
            {
                var byName = pack.Merged.cells.ToDictionary(c => c.file, c => c);
                var entries = _catalog.Entries.Where(e => e.category == pack.Name).ToList();

                Assert.AreEqual(byName.Count, entries.Count,
                    $"{pack.Name}: catalog holds {entries.Count} tiles for a manifest of {byName.Count} " +
                    "cells. A shortfall means sprite names collided with another category and lost " +
                    "TileCatalog's global first-occurrence-wins dedup.");

                foreach (var entry in entries)
                {
                    Assert.IsTrue(byName.TryGetValue(entry.tileName, out var cell),
                        $"{pack.Name}: catalog tile '{entry.tileName}' has no manifest cell.");
                    Assert.AreEqual(cell.r, entry.gridR,
                        $"{pack.Name}/{entry.tileName}: gridR {entry.gridR} != manifest row {cell.r}.");
                    Assert.AreEqual(cell.c, entry.gridC,
                        $"{pack.Name}/{entry.tileName}: gridC {entry.gridC} != manifest col {cell.c}.");
                    Assert.AreEqual(cell.uniqueId, entry.uniqueId,
                        $"{pack.Name}/{entry.tileName}: uniqueId {entry.uniqueId} != manifest {cell.uniqueId}.");
                }
            }
        }

        [Test]
        public void Catalog_EveryTileOfAMultiSheetPack_RendersAtExactlyOneWorldCell()
        {
            // The postprocessor derives PPU from the source size so any square tile fills one cell.
            // Asserting the RATIO rather than "32" keeps a legitimate 16-px pack from failing while
            // still catching the oversized-tile bug, where one cell renders as N x N units.
            foreach (var pack in _packs)
            {
                foreach (var entry in _catalog.Entries.Where(e => e.category == pack.Name))
                {
                    var sprite = entry.preview;
                    Assert.IsNotNull(sprite, $"{pack.Name}/{entry.tileName}: no preview sprite.");
                    Assert.AreEqual(sprite.rect.width, sprite.rect.height,
                        $"{pack.Name}/{entry.tileName}: sprite is {sprite.rect.width}x{sprite.rect.height}, not square.");
                    Assert.AreEqual(1f, sprite.rect.width / sprite.pixelsPerUnit, 0.0001f,
                        $"{pack.Name}/{entry.tileName}: {sprite.rect.width}px at PPU {sprite.pixelsPerUnit} " +
                        $"renders {sprite.rect.width / sprite.pixelsPerUnit} world units, not 1 map cell.");
                }
            }
        }
    }
}
