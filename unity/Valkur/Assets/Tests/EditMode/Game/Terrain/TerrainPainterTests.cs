using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Terrain
{
    /// <summary>
    /// Integration tests for <see cref="TerrainPainter"/>. Spins up an in-memory
    /// Tilemap, a synthetic <see cref="TilesetRuleset"/> with a sprite per Blob16
    /// slot, and verifies that:
    ///   - PaintRegion stamps the terrain into the map for every cell in the rect.
    ///   - PaintRegion emits exactly one TileEdit per cell that actually changed.
    ///   - Cells in the interior of the rect resolve to <see cref="Blob16Slot.Center"/>.
    ///   - Cells on the rect border resolve to the matching edge / corner slot.
    ///   - canEditCell predicate skips disallowed cells.
    /// </summary>
    [TestFixture]
    public class TerrainPainterTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Object> _scriptableObjects = new List<Object>();
        private readonly List<Sprite> _sprites = new List<Sprite>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();

            foreach (var s in _sprites)
            {
                if (s == null) continue;
                if (s.texture != null) Object.DestroyImmediate(s.texture);
                Object.DestroyImmediate(s);
            }
            _sprites.Clear();

            foreach (var so in _scriptableObjects)
                if (so != null) Object.DestroyImmediate(so);
            _scriptableObjects.Clear();

            // Clear caches that out-live the test boundary. TileRegistry caches
            // Tile instances by sprite name; without this, a Tile from the
            // previous test (whose sprite we just destroyed above) would
            // leak into the next test and trip MissingReferenceException
            // when its `.sprite.name` is read. The production resolver is
            // also defensive (TerrainTileResolver.IsCachedTileStillValid),
            // but clearing here keeps the test suite's failure mode obvious
            // if the resolver's defence ever regresses.
            TileRegistry.Instance.Clear();
            TerrainCatalogLoader.InvalidateCache();
        }

        private Sprite NewSprite(string name)
        {
            var tex = new Texture2D(1, 1);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = name;
            _sprites.Add(sprite);
            return sprite;
        }

        private TilesetRuleset NewRulesetWithAllSlots(string folder, string terrain, int priority)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, terrain, null, priority, AutoTileModel.Blob16);
            for (int i = 0; i < 16; i++)
                rs.EditorSetSlot((Blob16Slot)i, new[] { NewSprite($"{folder}_slot{i}") });
            return rs;
        }

        private TerrainCatalog NewCatalog(params TilesetRuleset[] rulesets)
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            _scriptableObjects.Add(catalog);
            foreach (var rs in rulesets) catalog.EditorAdd(rs);
            return catalog;
        }

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

        // ---------------- PaintRegion ----------------

        [Test]
        public void PaintRegion_NullTilemap_ReturnsEmpty()
        {
            var rs = NewRulesetWithAllSlots("grass", "grass", 0);
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var rect = new BoundsInt(0, 0, 0, 2, 2, 1);
            var (edits, metadataEdits) = TerrainPainter.PaintRegion(null, rect, "grass", catalog, map);
            Assert.IsEmpty(edits);
            Assert.IsEmpty(metadataEdits);
        }

        [Test]
        public void PaintRegion_StampsTerrainAcrossEntireRect()
        {
            var rs = NewRulesetWithAllSlots("grass", "grass", 0);
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var tilemap = NewTilemap();

            var rect = new BoundsInt(0, 0, 0, 3, 3, 1);
            TerrainPainter.PaintRegion(tilemap, rect, "grass", catalog, map);

            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                Assert.AreEqual("grass", map.GetTerrain(new Vector2Int(x, y)),
                    $"cell ({x},{y}) should be stamped with 'grass'.");
        }

        [Test]
        public void PaintRegion_EmitsTileEditsForChangedCells()
        {
            var rs = NewRulesetWithAllSlots("grass", "grass", 0);
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var tilemap = NewTilemap();

            var rect = new BoundsInt(0, 0, 0, 3, 3, 1);
            var (edits, metadataEdits) = TerrainPainter.PaintRegion(tilemap, rect, "grass", catalog, map);

            Assert.AreEqual(9, edits.Count, "3×3 rect should produce 9 tile edits when starting empty.");
            Assert.AreEqual(9, metadataEdits.Count, "3×3 rect should produce 9 terrain metadata edits when starting empty.");
            foreach (var e in edits)
                Assert.IsNull(e.OldTile, "starting tilemap is empty");
        }

        [Test]
        public void PaintRegion_InteriorCellResolvesToCenter()
        {
            var rs = NewRulesetWithAllSlots("grass", "grass", 0);
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var tilemap = NewTilemap();

            var rect = new BoundsInt(0, 0, 0, 3, 3, 1);
            TerrainPainter.PaintRegion(tilemap, rect, "grass", catalog, map);

            var interior = tilemap.GetTile(new Vector3Int(1, 1, 0)) as UnityEngine.Tilemaps.Tile;
            Assert.IsNotNull(interior);
            Assert.AreEqual("grass_slot15", interior.sprite.name,
                "Interior cell of a 3×3 rect has all 4 cardinal neighbours as same terrain → Center slot.");
        }

        [Test]
        public void PaintRegion_BottomLeftCornerResolvesToConnectNE()
        {
            var rs = NewRulesetWithAllSlots("grass", "grass", 0);
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var tilemap = NewTilemap();

            var rect = new BoundsInt(0, 0, 0, 3, 3, 1);
            TerrainPainter.PaintRegion(tilemap, rect, "grass", catalog, map);

            // Cell (0,0) of the rect: north neighbour (0,1) is grass, east neighbour
            // (1,0) is grass, south (0,-1) and west (-1,0) are empty → mask = N|E = 3 = ConnectNE.
            var corner = tilemap.GetTile(new Vector3Int(0, 0, 0)) as UnityEngine.Tilemaps.Tile;
            Assert.IsNotNull(corner);
            Assert.AreEqual("grass_slot3", corner.sprite.name);
        }

        [Test]
        public void PaintRegion_SingleCellRectResolvesToIsolated()
        {
            var rs = NewRulesetWithAllSlots("grass", "grass", 0);
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var tilemap = NewTilemap();

            var rect = new BoundsInt(0, 0, 0, 1, 1, 1);
            TerrainPainter.PaintRegion(tilemap, rect, "grass", catalog, map);

            var cell = tilemap.GetTile(new Vector3Int(0, 0, 0)) as UnityEngine.Tilemaps.Tile;
            Assert.IsNotNull(cell);
            Assert.AreEqual("grass_slot0", cell.sprite.name,
                "Single-cell rect with no neighbours → Isolated.");
        }

        [Test]
        public void PaintRegion_CanEditCellPredicate_SkipsCells()
        {
            var rs = NewRulesetWithAllSlots("grass", "grass", 0);
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var tilemap = NewTilemap();

            // Reject the centre cell of a 3×3 rect.
            var blocked = new Vector3Int(1, 1, 0);
            var rect = new BoundsInt(0, 0, 0, 3, 3, 1);
            TerrainPainter.PaintRegion(tilemap, rect, "grass", catalog, map,
                cell => cell != blocked);

            Assert.IsNull(map.GetTerrain(new Vector2Int(1, 1)), "blocked cell shouldn't be stamped.");
            Assert.IsNull(tilemap.GetTile(blocked), "blocked cell shouldn't get a sprite.");
            Assert.AreEqual("grass", map.GetTerrain(new Vector2Int(0, 0)), "unblocked cells still painted.");
        }

        [Test]
        public void PaintRegion_TerrainWithoutRuleset_DoesNothingButStampsTerrain()
        {
            var catalog = NewCatalog(); // empty
            var map = new TerrainMap();
            var tilemap = NewTilemap();

            var rect = new BoundsInt(0, 0, 0, 2, 2, 1);
            var (edits, metadataEdits) = TerrainPainter.PaintRegion(tilemap, rect, "grass", catalog, map);

            Assert.IsEmpty(edits, "no ruleset → no visual edits.");
            Assert.AreEqual("grass", map.GetTerrain(new Vector2Int(0, 0)),
                "terrain stamping happens before ruleset lookup, so the map still records intent.");
            Assert.AreEqual(4, metadataEdits.Count,
                "2×2 rect still records terrain MetadataEdits for undo even though no TileEdit was produced.");
        }

        // ---------------- Resolve (single cell) ----------------

        [Test]
        public void Resolve_OnUntrackedCell_ReturnsNull()
        {
            var rs = NewRulesetWithAllSlots("grass", "grass", 0);
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var tilemap = NewTilemap();
            var edit = TerrainPainter.Resolve(tilemap, new Vector3Int(0, 0, 0), catalog, map);
            Assert.IsNull(edit);
        }

        [Test]
        public void Resolve_WithKnownTerrain_AppliesVariant()
        {
            var rs = NewRulesetWithAllSlots("grass", "grass", 0);
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var tilemap = NewTilemap();
            map.SetTerrain(new Vector2Int(0, 0), "grass");

            var edit = TerrainPainter.Resolve(tilemap, new Vector3Int(0, 0, 0), catalog, map);
            Assert.IsTrue(edit.HasValue);
            var t = tilemap.GetTile(new Vector3Int(0, 0, 0)) as UnityEngine.Tilemaps.Tile;
            Assert.IsNotNull(t);
            Assert.AreEqual("grass_slot0", t.sprite.name);
        }
    }
}
