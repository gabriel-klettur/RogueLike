using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Input;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Owns the Select tool's three sub-modes (Single / Rect / Multi) plus the
    /// Copy / Cut / Paste / Clear-selection callbacks. Split from BrushHandlers
    /// because the original Select had grown beyond the single-click footprint
    /// behaviour and now coordinates a clipboard, a drag rectangle, and a
    /// persistent selection set.
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

        // ── Copy / Cut / Paste ──────────────────────────────────────────────

        private void OnCopyClicked()
        {
            if (_state.SelectedCells.Count == 0) { _ui?.SetStatus("Nothing selected"); return; }
            var tilemap = GetCurrentTilemap();
            if (tilemap == null) { _ui?.SetStatus("No tilemap on current layer"); return; }

            var bounds = ComputeSelectionBounds(_state.SelectedCells);
            var arr    = new TileBase[bounds.size.x, bounds.size.y];
            foreach (var c in _state.SelectedCells)
            {
                int dx = c.x - bounds.xMin;
                int dy = c.y - bounds.yMin;
                arr[dx, dy] = tilemap.GetTile(c);
            }

            _state.Clipboard = new TileClipboard
            {
                Tiles        = arr,
                SourceBounds = bounds,
                SourceLayer  = _state.CurrentLayer,
                IsCut        = false,
            };
            _ui?.RefreshClipboardButtons();
            _ui?.SetStatus($"Copied {bounds.size.x}×{bounds.size.y} (layer {_state.CurrentLayer})");
        }

        private void OnCutClicked()
        {
            if (_state.SelectedCells.Count == 0) { _ui?.SetStatus("Nothing selected"); return; }
            OnCopyClicked();
            if (_state.Clipboard == null) return; // OnCopy bailed (no tilemap, etc.)
            _state.Clipboard.IsCut = true;

            var tilemap = GetCurrentTilemap();
            if (tilemap == null) return;

            _undo.StartStroke(tilemap);
            var edits = new List<TileEdit>();
            foreach (var c in _state.SelectedCells)
            {
                if (!CanEditCell(c)) continue;
                var old = tilemap.GetTile(c);
                if (old == null) continue;
                edits.Add(new TileEdit(c, old, null));
                tilemap.SetTile(c, null);
            }
            _undo.RecordEdits(edits);
            _undo.EndStroke();
            _persistence?.MarkBatchDirty(edits);
            if (Application.isPlaying) _persistence?.SaveAllDirty();

            _ui?.SetStatus($"Cut {edits.Count} cell(s)");
        }

        private void OnPasteClicked()
        {
            if (_state.Clipboard == null || _state.Clipboard.IsEmpty)
            {
                _ui?.SetStatus("Clipboard empty");
                return;
            }

            var tilemap = GetCurrentTilemap();
            if (tilemap == null) { _ui?.SetStatus("No tilemap on current layer"); return; }

            // Anchor priority:
            //   1) the cell currently under the mouse (if pointer is over the canvas
            //      AND the input handler is available — null in EditMode tests)
            //   2) the last selected cell (if any)
            //   3) origin (0,0) as last resort
            Vector3Int anchor;
            if (_input != null && !_input.IsPointerOverUI())
                anchor = GetCellUnderMouse(tilemap);
            else if (_state.SelectedCellPos.HasValue)
                anchor = _state.SelectedCellPos.Value;
            else
                anchor = Vector3Int.zero;

            int w = _state.Clipboard.Width;
            int h = _state.Clipboard.Height;

            _undo.StartStroke(tilemap);
            var edits = new List<TileEdit>();
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                var tile = _state.Clipboard.Tiles[dx, dy];
                if (tile == null) continue;
                // Anchor is the top-left of the pasted block; flip dy so the block
                // grows downward (matches brush convention: cursor extends right + down).
                var pos = new Vector3Int(anchor.x + dx, anchor.y - ((h - 1) - dy), anchor.z);
                if (!CanEditCell(pos)) continue;
                edits.Add(new TileEdit(pos, tilemap.GetTile(pos), tile));
                tilemap.SetTile(pos, tile);
            }
            _undo.RecordEdits(edits);
            _undo.EndStroke();
            _persistence?.MarkBatchDirty(edits);
            if (Application.isPlaying) _persistence?.SaveAllDirty();

            _ui?.SetStatus($"Pasted {edits.Count} cell(s)");
        }

        private static BoundsInt ComputeSelectionBounds(HashSet<Vector3Int> cells)
        {
            int xMin = int.MaxValue, yMin = int.MaxValue;
            int xMax = int.MinValue, yMax = int.MinValue;
            foreach (var c in cells)
            {
                if (c.x < xMin) xMin = c.x;
                if (c.y < yMin) yMin = c.y;
                if (c.x > xMax) xMax = c.x;
                if (c.y > yMax) yMax = c.y;
            }
            int w = (xMax - xMin) + 1;
            int h = (yMax - yMin) + 1;
            return new BoundsInt(xMin, yMin, 0, w, h, 1);
        }
    }
}
