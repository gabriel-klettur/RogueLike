using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Owns the map-side clipboard: Copy / Cut / Paste against
    /// <see cref="TileEditorState.Clipboard"/>, and the yellow copy-source
    /// outline (<see cref="_copiedMapCells"/>) drawn on the grid overlay.
    /// Acts on the selection maintained by <c>TileEditorManager.SelectHandlers.cs</c>.
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
            // Autosave is DEBOUNCED (TileOverlayPersistence.Autosave): the edits above already
                // armed it through MarkBatchDirty, and it flushes off-thread after a quiet
                // period. Forcing a synchronous SaveAllDirty() here measured 6.48 ms on the
                // main thread for a single painted cell. Explicit saves (F8 close, slot
                // switch, Save button) still flush synchronously and wait for this one.

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
            // Autosave is DEBOUNCED (TileOverlayPersistence.Autosave): the edits above already
                // armed it through MarkBatchDirty, and it flushes off-thread after a quiet
                // period. Forcing a synchronous SaveAllDirty() here measured 6.48 ms on the
                // main thread for a single painted cell. Explicit saves (F8 close, slot
                // switch, Save button) still flush synchronously and wait for this one.

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
