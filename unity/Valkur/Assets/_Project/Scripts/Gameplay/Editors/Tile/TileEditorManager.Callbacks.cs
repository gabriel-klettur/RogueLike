using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorManager
    {

        private void OnTileSelected(TileCatalog.TileEntry entry)
        {
            _state.SelectedTile = entry.tile;
            _state.SelectedCategory = entry.category;
            _ui.SetStatus($"Selected: {entry.tileName}");

            Sprite preview = entry.preview;
            if (preview == null && entry.tile is Tile tileAsset)
                preview = tileAsset.sprite;
            _ui.UpdateSelectedTilePreview(preview, entry.tileName);

            // Auto-switch to Brush so the user can paint immediately after
            // picking a tile. Skipped while the user is mid-flow building a
            // multi-tile selection in the picker (Select tool + Rect/Multi
            // sub-mode): switching tools there would fire the leavingSelect
            // reset (see OnToolChanged below) and collapse the selection set
            // back to Single mode, breaking the workflow the user just started.
            bool inMultiTileSelectFlow =
                _state.CurrentTool == TileEditorState.Tool.Select &&
                _state.CurrentSelectMode != TileEditorState.SelectMode.Single;
            if (!inMultiTileSelectFlow &&
                (_state.CurrentTool == TileEditorState.Tool.Select ||
                 _state.CurrentTool == TileEditorState.Tool.Eyedropper))
            {
                OnToolChanged(TileEditorState.Tool.Brush);
            }
        }

        private void OnToolChanged(TileEditorState.Tool tool)
        {
            // Re-clicking the SELECT button while Select is already active toggles
            // the SelectModes panel open/closed. Lets the user hide the panel
            // without leaving the tool (and without hunting for the [x] header
            // button); preserves selection and clipboard.
            if (tool == TileEditorState.Tool.Select && _state.CurrentTool == TileEditorState.Tool.Select)
            {
                _ui?.ToggleDropdown("selectmodes");
                return;
            }

            _undo?.EndStroke();

            // User decision: leaving Select clears the selection set and resets the
            // sub-mode to Single. The clipboard is NOT cleared — it survives so the
            // user can switch to Brush, edit, return to Select, and Ctrl+V.
            bool leavingSelect = _state.CurrentTool == TileEditorState.Tool.Select &&
                                 tool != TileEditorState.Tool.Select;
            if (leavingSelect)
            {
                _state.SelectedCells.Clear();
                _state.CurrentSelectMode = TileEditorState.SelectMode.Single;
                _state.RectDragStart = null;
                _state.RectDragCurrent = null;
                // Mirror the cleared selection state to the overlay immediately so
                // the green outlines vanish without waiting for the next on-canvas
                // frame (the per-frame push in UpdateGridCursor early-returns over UI).
                _gridOverlay?.SetSelectedCells(_state.SelectedCells);
                _gridOverlay?.SetRectDragPreview(null, null);
            }

            _state.CurrentTool = tool;
            _state.IsDragging = false;
            _state.BrushStrokeCells.Clear();
            _ui?.RefreshToolHighlights();
            _ui?.RefreshSelectModeToggles();
            _ui?.RefreshClipboardButtons();
            // The Tiles picker swaps content when entering / leaving AutoTileRegion
            // (raw sprite tiles ↔ terrain chips). Refresh on every tool change so
            // the swap happens in both directions.
            _ui?.RefreshTilePicker();
            _ui?.SetStatus($"Tool: {tool}");
            UpdateBorderToolLabel();
        }

        private void UpdateBorderToolLabel()
        {
            if (_borderOverlayGo == null) return;
            var overlay = _borderOverlayGo.GetComponent<TileEditorBorderOverlay>();
            if (overlay != null)
                overlay.SetToolLabel(_state.CurrentTool.ToString().ToUpper());
        }

        private void OnLayerChanged(TilemapLayerSetup.TilemapLayer layer)
        {
            // End any in-flight stroke before swapping layers. The batch is bound to
            // a single TargetTilemap (the active layer's tilemap at StartStroke time),
            // so letting a stroke span layers would record edits whose Position is
            // valid on the new layer but get applied to the old layer's tilemap on
            // Undo — silent corruption that's hard to spot.
            _undo?.EndStroke();
            _state.CurrentLayer = layer;
            _ui?.RefreshLayerLabel();
        }

        private void OnLayerVisibilityChanged(TilemapLayerSetup.TilemapLayer layer, bool visible)
        {
            if (worldGridBuilder == null) return;
            var tilemap = worldGridBuilder.GetTilemap(layer);
            if (tilemap == null) return;
            var renderer = tilemap.GetComponent<TilemapRenderer>();
            if (renderer != null)
                renderer.enabled = visible;
        }

        private void OnBrushSizeChanged(int newSize)
        {
            _state.BrushSize = Mathf.Clamp(newSize, TileEditorConstants.MinBrushSize, TileEditorConstants.MaxBrushSize);
            _ui.RefreshBrushSizeLabel();
        }

        /// <summary>
        /// AUTO brush modifier toggle (Tiles panel checkbox, next to the RULESET
        /// button). Flips <see cref="TileEditorState.AutoBrushMode"/> and returns the
        /// new state so the click closure that built the checkbox
        /// (<c>TileEditorUIBuilder.LeftPanel.Tiles.cs</c>) can repaint it directly —
        /// the row isn't tracked in <c>UIRefs</c>, it's a lightweight flag no other
        /// panel needs to read back.
        ///
        /// Explains itself on the way IN, not just on the first failed stroke: if
        /// the tile currently selected for painting has no usable ruleset, turning
        /// AUTO on would silently paint nothing on every subsequent drag — the same
        /// "broken editor" failure mode <see cref="TileEditorConstants.NoTileSelectedHint"/>
        /// already exists to prevent for the plain brush. <see cref="HandleBrushInput"/>
        /// re-checks the same condition on every stroke (the selection can change
        /// after this toggle fires), so this message is a courtesy, not the only guard.
        /// </summary>
        public bool OnAutoBrushToggleClicked()
        {
            _state.AutoBrushMode = !_state.AutoBrushMode;

            if (_state.AutoBrushMode)
            {
                var (terrain, reason) = ResolveAutoBrushTerrain();
                _ui?.SetStatus(string.IsNullOrEmpty(terrain)
                    ? $"AUTO brush ON - {reason}"
                    : $"AUTO brush ON - painting terrain '{terrain}'.");
            }
            else
            {
                _ui?.SetStatus("AUTO brush OFF - painting raw tiles again.");
            }

            return _state.AutoBrushMode;
        }

        private void OnUndoClicked()
        {
            // End any active stroke first so the in-progress batch is committed before undoing.
            _undo?.EndStroke();
            var batch = _undo?.Undo();
            if (batch != null)
            {
                _persistence?.MarkBatchDirty(batch.Edits);
                _ui?.SetStatus("Undo");
                RegenerateColliderIfNeeded(batch);
            }
            else
                _ui?.SetStatus("Nothing to undo");
        }

        private void OnRedoClicked()
        {
            _undo?.EndStroke();
            var batch = _undo?.Redo();
            if (batch != null)
            {
                _persistence?.MarkBatchDirty(batch.Edits);
                _ui?.SetStatus("Redo");
                RegenerateColliderIfNeeded(batch);
            }
            else
                _ui?.SetStatus("Nothing to redo");
        }

        /// <summary>
        /// After Undo/Redo restores edits on the Collision layer, the painted tile data
        /// changes but the <c>CompositeCollider2D</c> shape is cached — Physics2D queries
        /// keep seeing the pre-undo geometry until we explicitly rebake it. Mirrors the
        /// regen call done by <c>HandleColliderInput</c> after each draw/erase stroke.
        /// </summary>
        private void RegenerateColliderIfNeeded(TileEditBatch batch)
        {
            if (batch == null) return;
            var collision = GetCollisionTilemap();
            if (collision == null) return;

            if (batch.TargetTilemap == collision)
            {
                RegenerateCompositeCollider(collision);
                return;
            }

            // Cross-tilemap batch (e.g. Move-To-Layer with Collision as source or
            // destination): the batch's fallback tilemap is not Collision, but one
            // or more individual edits target it through TileEdit.TargetTilemap.
            // Scan the edits and rebake the composite once if any hit Collision.
            for (int i = 0; i < batch.Edits.Count; i++)
            {
                if (batch.Edits[i].TargetTilemap == collision)
                {
                    RegenerateCompositeCollider(collision);
                    return;
                }
            }
        }

        // ── View panel handlers ──

        private void OnShowGridLinesClicked()
        {
            _state.ShowGridLines = !_state.ShowGridLines;
            ApplyViewOverlayVisibility();
            _ui?.RefreshViewToggles();
            _ui?.SetStatus(_state.ShowGridLines ? "Tiles grid visible" : "Tiles grid hidden");
        }

        private void OnShowZoneGridClicked()
        {
            _state.ShowZoneGrid = !_state.ShowZoneGrid;
            ApplyViewOverlayVisibility();
            _ui?.RefreshViewToggles();
            _ui?.SetStatus(_state.ShowZoneGrid ? "Zone grid visible" : "Zone grid hidden");
        }

        /// <summary>
        /// Toggle the per-tile layer-digit overlay (View panel → "Show Tile Layer").
        /// Cheap to flip — the overlay's draw method early-outs when the flag is off,
        /// and the per-frame cost when ON is bounded by the viewport, not the map size.
        /// </summary>
        private void OnShowTileLayerClicked()
        {
            _state.ShowTileLayerOverlay = !_state.ShowTileLayerOverlay;
            ApplyViewOverlayVisibility();
            _ui?.RefreshViewToggles();
            _ui?.SetStatus(_state.ShowTileLayerOverlay ? "Tile layer overlay visible" : "Tile layer overlay hidden");
        }

        /// <summary>
        /// Push the View-panel flags to their respective renderers so toggles respond
        /// instantly — without this, the overlay would only see the new value on the first
        /// frame the cursor leaves the UI (the per-frame push in <c>UpdateGridCursor</c>
        /// early-returns over UI).
        ///
        /// Tiles Grid drives the editor's own GL overlay; Zone Grid delegates to the
        /// Map Editor's <c>SetExternalOverlayRequest</c> so the Tile Editor never draws
        /// its own zone outlines — this avoids a duplicate cyan ring on top of the green
        /// Map-Editor outlines (the cyan/green doubling was the original bug).
        /// </summary>
        private void ApplyViewOverlayVisibility()
        {
            _gridOverlay?.SetShowGridLines(_state.ShowGridLines);

            // Tile-layer overlay: resolve the 9 visual tilemaps once (lazy) and push
            // the array reference + the on/off flag to the overlay. Resolution is
            // skipped while the toggle is OFF so the cost stays at zero — flipping
            // the toggle ON triggers a single Find+GetComponent sweep across the 9
            // layers (cached as long as the WorldGridBuilder hierarchy is stable).
            if (_gridOverlay != null)
            {
                if (_state.ShowTileLayerOverlay)
                    _gridOverlay.SetLayerTilemaps(EnsureLayerTilemapsCache());
                _gridOverlay.SetShowTileLayer(_state.ShowTileLayerOverlay);
            }

            if (Valkur.Gameplay.MapEditor.MapEditorManager.HasInstance)
                Valkur.Gameplay.MapEditor.MapEditorManager.Instance
                    .SetExternalOverlayRequest(_state.ShowZoneGrid);
        }

    }
}