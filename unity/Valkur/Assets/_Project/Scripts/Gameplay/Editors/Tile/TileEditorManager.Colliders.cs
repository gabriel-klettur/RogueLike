using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

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
            _gridOverlay.SetCollisionTagMap(CollisionTags);
            _gridOverlay.SetShowColliderOverlay(_state.ShowColliderOverlay);
            ApplyCollidersLayerPanelVisibility();
        }

        // ── COLLIDERS LAYER diagnostic panel ─────────────────────────────
        // The panel sits bottom-right of the canvas, immediately to the left of
        // the Layers dropdown. It is *purely* a diagnostic readout — visible only
        // when the user is actively authoring per-layer collisions (editor active
        // AND Show Colliders ON), so it never costs screen space during normal
        // gameplay or while other Tile-Editor tools are in use.

        // Cached refs resolved lazily — avoids per-frame FindObjectOfType cost
        // while the panel is being ticked.
        private VisualLayerOccupant _playerLayerOccupant;
        private readonly bool[] _underfootScratch = new bool[9];

        /// <summary>
        /// Show / hide the "COLLIDERS LAYER" panel based on whether the user is
        /// in a state where the readout is useful. Hidden in every other state
        /// (game mode, editor open with Show Colliders OFF).
        /// </summary>
        private void ApplyCollidersLayerPanelVisibility()
        {
            if (_ui == null) return;
            var panel = _ui.GetCollidersLayerPanel();
            if (panel == null) return;
            bool show = _state != null && _state.Active && _state.ShowColliderOverlay;
            if (panel.activeSelf != show)
                panel.SetActive(show);
        }

        /// <summary>
        /// Update the panel's two readout labels with the player's logical
        /// visual layer (from <see cref="VisualLayerOccupant"/>) + the set of
        /// visual layers that currently have a tile under the player's feet
        /// (from <see cref="VisualLayerProbe"/>). Called once per frame by
        /// <see cref="Update"/> while the panel is visible.
        /// </summary>
        internal void TickCollidersLayerPanel()
        {
            if (_ui == null) return;
            var panel = _ui.GetCollidersLayerPanel();
            if (panel == null || !panel.activeSelf) return;

            // Re-resolve the player occupant lazily — it can get nulled out
            // between deaths/respawns, so a null check is cheaper than a hard
            // singleton subscription.
            if (_playerLayerOccupant == null)
                _playerLayerOccupant = FindObjectOfType<VisualLayerOccupant>();

            int layer = -1;
            string layerName = null;
            int populated = 0;
            if (_playerLayerOccupant != null)
            {
                layer = _playerLayerOccupant.CurrentVisualLayer;
                layerName = _playerLayerOccupant.LayerName;
                populated = VisualLayerProbe.Sample(_playerLayerOccupant.transform.position,
                                                     worldGridBuilder, _underfootScratch);
            }
            else
            {
                // Clear the scratch buffer so the underfoot line shows "(none)"
                // instead of stale data from the previous owner.
                for (int i = 0; i < _underfootScratch.Length; i++) _underfootScratch[i] = false;
            }

            _ui.RefreshCollidersLayerPanel(layer, layerName,
                populated > 0 ? _underfootScratch : null);
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

            Vector3Int cellPos = GetCellUnderMouse(collision);

            bool drawing = _state.CurrentColliderMode == TileEditorState.ColliderMode.Draw;
            TileBase tileToPaint = drawing ? GetOrCreateColliderTile() : null;

            // Use MouseInputManager so the legacy backend kicks in if the new
            // InputSystem package drops OS events (recurring Unity 2022.3 bug).
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _state.BrushStrokeCells.Clear();
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(collision);
                var edits = TileBrush.Paint(collision, cellPos, tileToPaint, _state.BrushSize, canEditCell: null);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                ApplyTagToEdits(edits, drawing);
                AddCellsToBrushStroke(cellPos);
                _state.IsDragging = true;
                if (edits.Count > 0)
                    RegenerateCompositeCollider(collision);
            }
            else if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed() && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                var edits = TileBrush.Paint(collision, cellPos, tileToPaint, _state.BrushSize, canEditCell: null);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                ApplyTagToEdits(edits, drawing);
                AddCellsToBrushStroke(cellPos);
                if (edits.Count > 0)
                    RegenerateCompositeCollider(collision);
            }
            else if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                _state.BrushStrokeCells.Clear();
                // Auto-persist: flush dirty zones immediately so collider edits
                // survive a scene reload without requiring an explicit Save click.
                // Mirrors the behaviour of HandleBrushInput / HandleEraserInput.
                if (Application.isPlaying) _persistence?.SaveAllDirty();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        // ── Apply-To-Layer picker handler ───────────────────────────────

        /// <summary>
        /// User clicked one of the Apply-To-Layer buttons in the Colliders panel.
        /// Updates <see cref="TileEditorState.ActiveCollisionTag"/>, asks the UI to
        /// repaint the picker row's highlight + value label, and emits a status hint.
        /// Invalid tag values fall back to <see cref="CollisionTagMap.Wildcard"/>.
        /// </summary>
        internal void OnCollisionTagChanged(string tag)
        {
            if (!CollisionTagMap.IsValidTag(tag)) tag = CollisionTagMap.Wildcard;
            _state.ActiveCollisionTag = tag;
            _ui?.RefreshCollisionTagPicker();
            _ui?.SetStatus($"Collider tag → {tag}");
        }

        /// <summary>
        /// Mirror every edit emitted by <see cref="TileBrush.Paint"/> into
        /// <see cref="CollisionTagMap"/>:
        ///   • Drawing → stamp <see cref="TileEditorState.ActiveCollisionTag"/> on each
        ///     cell that received a collider tile.
        ///   • Erasing → clear the cell so a future re-paint starts back at the user's
        ///     current active tag (no stale tag rides on top of a fresh paint).
        ///
        /// Lives outside the undo-recorded batch in M1 — see
        /// "Open questions: Undo del tag map" in the plan. The fallback when an Undo
        /// reinstates a collider without a tag is the map's wildcard default, which is
        /// the safe ("applies to all") choice.
        /// </summary>
        private void ApplyTagToEdits(System.Collections.Generic.List<TileEdit> edits, bool drawing)
        {
            if (edits == null || edits.Count == 0) return;
            string tag = drawing ? _state.ActiveCollisionTag : null;
            if (drawing && !CollisionTagMap.IsValidTag(tag))
                tag = CollisionTagMap.Wildcard;

            for (int i = 0; i < edits.Count; i++)
            {
                if (drawing) CollisionTags.Set(edits[i].Position, tag);
                else         CollisionTags.Clear(edits[i].Position);
            }
        }
    }
}