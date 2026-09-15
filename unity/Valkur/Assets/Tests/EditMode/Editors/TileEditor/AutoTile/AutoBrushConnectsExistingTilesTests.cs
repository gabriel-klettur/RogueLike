// What AUTO does: it ANALYSES the tiles already on the map inside its footprint and joins them.
//
// The verb matters and it is not the one the tool started with. PaintRegion STAMPS a terrain over
// an area — that is how ground gets laid down, and it replaces both sides of whatever it crosses.
// The author's ask was the other one: they place the tiles they want with the ordinary brush, and
// AUTO resolves the SEAM between two areas of the same pack so the hard cut becomes the pack's own
// boundary art.
//
// That is only possible because a Corner16 slot is the pack's own statement about which of a
// tile's four corners are the secondary terrain, so a placed tile hands back all four of its
// corner terrains exactly — no inference. Measured on the shipped water pack, all 16 slots
// round-trip from the placed sprite, duplicate sheet cells included.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.AutoTile
{
    [TestFixture]
    public class AutoBrushConnectsExistingTilesTests
    {
        private const byte PurePrimary = 0;
        private const byte PureSecondary = 15;

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
            Assert.IsNotNull(_pack, "A populated Corner16 pack must ship.");
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

        /// <summary>The slot of whatever tile sits at that cell, or -1 when it holds none of this pack.</summary>
        private int SlotAt(Tilemap map, int x, int y)
        {
            var sprite = (map.GetTile(new Vector3Int(x, y, 0)) as Tile)?.sprite;
            if (sprite == null) return -1;
            var found = AutoBrushPackLookup.FindPackAndSlot(_catalog, sprite, _tiles);
            return found.Ruleset == _pack ? found.SlotMask : -1;
        }

        /// <summary>Hand-paints solid primary left of <paramref name="seamX"/> and solid secondary
        /// from it rightward — the hard cut an author gets from the ordinary brush.</summary>
        private Tilemap HardCut(int width, int height, int seamX)
        {
            var map = NewTilemap();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                map.SetTile(new Vector3Int(x, y, 0),
                    TerrainTileResolver.ResolveTile(SpriteFor(x < seamX ? PurePrimary : PureSecondary)));
            return map;
        }

        // ── The recovery the whole thing rests on ────────────────────────────────

        [Test]
        public void EveryPlacedTile_HandsBackItsFourCornerTerrains()
        {
            // No inference: the slot IS the corner assignment. If this ever stops round-tripping,
            // every seam AUTO draws is decided from something other than what is on screen.
            //
            // EVERY VARIANT of EVERY populated pack, not one per slot of one pack. A pack has as
            // many variants per slot as it has sheets — grass_dirt now carries six — and a wave
            // whose sprites the lookup cannot recognise is silently unusable by AUTO: the tiles
            // appear in the picker, paint fine with the ordinary brush, and then contribute no
            // vote and get no seam. Nothing reports it.
            int checked_ = 0;
            foreach (var pack in _catalog.Rulesets.Where(r =>
                         r != null && r.Model == AutoTileModel.Corner16 && r.CornerSlots.Count == 16 &&
                         !string.IsNullOrEmpty(r.TerrainSecondary)))
            foreach (var slot in pack.CornerSlots)
            {
                if (slot.variants == null) continue;
                foreach (var sprite in slot.variants)
                {
                    if (sprite == null) continue;
                    checked_++;

                    var found = AutoBrushPackLookup.FindPackAndSlot(_catalog, sprite, _tiles);
                    Assert.AreSame(pack, found.Ruleset,
                        $"[{pack.FolderName}] slot {slot.slot} variant '{sprite.name}' resolved to " +
                        $"'{found.Ruleset?.FolderName ?? "(none)"}'");
                    Assert.AreEqual((byte)slot.slot, found.SlotMask,
                        $"[{pack.FolderName}] variant '{sprite.name}' did not round-trip its own slot");
                }
            }

            Assert.Greater(checked_, 16,
                "fixture sanity: this must sweep every pack's every variant, not one tile per slot");
        }

        // ── Joining two areas ────────────────────────────────────────────────────

        [Test]
        public void AStrokeAlongASeam_TurnsTheHardCutIntoBoundaryArt()
        {
            var map = HardCut(8, 4, seamX: 4);

            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++)
                Assert.IsTrue(SlotAt(map, x, y) == PurePrimary || SlotAt(map, x, y) == PureSecondary,
                    "the starting map is two solid areas butted together");

            var terrain = new TerrainMap();
            for (int y = 3; y >= 1; y--)
                TerrainPainter.ConnectRegion(map, new BoundsInt(3, y - 1, 0, 2, 2, 1),
                    _pack, _tiles, terrain, _pack.TerrainSecondary);

            var seam = new HashSet<int>();
            for (int y = 0; y < 4; y++) seam.Add(SlotAt(map, 3, y));

            Assert.AreEqual(1, seam.Count, "the whole seam resolves the same way");
            int slot = seam.First();
            Assert.AreNotEqual(PurePrimary, slot, "the seam must stop being solid primary");
            Assert.AreNotEqual(PureSecondary, slot, "and must not simply be overwritten with the other terrain");
            Assert.AreEqual(2, CountBits((byte)slot),
                "a vertical seam is a half-and-half tile: exactly two of its corners are secondary");
        }

        [Test]
        public void AStrokeDeepInsideOneArea_ChangesNothing()
        {
            // AUTO analyses; where there is nothing to join it must be a no-op. A tool that
            // repaints solid ground on every pass is one an author cannot use to touch up.
            var map = HardCut(8, 4, seamX: 4);
            var terrain = new TerrainMap();

            var result = TerrainPainter.ConnectRegion(map, new BoundsInt(6, 1, 0, 2, 2, 1),
                _pack, _tiles, terrain, _pack.TerrainSecondary);

            Assert.IsEmpty(result.TileEdits,
                "nothing to join inside a solid area, so nothing may change.");
        }

        [Test]
        public void ItNeverInventsABoundaryInsideSolidGround()
        {
            // The trap this guards: an UNDECIDED vertex reads as the primary terrain, so a cell
            // beside the footprint with one unknown corner would be handed an edge through solid
            // ground. Measured before the fix, a stroke down a seam turned a column two cells
            // INSIDE the secondary area into a half-and-half tile.
            var map = HardCut(10, 4, seamX: 4);
            var terrain = new TerrainMap();

            for (int y = 3; y >= 1; y--)
                TerrainPainter.ConnectRegion(map, new BoundsInt(3, y - 1, 0, 2, 2, 1),
                    _pack, _tiles, terrain, _pack.TerrainSecondary);

            for (int y = 0; y < 4; y++)
            for (int x = 5; x < 10; x++)
                Assert.AreEqual(PureSecondary, SlotAt(map, x, y),
                    $"cell ({x},{y}) is deep inside the secondary area and must stay solid");

            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 3; x++)
                Assert.AreEqual(PurePrimary, SlotAt(map, x, y),
                    $"cell ({x},{y}) is deep inside the primary area and must stay solid");
        }

        [Test]
        public void TheSelectedTile_DecidesWhichWayTheSeamLeans()
        {
            // Along a hard cut every vertex of the seam is a 2-2 tie, so the tie-break is what
            // places the new boundary. Making it the author's selected terrain is what turns an
            // arbitrary rule into a choice they make by picking a tile.
            int SeamSlotWhenPreferring(string preferred)
            {
                var map = HardCut(8, 4, seamX: 4);
                var terrain = new TerrainMap();
                TerrainPainter.ConnectRegion(map, new BoundsInt(3, 1, 0, 2, 2, 1),
                    _pack, _tiles, terrain, preferred);
                return SlotAt(map, 3, 2);
            }

            int leaningSecondary = SeamSlotWhenPreferring(_pack.TerrainSecondary);
            int leaningPrimary = SeamSlotWhenPreferring(_pack.TerrainPrimary);

            Assert.AreNotEqual(leaningSecondary, leaningPrimary,
                "the selected tile must change where the boundary lands, or picking one is inert");
        }

        [Test]
        public void ATileFromAnotherPack_IsNotCountedAsAVote()
        {
            // A neighbouring pack's tile says nothing about THIS pack's corners. Counting it
            // would let unrelated scenery drag a seam around.
            var other = _catalog.Rulesets.FirstOrDefault(r =>
                r != null && r != _pack && r.Model == AutoTileModel.Corner16 && r.CornerSlots.Count == 16);
            if (other == null) Assert.Ignore("Only one populated Corner16 pack ships.");

            var map = NewTilemap();
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                map.SetTile(new Vector3Int(x, y, 0), TerrainTileResolver.ResolveTile(
                    other.CornerSlots.First(s => (byte)s.slot == PureSecondary).variants.First(v => v != null)));

            var terrain = new TerrainMap();
            var result = TerrainPainter.ConnectRegion(map, new BoundsInt(1, 1, 0, 2, 2, 1),
                _pack, _tiles, terrain, _pack.TerrainSecondary);

            Assert.IsEmpty(result.MetadataEdits,
                "no vertex may be decided from tiles that belong to a different pack");
            Assert.IsEmpty(result.TileEdits);
        }

        private static int CountBits(byte b)
        {
            int n = 0;
            for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) n++;
            return n;
        }
    }
}
