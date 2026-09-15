// Opening the Tile Editor must not change the map.
//
// TileEditorManager.LoadAllTerrainsFromDisk streams the saved terrain matrices and then re-runs
// the resolver over every cell of every zone that has one — the "auto-curation" pass, whose job
// is to fix cells whose art no longer matches the terrain they carry. It is the only code that
// rewrites tiles nobody asked it to touch, so everything it does has to be justified by a real
// disagreement.
//
// It was not. It asked FindPaintRuleset(terrain), which resolves a terrain NAME to exactly one
// ruleset, and two packs claim 'grass'. Measured on the shipped overrides for Forest: of the
// 1,879 cells carrying both terrain and auto-tile art, 1,072 — 57% — were handed the WRONG pack
// and repainted with its art, on every single open. The report was "I load the game and the
// zone is right, then I open the Tile Editor and many tiles change".
//
// Two rules fix it and both are load-bearing:
//   the pack comes from the TILE that is there, which is unambiguous where the name is not; and
//   a cell whose slot is already correct is left alone, so a correct map is a no-op rather than
//   a reshuffle of which sheet's variant each cell happens to draw.

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
    public class AutoTileCurationTests
    {
        private const byte PurePrimary = 0;
        private const byte PureSecondary = 15;

        private readonly List<Object> _created = new List<Object>();
        private TerrainCatalog _catalog;
        private TileCatalog _tiles;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _catalog = Resources.Load<TerrainCatalog>("TerrainCatalog");
            Assert.IsNotNull(_catalog);
            _tiles = TileCatalog.BuildFromResources();
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

        private IEnumerable<TilesetRuleset> Packs => _catalog.Rulesets.Where(r =>
            r != null && r.Model == AutoTileModel.Corner16 && r.CornerSlots.Count == 16 &&
            !string.IsNullOrEmpty(r.TerrainSecondary));

        private static Sprite SpriteIn(TilesetRuleset pack, byte mask) => pack.CornerSlots
            .Where(s => (byte)s.slot == mask && s.variants != null)
            .SelectMany(s => s.variants).FirstOrDefault(v => v != null);

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

        /// <summary>Paints a solid field of <paramref name="pack"/> and stamps the matching terrain,
        /// i.e. a correctly authored area — exactly what curation must leave alone.</summary>
        private (Tilemap Map, TerrainMap Terrain) SolidField(TilesetRuleset pack, byte mask, int size)
        {
            var map = NewTilemap();
            var terrain = new TerrainMap();
            string t = mask == PureSecondary ? pack.TerrainSecondary : pack.TerrainPrimary;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                map.SetTile(new Vector3Int(x, y, 0), TerrainTileResolver.ResolveTile(SpriteIn(pack, mask)));
            for (int y = 0; y <= size; y++)
            for (int x = 0; x <= size; x++)
                terrain.SetTerrain(new Vector2Int(x, y), t);

            return (map, terrain);
        }

        // ── The no-op ────────────────────────────────────────────────────────────

        [Test]
        public void ACorrectlyPaintedArea_SurvivesCurationUntouched()
        {
            foreach (var pack in Packs)
            foreach (byte mask in new[] { PurePrimary, PureSecondary })
            {
                if (SpriteIn(pack, mask) == null) continue;
                var (map, terrain) = SolidField(pack, mask, 6);

                int changed = 0;
                for (int y = 0; y < 6; y++)
                for (int x = 0; x < 6; x++)
                    if (TerrainPainter.Resolve(map, new Vector3Int(x, y, 0), _catalog, terrain, _tiles).HasValue)
                        changed++;

                Assert.AreEqual(0, changed,
                    $"[{pack.FolderName}] opening the editor repainted {changed} cells of an area " +
                    "that was already correct. Curation must be a no-op on a correct map.");
            }
        }

        [Test]
        public void ItRunsTwiceWithoutDrifting()
        {
            // A pass that is not idempotent means the map changes a little on every open.
            var pack = Packs.First();
            var (map, terrain) = SolidField(pack, PureSecondary, 6);

            // Break one cell so the first pass has real work to do.
            map.SetTile(new Vector3Int(3, 3, 0), TerrainTileResolver.ResolveTile(SpriteIn(pack, PurePrimary)));

            int First()
            {
                int n = 0;
                for (int y = 0; y < 6; y++)
                for (int x = 0; x < 6; x++)
                    if (TerrainPainter.Resolve(map, new Vector3Int(x, y, 0), _catalog, terrain, _tiles).HasValue) n++;
                return n;
            }

            Assert.AreEqual(1, First(), "exactly the broken cell is cured");
            Assert.AreEqual(0, First(), "and a second pass finds nothing left to do");
        }

        // ── The pack comes from the tile ─────────────────────────────────────────

        [Test]
        public void APackShadowedOnItsTerrainName_IsNotRepaintedByTheOtherOne()
        {
            // The defect, in one case: two packs share a primary terrain, FindPaintRuleset hands
            // the name to exactly one of them, and curation repainted the other pack's art with
            // it. Measured on the shipped Forest override, that was 1,072 cells of 1,879.
            var shadowed = Packs.FirstOrDefault(p => _catalog.FindPaintRuleset(p.TerrainPrimary) != p);
            if (shadowed == null) Assert.Ignore("No shipped pack is currently shadowed on its primary terrain.");

            var (map, terrain) = SolidField(shadowed, PurePrimary, 5);

            for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                Assert.IsFalse(TerrainPainter.Resolve(map, new Vector3Int(x, y, 0), _catalog, terrain, _tiles).HasValue,
                    $"[{shadowed.FolderName}] is shadowed by " +
                    $"'{_catalog.FindPaintRuleset(shadowed.TerrainPrimary)?.FolderName}' on the terrain " +
                    "name; curing from the NAME repaints it with the other pack's art.");

            for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
            {
                var sprite = (map.GetTile(new Vector3Int(x, y, 0)) as Tile)?.sprite;
                Assert.AreSame(shadowed, AutoBrushPackLookup.FindPackAndSlot(_catalog, sprite, _tiles).Ruleset,
                    "and every cell must still belong to the pack the author painted with");
            }
        }

        [Test]
        public void ArtThatBelongsToNoPack_IsNeverTouched()
        {
            // Hand-placed scenery sits on the same tilemap. Curation has nothing to say about it,
            // and a pass that resolves it anyway destroys work the author did by hand.
            var loose = _tiles.GetTilesForCategory("castle_pandora").FirstOrDefault(t => t.preview != null);
            Assert.IsNotNull(loose, "Fixture assumes castle_pandora ships sprites.");
            Assert.IsNull(AutoBrushPackLookup.FindPackAndSlot(_catalog, loose.preview, _tiles).Ruleset,
                "Fixture assumes that category is not an auto-tile pack.");

            var pack = Packs.First();
            var (map, terrain) = SolidField(pack, PureSecondary, 5);
            map.SetTile(new Vector3Int(2, 2, 0), TerrainTileResolver.ResolveTile(loose.preview));

            Assert.IsFalse(TerrainPainter.Resolve(map, new Vector3Int(2, 2, 0), _catalog, terrain, _tiles).HasValue);
            Assert.AreEqual(loose.preview, (map.GetTile(new Vector3Int(2, 2, 0)) as Tile)?.sprite,
                "the hand-placed tile must still be there afterwards");
        }

        [Test]
        public void ACellWithAnUnknownCorner_IsLeftAlone()
        {
            // An absent vertex reads as the primary terrain, so resolving a cell with one would
            // cut an edge through solid ground. At the frontier of the saved terrain matrix that
            // is every border cell of the zone.
            var pack = Packs.First();
            var map = NewTilemap();
            var terrain = new TerrainMap();

            map.SetTile(new Vector3Int(0, 0, 0), TerrainTileResolver.ResolveTile(SpriteIn(pack, PureSecondary)));
            terrain.SetTerrain(new Vector2Int(0, 0), pack.TerrainSecondary);
            terrain.SetTerrain(new Vector2Int(1, 0), pack.TerrainSecondary);
            terrain.SetTerrain(new Vector2Int(0, 1), pack.TerrainSecondary);
            // (1,1) deliberately absent

            Assert.IsFalse(TerrainPainter.Resolve(map, new Vector3Int(0, 0, 0), _catalog, terrain, _tiles).HasValue,
                "three known corners are not enough to resolve a cell");
        }

        // ── And it still cures what is genuinely wrong ───────────────────────────

        [Test]
        public void ACellWhoseArtDisagreesWithItsTerrain_IsCorrected()
        {
            var pack = Packs.First();
            var (map, terrain) = SolidField(pack, PureSecondary, 6);

            // A solid-primary tile sitting where the terrain says solid secondary.
            var cell = new Vector3Int(3, 3, 0);
            map.SetTile(cell, TerrainTileResolver.ResolveTile(SpriteIn(pack, PurePrimary)));

            var edit = TerrainPainter.Resolve(map, cell, _catalog, terrain, _tiles);
            Assert.IsTrue(edit.HasValue, "curation exists for exactly this case");

            var after = (map.GetTile(cell) as Tile)?.sprite;
            var found = AutoBrushPackLookup.FindPackAndSlot(_catalog, after, _tiles);
            Assert.AreSame(pack, found.Ruleset, "and it must stay in the same pack");
            Assert.AreEqual(PureSecondary, found.SlotMask, "resolved to what the terrain says");
        }
    }
}
