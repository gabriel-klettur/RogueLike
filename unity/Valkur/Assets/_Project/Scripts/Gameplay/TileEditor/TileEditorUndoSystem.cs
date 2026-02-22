using System.Collections.Generic;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Manages undo/redo stacks for tile edit operations.
    /// Extracted from TileEditorManager to isolate history concerns.
    /// </summary>
    public class TileEditorUndoSystem
    {
        private readonly List<TileEditBatch> _undoStack = new List<TileEditBatch>();
        private readonly List<TileEditBatch> _redoStack = new List<TileEditBatch>();
        private TileEditBatch _currentBatch;

        public void StartStroke(Tilemap tilemap)
        {
            _currentBatch = new TileEditBatch { TargetTilemap = tilemap };
        }

        public void RecordEdits(List<TileEdit> edits)
        {
            _currentBatch?.Edits.AddRange(edits);
        }

        public void EndStroke()
        {
            if (_currentBatch == null) return;
            if (_currentBatch.Edits.Count > 0)
            {
                _undoStack.Add(_currentBatch);
                if (_undoStack.Count > TileEditorState.MAX_UNDO)
                    _undoStack.RemoveAt(0);
                _redoStack.Clear();
            }
            _currentBatch = null;
        }

        /// <summary>
        /// Undo the last edit batch. Returns true if an undo was performed.
        /// </summary>
        public bool Undo()
        {
            if (_undoStack.Count == 0) return false;
            var batch = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            batch.Undo();
            _redoStack.Add(batch);
            return true;
        }

        /// <summary>
        /// Redo the last undone edit batch. Returns true if a redo was performed.
        /// </summary>
        public bool Redo()
        {
            if (_redoStack.Count == 0) return false;
            var batch = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            batch.Redo();
            _undoStack.Add(batch);
            return true;
        }

        public bool HasActiveStroke => _currentBatch != null;
    }
}
