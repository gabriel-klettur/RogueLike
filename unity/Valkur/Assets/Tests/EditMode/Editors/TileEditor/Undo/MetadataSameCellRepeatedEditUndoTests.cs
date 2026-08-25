using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// Pins the ordering contract <see cref="TileEditBatch.Undo"/> /
    /// <see cref="TileEditBatch.Redo"/> depend on when the SAME cell is edited
    /// more than once inside a single stroke (one open batch, before
    /// <c>EndStroke</c>): Undo walks Edits/MetadataEdits in REVERSE, so the
    /// LAST-recorded edit for a cell reverts first and the FIRST-recorded
    /// edit's <c>OldValue</c> — the true pre-stroke value — is what survives;
    /// Redo walks FORWARD, so the LAST-recorded edit's <c>NewValue</c> — the
    /// true final, on-screen value — is what survives. Flip either direction
    /// and a double-touched cell lands on the INTERMEDIATE value instead of
    /// the pre-stroke / final one — wrong, but easy to miss visually (the cell
    /// still ends up looking painted either way).
    ///
    /// No sibling suite exercises two edits to the same cell within one open
    /// batch — every existing test (<c>TileEditorUndoSystemTests</c>,
    /// <c>ColliderTagUndoTests</c>, <c>LayerJumpsUndoTests</c>,
    /// <c>AutoTileRegionUndoTests</c>, <c>TileEditBatchCrossTilemapTests</c>)
    /// only ever records ONE edit per cell per stroke, or spreads repeated
    /// touches across SEPARATE, independently-committed strokes.
    ///
    /// Drives <see cref="TileEditorUndoSystem"/> + <see cref="TileEditBatch"/>
    /// directly against real production metadata-map classes — no
    /// TileEditorManager instance required.
    /// </summary>
    [TestFixture]
    public class MetadataSameCellRepeatedEditUndoTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Object> _tiles = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();

            foreach (var t in _tiles)
                if (t != null) Object.DestroyImmediate(t);
            _tiles.Clear();
        }

        private Tile MakeTile(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sprite;
            _tiles.Add(t);
            return t;
        }

        private Tilemap NewTilemap(string name)
        {
            var gridGo = new GameObject(name + "_Grid");
            _created.Add(gridGo);
            gridGo.AddComponent<Grid>().cellSize = Vector3.one;
            var tmGo = new GameObject(name + "_Tilemap");
            tmGo.transform.SetParent(gridGo.transform, false);
            return tmGo.AddComponent<Tilemap>();
        }

        [Test]
        public void CollisionTagMapAndTile_SameCellTouchedTwiceInOneStroke_UndoRestoresPreStrokeValue_RedoRestoresFinalValue()
        {
            var tilemap = NewTilemap("Collision");
            var tagMap = new CollisionTagMap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(2, 2, 0);
            var tileA = MakeTile(Color.red);
            var tileB = MakeTile(Color.green);
            var tileC = MakeTile(Color.blue);

            // Pre-stroke state.
            tilemap.SetTile(cell, tileA);
            tagMap.Set(cell, "1");

            undo.StartStroke(tilemap);

            // First touch within the stroke.
            tilemap.SetTile(cell, tileB);
            tagMap.Set(cell, "2");
            undo.RecordEdits(new List<TileEdit> { new TileEdit(cell, tileA, tileB) });
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, "1", "2", tagMap) });

            // Second touch, SAME cell, SAME open stroke (never ended in between).
            tilemap.SetTile(cell, tileC);
            tagMap.Set(cell, "3");
            undo.RecordEdits(new List<TileEdit> { new TileEdit(cell, tileB, tileC) });
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, "2", "3", tagMap) });

            undo.EndStroke();

            Assert.AreEqual(tileC, tilemap.GetTile(cell), "Sanity: on-screen tile is the LAST write.");
            Assert.AreEqual("3", tagMap.GetRaw(cell), "Sanity: on-screen tag is the LAST write.");

            undo.Undo();

            Assert.AreEqual(tileA, tilemap.GetTile(cell),
                "Undo must land on the PRE-STROKE tile (tileA), not the intermediate tileB — " +
                "requires walking Edits in REVERSE order.");
            Assert.AreEqual("1", tagMap.GetRaw(cell),
                "Undo must land on the PRE-STROKE tag ('1'), not the intermediate '2' — " +
                "requires walking MetadataEdits in REVERSE order.");

            undo.Redo();

            Assert.AreEqual(tileC, tilemap.GetTile(cell),
                "Redo must land on the FINAL tile (tileC), not the intermediate tileB — " +
                "requires walking Edits FORWARD.");
            Assert.AreEqual("3", tagMap.GetRaw(cell),
                "Redo must land on the FINAL tag ('3'), not the intermediate '2' — " +
                "requires walking MetadataEdits FORWARD.");
        }

        [Test]
        public void TerrainMap_SameCellTouchedTwiceInOneStroke_UndoRestoresPreStrokeValue_RedoRestoresFinalValue()
        {
            var terrainMap = new TerrainMap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(4, 0, 0);

            terrainMap.SetTerrain(cell, "dirt"); // pre-stroke

            undo.StartStroke(null);
            terrainMap.SetTerrain(cell, "grass"); // 1st touch within the stroke
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, "dirt", "grass", terrainMap) });
            terrainMap.SetTerrain(cell, "sand"); // 2nd touch, same cell, same open stroke
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, "grass", "sand", terrainMap) });
            undo.EndStroke();

            Assert.AreEqual("sand", terrainMap.GetTerrain(cell), "Sanity: on-screen value is the last write.");

            undo.Undo();
            Assert.AreEqual("dirt", terrainMap.GetTerrain(cell),
                "Undo must land on the PRE-STROKE terrain ('dirt'), not the intermediate 'grass'.");

            undo.Redo();
            Assert.AreEqual("sand", terrainMap.GetTerrain(cell),
                "Redo must land on the FINAL terrain ('sand'), not the intermediate 'grass'.");
        }

        [Test]
        public void LayerJumpMap_SameCellTouchedTwiceInOneStroke_UndoRestoresPreStrokeValue_RedoRestoresFinalValue()
        {
            var jumpMap = new LayerJumpMap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(6, 6, 0);

            jumpMap.Set(cell, "1"); // pre-stroke

            undo.StartStroke(null);
            jumpMap.Set(cell, "4"); // 1st touch within the stroke
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, "1", "4", jumpMap) });
            jumpMap.Set(cell, "7"); // 2nd touch, same cell, same open stroke
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, "4", "7", jumpMap) });
            undo.EndStroke();

            Assert.AreEqual("7", jumpMap.Get(cell), "Sanity: on-screen value is the last write.");

            undo.Undo();
            Assert.AreEqual("1", jumpMap.Get(cell),
                "Undo must land on the PRE-STROKE target ('1'), not the intermediate '4'.");

            undo.Redo();
            Assert.AreEqual("7", jumpMap.Get(cell),
                "Redo must land on the FINAL target ('7'), not the intermediate '4'.");
        }
    }
}
