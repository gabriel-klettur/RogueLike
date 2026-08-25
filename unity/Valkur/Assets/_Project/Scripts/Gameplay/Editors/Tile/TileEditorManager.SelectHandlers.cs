using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Input;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Owns the Select tool's three sub-modes (Single / Rect / Multi): per-frame
    /// dispatch, the drag-rect commit, the brush-footprint fill, and the
    /// mode-change / clear-selection callbacks. Copy/Cut/Paste and the
    /// clipboard outline live in <c>TileEditorManager.Clipboard.cs</c>;
    /// Move-To-Layer lives in <c>TileEditorManager.MoveToLayer.cs</c> — both
    /// act on the selection this file maintains.
    ///
    /// All inputs go through the centralised <see cref="MouseInputManager"/> /
    /// <see cref="KeyboardInputManager"/> facades — never read Mouse.current /
    /// Keyboard.current here (project rule, see CLAUDE.md "Input pipeline").
    /// </summary>
    public partial class TileEditorManager
    {
        // ── Per-frame dispatch ────────────────────────────────────────────────

        private void HandleSelectInputDispatch(Tilemap tilemap, Vector3Int cellPos)
        {
            switch (_state.CurrentSelectMode)
            {
                case TileEditorState.SelectMode.Single: HandleSelectSingle(tilemap, cellPos); break;
                case TileEditorState.SelectMode.Rect:   HandleSelectRect  (tilemap, cellPos); break;
                case TileEditorState.SelectMode.Multi:  HandleSelectMulti (tilemap, cellPos); break;
            }
        }

        // ── Single ─────────────────────────────────────────────────────────────

        private void HandleSelectSingle(Tilemap tilemap, Vector3Int cellPos)
        {
            if (!MouseInputManager.WasLeftMouseButtonPressedThisFrame()) return;

            _state.SelectedCellPos = cellPos;
            _state.SelectedCells.Clear();
            ApplyBrushFootprintToSelection(cellPos);
            UpdateSelectionStatusForUI(tilemap);
            _ui?.RefreshClipboardButtons();
        }

        // ── Rect (click-and-drag) ─────────────────────────────────────────────

        private void HandleSelectRect(Tilemap tilemap, Vector3Int cellPos)
        {
            if (MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _state.RectDragStart   = cellPos;
                _state.RectDragCurrent = cellPos;
                _state.IsDragging      = true;
                return;
            }

            if (MouseInputManager.IsLeftMouseButtonPressed() && _state.IsDragging)
            {
                _state.RectDragCurrent = cellPos;
                return;
            }

            if (MouseInputManager.WasLeftMouseButtonReleasedThisFrame() && _state.IsDragging)
            {
                CommitRectSelection();
                _state.IsDragging      = false;
                _state.RectDragStart   = null;
                _state.RectDragCurrent = null;
                UpdateSelectionStatusForUI(tilemap);
                _ui?.RefreshClipboardButtons();
            }
        }

        private void CommitRectSelection()
        {
            if (!_state.RectDragStart.HasValue || !_state.RectDragCurrent.HasValue) return;

            int xMin = Mathf.Min(_state.RectDragStart.Value.x, _state.RectDragCurrent.Value.x);
            int yMin = Mathf.Min(_state.RectDragStart.Value.y, _state.RectDragCurrent.Value.y);
            int xMax = Mathf.Max(_state.RectDragStart.Value.x, _state.RectDragCurrent.Value.x);
            int yMax = Mathf.Max(_state.RectDragStart.Value.y, _state.RectDragCurrent.Value.y);

            _state.SelectedCells.Clear();
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
                _state.SelectedCells.Add(new Vector3Int(x, y, 0));

            _state.SelectedCellPos = new Vector3Int(xMin, yMax, 0); // top-left anchor
        }

        // ── Multi (click-to-add) ─────────────────────────────────────────────

        private void HandleSelectMulti(Tilemap tilemap, Vector3Int cellPos)
        {
            if (MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _state.SelectedCellPos = cellPos;
                ApplyBrushFootprintToSelection(cellPos);
                UpdateSelectionStatusForUI(tilemap);
                _ui?.RefreshClipboardButtons();
                return;
            }

            // RMB clears the selection accumulated in Multi mode (the only way to
            // "start over" without leaving the tool).
            if (MouseInputManager.WasRightMouseButtonPressedThisFrame())
            {
                ClearSelection();
                return;
            }
        }

        // ── Selection helpers ────────────────────────────────────────────────

        private void ApplyBrushFootprintToSelection(Vector3Int cellPos)
        {
            // Cursor = top-left of the brush footprint (matches every other tool).
            for (int dy = 0; dy < _state.BrushSize; dy++)
            for (int dx = 0; dx < _state.BrushSize; dx++)
                _state.SelectedCells.Add(new Vector3Int(cellPos.x + dx, cellPos.y - dy, cellPos.z));
        }

        private void UpdateSelectionStatusForUI(Tilemap tilemap)
        {
            if (_ui == null) return;

            int n = _state.SelectedCells.Count;
            if (n == 0) { _ui.SetStatus("Selection cleared"); return; }

            // Show first sampled tile so the user has a quick "what did I just pick".
            TileBase first = null;
            if (_state.SelectedCellPos.HasValue)
                first = tilemap.GetTile(_state.SelectedCellPos.Value);

            string label = first != null ? first.name : "(mixed)";
            _ui.SetStatus(n == 1 ? $"Selected {label}" : $"Selected {n} cells ({label})");
            Sprite sprite = first is Tile tt ? tt.sprite : null;
            _ui.UpdateViewPanelSelected(sprite, label);
        }

        // ── Public clear (RMB / Esc / button) ────────────────────────────────

        public void ClearSelection()
        {
            _state.SelectedCells.Clear();
            _state.RectDragStart = null;
            _state.RectDragCurrent = null;
            _state.IsDragging = false;
            ApplySelectionOverlay();
            // Clear the map-side clipboard outline so the yellow ring disappears
            // together with the green selection.
            ClearCopiedMapCells();
            // The picker mirrors the same Single/Rect/Multi semantics, so the
            // user expects "Clear Selection" to wipe both surfaces in one click.
            _ui?.ClearTilesetSelection();
            _ui?.SetStatus("Selection cleared");
            _ui?.RefreshClipboardButtons();
        }

        /// <summary>
        /// Push the current selection set + drag-rect anchors to the GL overlay
        /// immediately. Without this the overlay only re-syncs on the next frame
        /// where the cursor leaves UI (per-frame push in <c>UpdateGridCursor</c> is
        /// gated by <c>IsPointerOverUI</c>) — same pattern used by Colliders/View.
        /// </summary>
        private void ApplySelectionOverlay()
        {
            if (_gridOverlay == null) return;
            _gridOverlay.SetSelectedCells(_state.SelectedCells);
            _gridOverlay.SetRectDragPreview(_state.RectDragStart, _state.RectDragCurrent);
        }

        // ── Mode change callback (radio: only one ON) ────────────────────────

        private void OnSelectModeChanged(TileEditorState.SelectMode mode)
        {
            // End any in-flight rect drag if the user toggles modes mid-drag.
            _state.IsDragging      = false;
            _state.RectDragStart   = null;
            _state.RectDragCurrent = null;

            _state.CurrentSelectMode = mode;
            ApplySelectionOverlay();
            _ui?.RefreshSelectModeToggles();
            _ui?.SetStatus($"Select mode: {mode}");
        }
    }
}
