using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// Coverage for the freehand AUTO Brush's stroke lifecycle — the sibling of
    /// <see cref="AutoTileRegionUndoTests"/> (which covers the click-drag
    /// rectangle tool) for <c>TileEditorManager.HandleAutoBrushInput</c> /
    /// <c>PaintAutoBrushFootprint</c>. Reproduces those methods' bodies verbatim
    /// against real production statics (<see cref="TerrainPainter.PaintRegion"/>,
    /// <see cref="TileEditorUndoSystem"/>) rather than spinning up
    /// <c>TileEditorManager</c> itself — see <c>AutoTileRegionUndoTests</c>'s
    /// class doc for the project's standing rationale (it's a MonoBehaviour that
    /// needs a full Grid + WorldGridBuilder scene to construct in EditMode).
    ///
    /// Most tests here use a Blob16 BASE ruleset, not Corner16: as documented in
    /// <c>TerrainPainterCorner16Tests</c>, <see cref="TerrainCatalog.FindBaseRuleset"/>
    /// excludes every transition ruleset today, so a Corner16-only catalog entry
    /// never produces a sprite through this path yet. What's under test here —
    /// the freehand drag batching into ONE undo step, and the brush-size
    /// footprint anchor — is model-agnostic: it's the exact code path a Corner16
    /// pack will also flow through once its own selection gap is closed. The
    /// last test below explicitly covers the Corner16-only (metadata-only) case.
    /// </summary>
    [TestFixture]
    public class AutoBrushStrokeUndoTests
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

        private TilesetRuleset NewBlobRulesetWithAllSlots(string folder)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, "grass", null, 0, AutoTileModel.Blob16);
            for (int i = 0; i < 16; i++)
                rs.EditorSetSlot((Blob16Slot)i, new[] { NewSprite($"{folder}_blob{i}") });
            return rs;
        }

        private TilesetRuleset NewCornerRulesetWithAllSlots(string folder)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, "grass", "dirt", 0, AutoTileModel.Corner16);
            for (int i = 0; i < 16; i++)
                rs.EditorSetSlot((Corner16Slot)i, new[] { NewSprite($"{folder}_corner{i}") });
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

        /// <summary>Reproduces TileEditorManager.PaintAutoBrushFootprint's body
        /// verbatim (minus the persistence/dirty-marking calls, which need a live
        /// TileOverlayPersistence tied to a manager instance and are covered by
        /// the TileOverlayPersistence* suites, not here).</summary>
        private static void PaintAutoBrushFootprintLike(TileEditorUndoSystem undo, Tilemap tilemap,
            Vector3Int cursorCell, string terrain, TerrainCatalog catalog, TerrainMap terrainMap, int brushSize)
        {
            var rect = new BoundsInt(cursorCell.x, cursorCell.y - (brushSize - 1), 0, brushSize, brushSize, 1);
            var (edits, metadataEdits) = TerrainPainter.PaintRegion(tilemap, rect, terrain, catalog, terrainMap);
            undo.RecordEdits(edits);
            undo.RecordMetadataEdits(metadataEdits);
        }

        /// <summary>Reproduces TileEditorManager.HandleAutoBrushInput's
        /// press -&gt; N drag calls -&gt; release lifecycle: ONE StartStroke/EndStroke
        /// pair wrapping one PaintAutoBrushFootprintLike call per cell visited.</summary>
        private static void RunAutoBrushStroke(TileEditorUndoSystem undo, Tilemap tilemap, TerrainCatalog catalog,
            TerrainMap terrainMap, string terrain, int brushSize, params Vector3Int[] dragPath)
        {
            undo.StartStroke(tilemap);
            foreach (var cell in dragPath)
                PaintAutoBrushFootprintLike(undo, tilemap, cell, terrain, catalog, terrainMap, brushSize);
            undo.EndStroke();
        }

        [Test]
        public void SingleCellStroke_CtrlZ_RevertsSpriteAndTerrainTogether()
        {
            var rs = NewBlobRulesetWithAllSlots("brush_grass");
            var catalog = NewCatalog(rs);
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(3, 3, 0);

            RunAutoBrushStroke(undo, tilemap, catalog, terrainMap, "grass", 1, cell);

            Assert.IsNotNull(tilemap.GetTile(cell), "Sanity: the stroke must place a sprite.");
            Assert.AreEqual("grass", terrainMap.GetTerrain(cell));

            undo.Undo();

            Assert.IsNull(tilemap.GetTile(cell), "One Ctrl+Z must clear the sprite...");
            Assert.IsNull(terrainMap.GetTerrain(cell), "...AND the terrain, in the same undo step.");
        }

        [Test]
        public void MultiCellDragStroke_SingleUndo_RevertsEntireStroke()
        {
            var rs = NewBlobRulesetWithAllSlots("brush_grass2");
            var catalog = NewCatalog(rs);
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();

            var a = new Vector3Int(0, 0, 0);
            var b = new Vector3Int(1, 0, 0);
            var c = new Vector3Int(2, 0, 0);

            RunAutoBrushStroke(undo, tilemap, catalog, terrainMap, "grass", 1, a, b, c);

            Assert.AreEqual("grass", terrainMap.GetTerrain(a));
            Assert.AreEqual("grass", terrainMap.GetTerrain(b));
            Assert.AreEqual("grass", terrainMap.GetTerrain(c));

            var undone = undo.Undo();

            Assert.IsNotNull(undone, "The whole drag must be ONE undo batch.");
            Assert.IsNull(terrainMap.GetTerrain(a));
            Assert.IsNull(terrainMap.GetTerrain(b));
            Assert.IsNull(terrainMap.GetTerrain(c));

            Assert.IsNull(undo.Undo(), "A second Undo must find nothing left — the drag was a single step, not three.");
        }

        [Test]
        public void BrushSizeGreaterThanOne_FootprintMatchesTopLeftAnchorConvention()
        {
            var rs = NewBlobRulesetWithAllSlots("brush_grass3");
            var catalog = NewCatalog(rs);
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();
            var cursor = new Vector3Int(5, 5, 0);
            const int size = 2;

            RunAutoBrushStroke(undo, tilemap, catalog, terrainMap, "grass", size, cursor);

            // Same anchor formula as TileEditorManager.AddCellsToBrushStroke:
            // cursor = top-left, footprint extends right (+x) and down (-y).
            var expectedFootprint = new[]
            {
                new Vector2Int(5, 5), new Vector2Int(6, 5),
                new Vector2Int(5, 4), new Vector2Int(6, 4),
            };
            foreach (var cell in expectedFootprint)
                Assert.AreEqual("grass", terrainMap.GetTerrain(cell), $"cell {cell} must be inside the {size}x{size} footprint.");

            // Cells just outside the footprint must be untouched.
            Assert.IsNull(terrainMap.GetTerrain(new Vector2Int(7, 5)));
            Assert.IsNull(terrainMap.GetTerrain(new Vector2Int(5, 3)));
        }

        [Test]
        public void UndoRedoRoundTrip_ReappliesTerrainAndSprite()
        {
            var rs = NewBlobRulesetWithAllSlots("brush_grass4");
            var catalog = NewCatalog(rs);
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(9, 9, 0);

            RunAutoBrushStroke(undo, tilemap, catalog, terrainMap, "grass", 1, cell);
            undo.Undo();
            Assert.IsNull(terrainMap.GetTerrain(cell));

            undo.Redo();

            Assert.IsNotNull(tilemap.GetTile(cell));
            Assert.AreEqual("grass", terrainMap.GetTerrain(cell));
        }

        [Test]
        public void Corner16OnlyCatalog_StrokeIsMetadataOnly_StillUndoable()
        {
            // Documents the same selection gap as TerrainPainterCorner16Tests,
            // seen through the freehand brush path: a Corner16-only catalog
            // entry produces no sprite, but the terrain stamp must still be a
            // real, undoable step (mirrors AutoTileRegionUndoTests'
            // TerrainWithoutRuleset coverage for the rectangle tool).
            var corner = NewCornerRulesetWithAllSlots("brush_corner_gap");
            var catalog = NewCatalog(corner);
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(0, 0, 0);

            RunAutoBrushStroke(undo, tilemap, catalog, terrainMap, "dirt", 1, cell);

            Assert.IsNull(tilemap.GetTile(cell), "KNOWN GAP: no base ruleset is ever found for a Corner16-only terrain.");
            Assert.AreEqual("dirt", terrainMap.GetTerrain(cell));

            var undone = undo.Undo();
            Assert.IsNotNull(undone, "A metadata-only AUTO brush stroke must still be a real undo step.");
            Assert.IsNull(terrainMap.GetTerrain(cell));
        }
    }
}
