using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// End-to-end tests for the undo/redo system used by the in-game tile editor.
    /// Covers stroke lifecycle, history cap (MAX_UNDO), redo invalidation on new edits,
    /// and round-trip restoration of the underlying Tilemap state.
    /// </summary>
    [TestFixture]
    public class TileEditorUndoSystemTests
    {
        private GameObject _root;
        private Tilemap _tilemap;
        private Tile _tileA;
        private Tile _tileB;
        private TileEditorUndoSystem _undo;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TilemapRoot");
            _root.AddComponent<Grid>().cellSize = Vector3.one;
            var go = new GameObject("Tilemap");
            go.transform.SetParent(_root.transform, false);
            _tilemap = go.AddComponent<Tilemap>();

            _tileA = MakeTile(Color.red);
            _tileB = MakeTile(Color.blue);
            _undo = new TileEditorUndoSystem();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_tileA);
            Object.DestroyImmediate(_tileB);
        }

        private static Tile MakeTile(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sprite;
            return t;
        }

        private List<TileEdit> PaintStroke(Vector3Int pos, TileBase tile)
        {
            _undo.StartStroke(_tilemap);
            var edits = TileBrush.Paint(_tilemap, pos, tile, brushSize: 1);
            _undo.RecordEdits(edits);
            _undo.EndStroke();
            return edits;
        }

        // ── Lifecycle ────────────────────────────────────────────────────

        [Test]
        public void StartStroke_SetsHasActiveStrokeTrue()
        {
            Assert.IsFalse(_undo.HasActiveStroke);
            _undo.StartStroke(_tilemap);
            Assert.IsTrue(_undo.HasActiveStroke);
            _undo.EndStroke();
            Assert.IsFalse(_undo.HasActiveStroke);
        }

        [Test]
        public void EndStroke_WithNoEdits_DoesNotPushToUndoStack()
        {
            _undo.StartStroke(_tilemap);
            _undo.EndStroke();

            Assert.IsNull(_undo.Undo(),
                "Empty strokes must not be pushed onto the undo stack.");
        }

        // ── Single edit round-trip ───────────────────────────────────────

        [Test]
        public void Undo_RestoresPreviousTile()
        {
            PaintStroke(Vector3Int.zero, _tileA);
            Assert.AreEqual(_tileA, _tilemap.GetTile(Vector3Int.zero));

            var batch = _undo.Undo();

            Assert.IsNotNull(batch);
            Assert.IsNull(_tilemap.GetTile(Vector3Int.zero),
                "Undo must restore the cell to its pre-edit (empty) state.");
        }

        [Test]
        public void Redo_AfterUndo_ReappliesEdit()
        {
            PaintStroke(Vector3Int.zero, _tileA);
            _undo.Undo();

            var redone = _undo.Redo();

            Assert.IsNotNull(redone);
            Assert.AreEqual(_tileA, _tilemap.GetTile(Vector3Int.zero));
        }

        [Test]
        public void Undo_WithEmptyStack_ReturnsNull()
        {
            Assert.IsNull(_undo.Undo());
        }

        [Test]
        public void Redo_WithEmptyStack_ReturnsNull()
        {
            Assert.IsNull(_undo.Redo());
        }

        // ── Redo invalidation ────────────────────────────────────────────

        [Test]
        public void NewStroke_AfterUndo_ClearsRedoStack()
        {
            PaintStroke(Vector3Int.zero, _tileA);
            _undo.Undo();

            // A fresh stroke must invalidate any pending redo.
            PaintStroke(new Vector3Int(5, 5, 0), _tileB);

            Assert.IsNull(_undo.Redo(),
                "Recording a new stroke after Undo must clear the redo stack.");
        }

        // ── History cap ──────────────────────────────────────────────────

        [Test]
        public void UndoStack_NeverExceedsMaxUndoLimit()
        {
            // Push MAX_UNDO + 5 distinct strokes
            for (int i = 0; i < TileEditorState.MAX_UNDO + 5; i++)
                PaintStroke(new Vector3Int(i, 0, 0), _tileA);

            int popped = 0;
            while (_undo.Undo() != null) popped++;

            Assert.AreEqual(TileEditorState.MAX_UNDO, popped,
                "Undo stack must be capped at TileEditorState.MAX_UNDO.");
        }

        // ── Multi-cell batch ─────────────────────────────────────────────

        [Test]
        public void Undo_MultiCellStroke_RestoresAllCells()
        {
            _undo.StartStroke(_tilemap);
            _undo.RecordEdits(TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, brushSize: 3));
            _undo.EndStroke();

            // 3×3 = 9 painted cells
            for (int dy = 0; dy < 3; dy++)
                for (int dx = 0; dx < 3; dx++)
                    Assert.AreEqual(_tileA, _tilemap.GetTile(new Vector3Int(dx, -dy, 0)));

            _undo.Undo();

            for (int dy = 0; dy < 3; dy++)
                for (int dx = 0; dx < 3; dx++)
                    Assert.IsNull(_tilemap.GetTile(new Vector3Int(dx, -dy, 0)),
                        "Undo must restore every cell of a multi-cell stroke.");
        }
    }
}
