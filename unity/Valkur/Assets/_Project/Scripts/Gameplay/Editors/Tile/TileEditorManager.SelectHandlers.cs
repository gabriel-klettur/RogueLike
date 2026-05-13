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
        // ── Clipboard source tracking (map side) ─────────────────────────────
        // Snapshot of the cells that were selected at the moment of the most
        // recent Copy or Cut. Drawn as a thick bright-yellow outline on the
        // GL grid overlay so the user can see the clipboard source region even
        // after the green selection has moved elsewhere.
        // Lives here (not on TileEditorState) because it is a pure visual
        // concern — it drives only the overlay, not the paste logic.
        private readonly HashSet<Vector3Int> _copiedMapCells = new HashSet<Vector3Int>();

        /// <summary>
        /// Snapshot <paramref name="cells"/> into <see cref="_copiedMapCells"/> and push
        /// the updated set to the grid overlay. Call after every Copy / Cut operation.
        /// </summary>
        private void SnapshotCopiedMapCells(IEnumerable<Vector3Int> cells)
        {
            _copiedMapCells.Clear();
            if (cells != null)
                foreach (var c in cells) _copiedMapCells.Add(c);
            _gridOverlay?.SetCopiedCells(_copiedMapCells);
        }

        /// <summary>Clear the map-side clipboard outline (e.g. on ClearSelection or editor deactivate).</summary>
        private void ClearCopiedMapCells()
        {
            _copiedMapCells.Clear();
            _gridOverlay?.SetCopiedCells(null);
        }

        /// <summary>
        /// Wipe the map's pending Select-tool selection so a subsequent Ctrl+C
        /// doesn't read stale cells and shadow whatever the user just copied via
        /// the TILES PICKER. Called by <see cref="TileEditorUI.CommitTilesetSelection"/>
        /// when the picker commits a multi-tile selection — the picker copy is
        /// now the canonical clipboard source, so any green map selection from
        /// before is conceptually invalid.
        ///
        /// Clears the green selection set, the drag anchors, and the yellow
        /// map-side copy outline. Does NOT touch <see cref="TileEditorState.Clipboard"/>
        /// — the caller has just written the picker tiles into it and we must
        /// preserve them for the next paste.
        /// </summary>
        public void ClearMapSelectionFromPickerCommit()
        {
            _state.SelectedCells.Clear();
            _state.SelectedCellPos = null;
            _state.RectDragStart   = null;
            _state.RectDragCurrent = null;
            _state.IsDragging      = false;
            ClearCopiedMapCells();
            ApplySelectionOverlay();
            _ui?.RefreshClipboardButtons();
        }
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
            // Snapshot the source cells for the yellow clipboard outline.
            SnapshotCopiedMapCells(_state.SelectedCells);
            // Map just became the clipboard source — wipe the picker's stale
            // green/yellow visuals so the user sees a single active source.
            _ui?.ClearPickerSelectionFromMapCopy();
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
            //   1) the cell currently under the mouse (if pointer is NOT over UI
            //      AND the input handler is available — null in EditMode tests)
            //   2) the last map cell the cursor was over (_lastMapCursorCell) — lets
            //      the user hover the picker panel, pick a rect, and press Ctrl+V
            //      without the paste teleporting to origin or a stale SelectedCellPos.
            //      This is updated every frame IsPointerOverUI() is false.
            //   3) the last selected map cell (legacy fallback for tests without a camera)
            //   4) origin (0,0,0) as last resort
            Vector3Int anchor;
            if (_input != null && !_input.IsPointerOverUI())
                anchor = GetCellUnderMouse(tilemap);
            else if (_lastMapCursorCell.HasValue)
                anchor = _lastMapCursorCell.Value;
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

        // ── Move To Layer (action on existing selection) ────────────────────
        //
        // Take every cell in <see cref="TileEditorState.SelectedCells"/> that holds a
        // tile on the active layer and move it to <paramref name="destLayer"/> as a
        // single atomic operation: clear the cell on the source tilemap, paint the
        // same tile on the destination tilemap. Both half-edits are recorded in one
        // <see cref="TileEditBatch"/> via the per-edit <c>TargetTilemap</c> override
        // (see <see cref="TileEdit"/> docs) so a single Ctrl+Z reverses both halves.
        //
        // Picker-only selections are filtered out by the empty <c>SelectedCells</c>
        // check — the picker has no map cells so there is nothing to move.
        // Destination-equals-source is a no-op (preserves the existing scene rather
        // than silently churning through every cell). Cells filtered by
        // <see cref="CanEditCell"/> (out-of-zone / read-only) are skipped just like
        // every other bulk operation.
        //
        // After a successful move the editor auto-switches to the destination layer
        // (so the user sees the result in context). The selection is intentionally
        // left intact so the user can verify visually and chain another action.

        internal void OnMoveToLayerClicked(TilemapLayerSetup.TilemapLayer destLayer)
        {
            if (_state.SelectedCells.Count == 0) { _ui?.SetStatus("Nothing selected"); return; }
            if (destLayer == _state.CurrentLayer)
            {
                _ui?.SetStatus($"Already on layer {destLayer}");
                return;
            }

            var srcTilemap = GetCurrentTilemap();
            var dstTilemap = GetTilemapForLayer(destLayer);
            if (srcTilemap == null || dstTilemap == null)
            {
                _ui?.SetStatus("Tilemap unavailable");
                return;
            }

            _undo.StartStroke(srcTilemap);
            var edits = new List<TileEdit>();
            int moved = 0;

            foreach (var c in _state.SelectedCells)
            {
                if (!CanEditCell(c)) continue;
                var srcTile = srcTilemap.GetTile(c);
                if (srcTile == null) continue;

                var oldDst = dstTilemap.GetTile(c);

                // Phase A: clear source
                srcTilemap.SetTile(c, null);
                edits.Add(new TileEdit(c, srcTile, null, srcTilemap));

                // Phase B: paint destination (overwrites whatever was there)
                dstTilemap.SetTile(c, srcTile);
                edits.Add(new TileEdit(c, oldDst, srcTile, dstTilemap));

                moved++;
            }

            _undo.RecordEdits(edits);
            _undo.EndStroke();
            _persistence?.MarkBatchDirty(edits);
            if (Application.isPlaying) _persistence?.SaveAllDirty();

            if (moved == 0)
            {
                _ui?.SetStatus("No tiles to move on the source layer");
                return;
            }

            // Switch the editor to the destination layer so subsequent edits
            // land on the layer the user just populated. Uses the same path as
            // the right-panel layer selector — closes any in-flight stroke
            // (already closed by EndStroke above; idempotent) and refreshes UI.
            OnLayerChanged(destLayer);

            // If the move touched Collision (as source OR destination) the composite
            // collider was rebuilt against stale geometry; OnLayerChanged doesn't know,
            // so explicitly rebake here.
            var collision = GetCollisionTilemap();
            if (collision != null && (srcTilemap == collision || dstTilemap == collision))
                RegenerateCompositeCollider(collision);

            _ui?.SetStatus($"Moved {moved} cell(s) → {destLayer}");
            _ui?.RefreshClipboardButtons();
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
