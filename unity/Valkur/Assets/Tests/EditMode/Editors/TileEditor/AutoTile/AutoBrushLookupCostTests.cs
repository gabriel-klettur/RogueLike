// What the AUTO brush costs per stroke, and the shape of the cost.
//
// AUTO asks "which pack and which slot is this sprite" four times per VERTEX plus once per cell,
// and it runs on every frame of a drag. The lookup used to SCAN — the catalog's rulesets and
// their slots, then all 3,765 picker entries twice to resolve a duplicate sheet cell — at about
// 0.19 ms a call. Per call that is nothing; per stroke it was the whole frame. Measured on the
// shipped project before the index:
//
//     1x1   13.4 ms      4x4   44.1 ms
//     2x2   20.8 ms      5x5   60.9 ms
//     3x3   30.6 ms      8x8  113.5 ms      i.e. 9 fps while painting
//
// and after it, 0.89 / 1.47 / 1.12 / 1.15 / 1.61 / 1.19 — flat rather than quadratic, because
// the per-lookup cost went from ~190 us to ~0.8 us.
//
// The numeric guards below are deliberately loose. A test machine is not a promise about
// milliseconds, and a tight bound here would fail for reasons that have nothing to do with the
// code. What they pin is the SHAPE: a lookup that is O(1) rather than a scan, and a stroke whose
// cost does not grow with the square of the brush. Both are what regressed, and both survive a
// slow machine.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.AutoTile
{
    [TestFixture]
    public class AutoBrushLookupCostTests
    {
        private readonly List<Object> _created = new List<Object>();
        private TerrainCatalog _catalog;
        private TileCatalog _tiles;
        private TilesetRuleset _pack;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _catalog = Resources.Load<TerrainCatalog>("TerrainCatalog");
            Assert.IsNotNull(_catalog);
            _tiles = TileCatalog.BuildFromResources();
            _pack = _catalog.Rulesets.FirstOrDefault(r =>
                r != null && r.Model == AutoTileModel.Corner16 && r.CornerSlots.Count == 16 &&
                !string.IsNullOrEmpty(r.TerrainSecondary));
            Assert.IsNotNull(_pack);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_tiles != null)
                foreach (var e in _tiles.Entries)
                    if (e.tile != null) Object.DestroyImmediate(e.tile);
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            AutoBrushPackLookup.InvalidateIndex();
        }

        private Sprite SpriteFor(byte mask) => _pack.CornerSlots
            .Where(s => (byte)s.slot == mask && s.variants != null)
            .SelectMany(s => s.variants).First(v => v != null);

        private Tilemap NewTilemap()
        {
            var gridGo = new GameObject("Grid");
            _created.Add(gridGo);
            gridGo.AddComponent<Grid>();
            var tilemapGo = new GameObject("Tilemap");
            tilemapGo.transform.SetParent(gridGo.transform);
            _created.Add(tilemapGo);
            return tilemapGo.AddComponent<Tilemap>();
        }

        private void PaintHardCut(Tilemap map, int size, int seamX)
        {
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                map.SetTile(new Vector3Int(x, y, 0),
                    TerrainTileResolver.ResolveTile(SpriteFor(x < seamX ? (byte)0 : (byte)15)));
        }

        [Test]
        public void TheLookupIsAnIndex_NotAScan()
        {
            // A scan's cost rises with the catalog; an index's does not. Comparing a sprite the
            // ruleset NAMES against a duplicate sheet cell (which the scan resolved by walking
            // every entry twice) is what separates the two: under a scan the duplicate was far
            // more expensive, under an index they are the same lookup.
            var named = SpriteFor(15);
            var duplicate = _tiles.Entries
                .Where(e => e.preview != null && e.uniqueId >= 0)
                .Select(e => e.preview)
                .FirstOrDefault(sp => !_pack.CornerSlots.SelectMany(s => s.variants ?? new Sprite[0])
                                           .Any(v => v == sp) &&
                                      AutoBrushPackLookup.FindPackAndSlot(_catalog, sp, _tiles).Ruleset != null);
            if (duplicate == null) Assert.Ignore("No duplicate sheet cell ships for this pack.");

            AutoBrushPackLookup.FindPackAndSlot(_catalog, named, _tiles); // build the index

            double Cost(Sprite sprite)
            {
                const int Reps = 2000;
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < Reps; i++) AutoBrushPackLookup.FindPackAndSlot(_catalog, sprite, _tiles);
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds / Reps;
            }

            double namedCost = Cost(named);
            double duplicateCost = Cost(duplicate);

            Assert.Less(namedCost, 0.05,
                $"a hit should be a dictionary read, measured {namedCost * 1000:F1} us");
            Assert.Less(duplicateCost, 0.05,
                "a duplicate sheet cell must cost the same as a named one — resolving it by " +
                $"walking the picker catalog is what cost 0.19 ms a call. Measured {duplicateCost * 1000:F1} us");
        }

        [Test]
        public void AStrokesCost_DoesNotGrowWithTheSquareOfTheBrush()
        {
            // The regression's signature: 1x1 at 13.4 ms and 8x8 at 113.5 ms, because the work
            // was (size+3)^2 lookups each walking the catalog. With an index the lookups are
            // still quadratic in count but each is free, so the stroke stays flat.
            const int Field = 24;
            var map = NewTilemap();
            var terrain = new TerrainMap();

            double Measure(int size)
            {
                var rect = new BoundsInt(Field / 2 - 1, Field / 2 - 1, 0, size, size, 1);
                PaintHardCut(map, Field, Field / 2);
                TerrainPainter.ConnectRegion(map, rect, _pack, _tiles, new TerrainMap(), _pack.TerrainSecondary);

                const int Reps = 6;
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < Reps; i++)
                {
                    PaintHardCut(map, Field, Field / 2);
                    TerrainPainter.ConnectRegion(map, rect, _pack, _tiles, new TerrainMap(), _pack.TerrainSecondary);
                }
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds / Reps;
            }

            double small = Measure(1);
            double large = Measure(8);

            Assert.Less(large, 16.0,
                $"an 8x8 stroke must fit in a frame; measured {large:F2} ms. Before the index it " +
                "was 113.5 ms, i.e. 9 fps while painting.");
            Assert.Less(large, small * 8.0,
                $"cost must not grow with the square of the brush: 1x1 {small:F2} ms vs 8x8 " +
                $"{large:F2} ms. An 8x8 does 64x the cells of a 1x1, so anything near that ratio " +
                "means the per-cell work is scanning again.");
        }

        [Test]
        public void TheIndexIsDropped_WhenTheCatalogsChange()
        {
            // A cached answer that outlives a ruleset re-import is worse than no cache: it
            // silently paints from the pack the author replaced.
            var sprite = SpriteFor(15);
            Assert.AreSame(_pack, AutoBrushPackLookup.FindPackAndSlot(_catalog, sprite, _tiles).Ruleset);

            AutoBrushPackLookup.InvalidateIndex();
            Assert.AreSame(_pack, AutoBrushPackLookup.FindPackAndSlot(_catalog, sprite, _tiles).Ruleset,
                "and it must rebuild correctly after being dropped");

            // A different TileCatalog instance is a different index, not a stale hit.
            var other = TileCatalog.BuildFromResources();
            try
            {
                Assert.AreSame(_pack, AutoBrushPackLookup.FindPackAndSlot(_catalog, sprite, other).Ruleset);
            }
            finally
            {
                foreach (var e in other.Entries)
                    if (e.tile != null) Object.DestroyImmediate(e.tile);
            }
        }
    }
}
