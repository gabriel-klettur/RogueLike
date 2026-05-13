using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Core;
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
            if (batch == null || batch.TargetTilemap == null) return;
            var collision = GetCollisionTilemap();
            if (collision == null || batch.TargetTilemap != collision) return;
            RegenerateCompositeCollider(collision);
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

            if (Valkur.Gameplay.MapEditor.MapEditorManager.HasInstance)
                Valkur.Gameplay.MapEditor.MapEditorManager.Instance
                    .SetExternalOverlayRequest(_state.ShowZoneGrid);
        }


        // â”€â”€ Helpers â”€â”€

        // Per-frame cache for GetCurrentTilemap / GetCollisionTilemap. WorldGridBuilder.GetTilemap
        // does a Transform.Find + GetComponent each call; without this cache the tile
        // editor pays 6+ Find calls per frame (UpdateGridCursor, UpdateViewPanelHover,
        // HandleMouseInput, etc.). Reset every frame by InvalidateTilemapFrameCache.
        private int      _tilemapCacheFrame      = -1;
        private Tilemap  _cachedCurrentTilemap;
        private TilemapLayerSetup.TilemapLayer _cachedCurrentLayer;
        private Tilemap  _cachedCollisionTilemap;

        // Per-frame cache for IsPointerOverUI. EventSystem.IsPointerOverGameObject
        // raycasts the entire UI canvas tree; calling it from each of the four
        // hot paths (HandleMouseInput, UpdateGridCursor, UpdateViewPanelHover,
        // CommitRectSelection) is wasted work — the value can't change inside
        // one frame.
        private int  _pointerOverUiFrame = -1;
        private bool _pointerOverUiCached;

        private void InvalidatePointerOverUiFrameCache()
        {
            _pointerOverUiFrame = -1;
        }

        internal bool IsPointerOverUiCached()
        {
            int f = Time.frameCount;
            if (_pointerOverUiFrame == f) return _pointerOverUiCached;
            _pointerOverUiFrame   = f;
            _pointerOverUiCached  = _input != null && _input.IsPointerOverUI();
            return _pointerOverUiCached;
        }

        /// <summary>
        /// Reset the per-frame tilemap cache. Called at the top of <see cref="Update"/>
        /// so any layer change made earlier in the frame is honoured on the next frame.
        /// </summary>
        private void InvalidateTilemapFrameCache()
        {
            _tilemapCacheFrame = Time.frameCount;
            _cachedCurrentTilemap = null;
            _cachedCollisionTilemap = null;
        }

        private Tilemap GetCurrentTilemap()
        {
            if (worldGridBuilder == null) return null;
            if (_tilemapCacheFrame == Time.frameCount
                && _cachedCurrentTilemap != null
                && _cachedCurrentLayer == _state.CurrentLayer)
                return _cachedCurrentTilemap;
            _cachedCurrentLayer = _state.CurrentLayer;
            _cachedCurrentTilemap = worldGridBuilder.GetTilemap(_state.CurrentLayer);
            return _cachedCurrentTilemap;
        }

        private Vector3Int GetCellUnderMouse(Tilemap tilemap)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            // Use MouseInputManager so the legacy backend supplies the position
            // when the new InputSystem package's Mouse.current is stale at (0,0).
            Vector3 screenPos = (Vector3)Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
            Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(screenPos);
            mouseWorld.z = 0f;
            return tilemap.WorldToCell(mouseWorld);
        }

        private Vector3 GetCellWorldCenter(Tilemap tilemap, Vector3Int cellPos)
        {
            Vector3 bottomLeft = tilemap.CellToWorld(cellPos);
            Vector3 cellSize = tilemap.cellSize;
            return bottomLeft + new Vector3(cellSize.x * 0.5f, cellSize.y * 0.5f, 0f);
        }

        // The Tile Editor must allow Brush, Eraser, Fill and Collider Draw/Erase
        // to operate on EVERY cell of EVERY zone. Earlier the F11 MapEditor could
        // install an `_editConstraint` (ZoneManager.IsTileInEditableZone) that
        // silently rejected paints in zones flagged `editableInTileEditor=false`
        // (e.g. the lobby) and in any cell outside a defined zone. That produced
        // dead spots on the map where the brush appeared to do nothing.
        //
        // Per product requirement the gate is now disabled at this single
        // choke point. SetEditConstraint / ClearEditConstraint remain on the
        // public API for backwards compatibility but no longer affect editing.
        // If a future need arises to re-introduce zone locks, restore the
        // original body and audit every TileBrush.* call site.
        private bool CanEditCell(Vector3Int cellPos) => true;

        protected override void OnDestroy()
        {
            _input?.Dispose();
            DisposeColliderTile();
            base.OnDestroy();
        }
    }
}