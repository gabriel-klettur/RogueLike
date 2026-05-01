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
        /// Undo the last edit batch. Returns the batch that was undone (so callers can re-mark its cells dirty), or null if nothing to undo.
        /// </summary>
        public TileEditBatch Undo()
        {
            if (_undoStack.Count == 0) return null;
            var batch = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            batch.Undo();
            _redoStack.Add(batch);
            return batch;
        }

        /// <summary>
        /// Redo the last undone edit batch. Returns the batch that was redone, or null if nothing to redo.
        /// </summary>
        public TileEditBatch Redo()
        {
            if (_redoStack.Count == 0) return null;
            var batch = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            batch.Redo();
            _undoStack.Add(batch);
            return batch;
        }

        public bool HasActiveStroke => _currentBatch != null;
    }
}
