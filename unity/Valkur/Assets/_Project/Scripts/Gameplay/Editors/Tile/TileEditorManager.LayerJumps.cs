using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Layer-Jumps panel logic for the runtime Tile Editor (M1.8):
    ///   • Show / Draw / Erase toggle handlers (mirror of the Colliders panel).
    ///   • TARGET LAYER picker callback.
    ///   • Mouse routing into the <see cref="LayerJumpMap"/> when Draw or Erase
    ///     mode is active.
    ///   • Brush-size-aware footprint, identical to how the Colliders painter
    ///     covers a square region per stroke step.
    ///
    /// State is owned by <see cref="TileEditorState"/>
    /// (<c>ShowLayerJumpsOverlay</c>, <c>CurrentLayerJumpMode</c>,
    /// <c>ActiveJumpTargetLayer</c>); this file flips state and asks the UI to
    /// repaint. Mutually exclusive with the Colliders panel — turning on Draw
    /// Jumps cancels Draw / Erase Colliders, and vice versa.
    /// </summary>
    public partial class TileEditorManager
    {
        // ── LayerJumpMap host (lazy, mirror of CollisionTagMap host) ─────────

        private LayerJumpMap _layerJumpMap;

        /// <summary>
        /// In-memory map of cell → target layer for tile-painted Layer Jumps.
        /// Lazy: a fresh manager creates an empty map on first access so EditMode
        /// test fixtures that bypass Start() still get a valid reference.
        /// </summary>
        public LayerJumpMap LayerJumps => _layerJumpMap ??= new LayerJumpMap();

        // ── Show toggle ──────────────────────────────────────────────────────

        internal void OnShowLayerJumpsClicked()
        {
            _state.ShowLayerJumpsOverlay = !_state.ShowLayerJumpsOverlay;
            ApplyLayerJumpsOverlayVisibility();
            _ui?.RefreshLayerJumpsToggles();
            _ui?.RefreshViewToggles();
            _ui?.SetStatus(_state.ShowLayerJumpsOverlay ? "Layer Jumps visible" : "Layer Jumps hidden");
        }

        // ── Draw / Erase toggles (mutex with Colliders Draw/Erase) ──────────

        internal void OnDrawLayerJumpsClicked()
        {
            bool wasDraw = _state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Draw;
            _undo?.EndStroke();
            _state.IsDragging = false;
            _state.BrushStrokeCells.Clear();

            // Mutex with the Colliders panel: turning Draw Jumps ON forces Colliders
            // out of any active edit mode (and vice versa). Same code path the
            // Colliders Draw/Erase pair already uses against each other.
            if (!wasDraw && _state.CurrentColliderMode != TileEditorState.ColliderMode.None)
            {
                _state.CurrentColliderMode = TileEditorState.ColliderMode.None;
                // Colliders may have been mid-drag when the mutex forced it off —
                // flush any pending composite rebake so it isn't lost.
                FlushPendingColliderRebake();
            }

            _state.CurrentLayerJumpMode = wasDraw
                ? TileEditorState.LayerJumpMode.None
                : TileEditorState.LayerJumpMode.Draw;

            if (_state.CurrentLayerJumpMode != TileEditorState.LayerJumpMode.None)
                _state.ShowLayerJumpsOverlay = true;

            if (_state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Draw)
                _state.CurrentTool = TileEditorState.Tool.Brush;

            ApplyLayerJumpsOverlayVisibility();
            _ui?.RefreshLayerJumpsToggles();
            _ui?.RefreshColliderToggles(); // Colliders may have been turned OFF by the mutex
            _ui?.RefreshToolHighlights();
            _ui?.SetStatus(_state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Draw
                ? $"Layer-jump draw mode (target = {_state.ActiveJumpTargetLayer})"
                : "Layer-jump edit mode disabled");
        }

        internal void OnEraseLayerJumpsClicked()
        {
            bool wasErase = _state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Erase;
            _undo?.EndStroke();
            _state.IsDragging = false;
            _state.BrushStrokeCells.Clear();

            if (!wasErase && _state.CurrentColliderMode != TileEditorState.ColliderMode.None)
            {
                _state.CurrentColliderMode = TileEditorState.ColliderMode.None;
                FlushPendingColliderRebake();
            }

            _state.CurrentLayerJumpMode = wasErase
                ? TileEditorState.LayerJumpMode.None
                : TileEditorState.LayerJumpMode.Erase;

            if (_state.CurrentLayerJumpMode != TileEditorState.LayerJumpMode.None)
                _state.ShowLayerJumpsOverlay = true;

            if (_state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Erase)
                _state.CurrentTool = TileEditorState.Tool.Eraser;

            ApplyLayerJumpsOverlayVisibility();
            _ui?.RefreshLayerJumpsToggles();
            _ui?.RefreshColliderToggles();
            _ui?.RefreshToolHighlights();
            _ui?.SetStatus(_state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Erase
                ? "Layer-jump erase mode (LMB to remove)"
                : "Layer-jump edit mode disabled");
        }

        // ── TARGET LAYER picker ─────────────────────────────────────────────

        internal void OnLayerJumpsTargetChanged(string targetLayer)
        {
            if (!LayerJumpMap.IsValidTarget(targetLayer)) targetLayer = "0";
            _state.ActiveJumpTargetLayer = targetLayer;
            _ui?.RefreshLayerJumpsPicker();
            _ui?.SetStatus($"Layer-jump target → {targetLayer}");
        }

        // ── Overlay binding ─────────────────────────────────────────────────

        private void ApplyLayerJumpsOverlayVisibility()
        {
            if (_gridOverlay == null) return;
            _gridOverlay.SetLayerJumpMap(LayerJumps);
            _gridOverlay.SetShowLayerJumps(_state.ShowLayerJumpsOverlay);
        }

        // ── Mouse routing ───────────────────────────────────────────────────

        /// <summary>
        /// True when the Layer-Jumps panel owns the mouse. Checked from the central
        /// <see cref="HandleMouseInput"/> dispatch so the regular tool handlers are
        /// skipped while jump painting is active. Coexists with
        /// <see cref="IsColliderEditModeActive"/> but the two are mutex'd in the
        /// toggle handlers above.
        /// </summary>
        internal bool IsLayerJumpsEditModeActive()
        {
            return _state != null && _state.CurrentLayerJumpMode != TileEditorState.LayerJumpMode.None;
        }

        /// <summary>
        /// Apply the current layer-jumps edit mode (Draw or Erase) to the brush-sized
        /// footprint at the cell under the mouse. Mirrors <c>HandleColliderInput</c>
        /// but writes directly to the in-memory <see cref="LayerJumpMap"/> rather than
        /// a tilemap. Each stroke opens/closes a <see cref="TileEditBatch"/> via
        /// <see cref="TileEditorUndoSystem"/> with <c>tilemap = null</c> — the batch
        /// carries only <see cref="MetadataEdit"/>s (no tilemap involved at all), which
        /// is exactly what the batch's optional-tilemap contract was built to allow.
        /// </summary>
        internal void HandleLayerJumpsInput()
        {
            var anchorTilemap = GetCurrentTilemap();
            if (anchorTilemap == null) return; // no tilemap → no cell math

            Vector3Int cursorCell = GetCellUnderMouse(anchorTilemap);

            bool drawing = _state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Draw;
            string target = drawing ? _state.ActiveJumpTargetLayer : null;

            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _state.BrushStrokeCells.Clear();
                _state.SelectedCellPos = cursorCell;
                _state.IsDragging = true;
                _undo.StartStroke(null);
                _undo.RecordMetadataEdits(StampLayerJumpsFootprint(cursorCell, target, drawing));
                AddCellsToBrushStroke(cursorCell);
            }
            else if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed() && _state.IsDragging)
            {
                _state.SelectedCellPos = cursorCell;
                _undo.RecordMetadataEdits(StampLayerJumpsFootprint(cursorCell, target, drawing));
                AddCellsToBrushStroke(cursorCell);
            }
            else if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                _state.BrushStrokeCells.Clear();
                // Autosave is DEBOUNCED (TileOverlayPersistence.Autosave): the edits above already
                // armed it through MarkBatchDirty, and it flushes off-thread after a quiet
                // period. Forcing a synchronous SaveAllDirty() here measured 6.48 ms on the
                // main thread for a single painted cell. Explicit saves (F8 close, slot
                // switch, Save button) still flush synchronously and wait for this one.
            }
        }

        /// <summary>
        /// Paint or clear the <see cref="TileEditorState.BrushSize"/> × BrushSize
        /// footprint anchored at <paramref name="cursorCell"/> (top-left corner,
        /// matching every other tool). Marks each touched cell dirty so the zone
        /// flushes on mouse-up, and returns the before/after of every cell that
        /// actually changed so the caller can record it into the open undo batch.
        /// </summary>
        private List<MetadataEdit> StampLayerJumpsFootprint(Vector3Int cursorCell, string target, bool drawing)
        {
            var dirty = new List<Vector3Int>(_state.BrushSize * _state.BrushSize);
            var metaEdits = new List<MetadataEdit>(_state.BrushSize * _state.BrushSize);
            for (int dy = 0; dy < _state.BrushSize; dy++)
            for (int dx = 0; dx < _state.BrushSize; dx++)
            {
                var cell = new Vector3Int(cursorCell.x + dx, cursorCell.y - dy, cursorCell.z);
                if (!CanEditCell(cell)) continue;

                string oldTarget = LayerJumps.Get(cell);
                if (string.IsNullOrEmpty(oldTarget)) oldTarget = null;
                string newTarget = drawing ? target : null;
                if (oldTarget != newTarget)
                    metaEdits.Add(new MetadataEdit(cell, oldTarget, newTarget, LayerJumps));

                if (drawing) LayerJumps.Set(cell, target);
                else         LayerJumps.Clear(cell);
                dirty.Add(cell);
            }
            // Persistence MarkBatchDirty needs TileEdit list, but jumps aren't tilemap
            // ops — fall back to per-cell MarkCellDirty which only needs Position.
            if (_persistence != null)
                for (int i = 0; i < dirty.Count; i++) _persistence.MarkCellDirty(dirty[i]);
            return metaEdits;
        }
    }
}
