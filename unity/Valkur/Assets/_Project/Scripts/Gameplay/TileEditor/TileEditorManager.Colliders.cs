using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Colliders panel logic for the runtime Tile Editor:
    ///   • toggle handlers (Show / Draw / Erase)
    ///   • mouse routing into the Collision tilemap when Draw or Erase is active
    ///   • shared invisible "wall" tile factory (mirrors WorldLoader.GetWallCollisionTile)
    ///   • CompositeCollider2D regeneration after edits so Physics2D queries stay in sync
    ///
    /// State is owned by <see cref="TileEditorState"/> (ShowColliderOverlay,
    /// CurrentColliderMode). The UI just emits click signals; this file flips state and
    /// asks the UI to repaint.
    /// </summary>
    public partial class TileEditorManager
    {
        // Cached invisible Tile used to fill collision cells. Created lazily on first
        // draw and disposed in OnDestroy(). Sprite is intentionally invisible (alpha 0)
        // because the red overlay is drawn by TileEditorGridOverlay, not by this tile.
        private Tile _colliderTile;

        // ── Toggle handlers (called by UI buttons) ────────────────────────

        private void OnShowCollidersClicked()
        {
            _state.ShowColliderOverlay = !_state.ShowColliderOverlay;
            ApplyColliderOverlayVisibility();
            _ui?.RefreshColliderToggles();
            _ui?.SetStatus(_state.ShowColliderOverlay ? "Colliders visible" : "Colliders hidden");
        }

        private void OnDrawCollidersClicked()
        {
            bool wasDraw = _state.CurrentColliderMode == TileEditorState.ColliderMode.Draw;
            // End any in-flight stroke so Draw-mode entry/exit doesn't leak edits.
            _undo?.EndStroke();
            _state.IsDragging = false;
            _state.BrushStrokeCells.Clear();

            _state.CurrentColliderMode = wasDraw
                ? TileEditorState.ColliderMode.None
                : TileEditorState.ColliderMode.Draw;

            // Auto-enable overlay when entering an editing mode so the user sees their work.
            if (_state.CurrentColliderMode != TileEditorState.ColliderMode.None)
                _state.ShowColliderOverlay = true;

            // Mirror cursor footprint by setting the matching tool (so the brush preview
            // square is shown). Layer is NOT switched — drawing routes to Collision
            // explicitly via HandleColliderInput().
            if (_state.CurrentColliderMode == TileEditorState.ColliderMode.Draw)
                _state.CurrentTool = TileEditorState.Tool.Brush;

            ApplyColliderOverlayVisibility();
            _ui?.RefreshColliderToggles();
            _ui?.RefreshToolHighlights();
            _ui?.SetStatus(_state.CurrentColliderMode == TileEditorState.ColliderMode.Draw
                ? "Collider draw mode (LMB to paint)"
                : "Collider edit mode disabled");
        }

        private void OnEraseCollidersClicked()
        {
            bool wasErase = _state.CurrentColliderMode == TileEditorState.ColliderMode.Erase;
            _undo?.EndStroke();
            _state.IsDragging = false;
            _state.BrushStrokeCells.Clear();

            _state.CurrentColliderMode = wasErase
                ? TileEditorState.ColliderMode.None
                : TileEditorState.ColliderMode.Erase;

            if (_state.CurrentColliderMode != TileEditorState.ColliderMode.None)
                _state.ShowColliderOverlay = true;

            if (_state.CurrentColliderMode == TileEditorState.ColliderMode.Erase)
                _state.CurrentTool = TileEditorState.Tool.Eraser;

            ApplyColliderOverlayVisibility();
            _ui?.RefreshColliderToggles();
            _ui?.RefreshToolHighlights();
            _ui?.SetStatus(_state.CurrentColliderMode == TileEditorState.ColliderMode.Erase
                ? "Collider erase mode (LMB to remove)"
                : "Collider edit mode disabled");
        }

        // ── Overlay binding ──────────────────────────────────────────────

        /// <summary>
        /// Push the current Show/Hide state into the GL grid overlay and ensure the
        /// Collision tilemap reference is bound so the overlay can sample painted cells.
        /// </summary>
        private void ApplyColliderOverlayVisibility()
        {
            if (_gridOverlay == null) return;
            _gridOverlay.SetCollisionTilemap(GetCollisionTilemap());
            _gridOverlay.SetShowColliderOverlay(_state.ShowColliderOverlay);
        }

        // ── Mouse routing for collider edit modes ────────────────────────

        /// <summary>
        /// True when the current collider mode owns the mouse — the regular
        /// per-tool handlers in TileEditorManager.InputHandlers should be skipped.
        /// </summary>
        private bool IsColliderEditModeActive()
        {
            return _state != null && _state.CurrentColliderMode != TileEditorState.ColliderMode.None;
        }

        /// <summary>
        /// Apply the current collider mode (Draw or Erase) to the cell under the mouse.
        /// Mirrors HandleBrushInput / HandleEraserInput but always targets the Collision
        /// tilemap and uses the cached invisible collider tile.
        /// </summary>
        private void HandleColliderInput()
        {
            var collision = GetCollisionTilemap();
            if (collision == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector3Int cellPos = GetCellUnderMouse(collision);

            bool drawing = _state.CurrentColliderMode == TileEditorState.ColliderMode.Draw;
            TileBase tileToPaint = drawing ? GetOrCreateColliderTile() : null;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _state.BrushStrokeCells.Clear();
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(collision);
                var edits = TileBrush.Paint(collision, cellPos, tileToPaint, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
                _state.IsDragging = true;
                if (edits.Count > 0)
                    RegenerateCompositeCollider(collision);
                else if (!CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F11 Map Editor.");
            }
            else if (mouse.leftButton.isPressed && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                var edits = TileBrush.Paint(collision, cellPos, tileToPaint, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
                if (edits.Count > 0)
                    RegenerateCompositeCollider(collision);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                _state.BrushStrokeCells.Clear();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

    }
}