using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorManager
    {
        // ── Visual helpers ──

        private partial void CreateScreenBorderOverlay()
        {
            _borderOverlayGo = new GameObject("TileEditorBorderOverlay");
            _borderOverlayGo.transform.SetParent(transform);
            var overlay = _borderOverlayGo.AddComponent<TileEditorBorderOverlay>();
            overlay.Initialize();
            _borderOverlayGo.SetActive(false);
        }

        private partial void CreateGridCursor()
        {
            var cursorGo = new GameObject("TileEditorGridCursor");
            cursorGo.transform.SetParent(transform);
            _gridCursor = cursorGo.AddComponent<TileEditorGridCursor>();
            _gridCursor.Initialize();
            cursorGo.SetActive(false);
        }

        private partial void CreateGridOverlay()
        {
            _gridOverlayGo = new GameObject("TileEditorGridOverlay");
            _gridOverlayGo.transform.SetParent(transform);
            _gridOverlay = _gridOverlayGo.AddComponent<TileEditorGridOverlay>();
            _gridOverlay.Initialize(_mainCamera);
            _gridOverlayGo.SetActive(false);
        }

        private partial void CreateBrushPreview()
        {
            _brushPreviewGo = new GameObject("BrushPreview");
            _brushPreviewGo.transform.SetParent(transform);
            var sr = _brushPreviewGo.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 999;
            sr.color = new Color(1f, 1f, 1f, 0.4f);
            _brushPreviewGo.SetActive(false);
        }

        private partial void UpdateBrushPreview()
        {
            HideBrushPreview();
        }

        private void HideBrushPreview()
        {
            if (_brushPreviewGo != null && _brushPreviewGo.activeSelf)
                _brushPreviewGo.SetActive(false);
        }

        private partial void UpdateGridCursor()
        {
            if (_gridCursor == null) return;

            if (_input.IsPointerOverUI())
            {
                _gridCursor.gameObject.SetActive(false);
                return;
            }

            var tilemap = GetCurrentTilemap();
            if (tilemap == null)
            {
                _gridCursor.gameObject.SetActive(false);
                return;
            }

            _gridCursor.gameObject.SetActive(true);
            Vector3Int cellPos = GetCellUnderMouse(tilemap);

            // Track the last map cell under the cursor so OnPasteClicked can
            // use it as the paste anchor even when the pointer is over the picker
            // panel (IsPointerOverUI → falls back to this instead of stale
            // SelectedCellPos or the origin).
            _lastMapCursorCell = cellPos;
            Vector3 worldPos = GetCellWorldCenter(tilemap, cellPos);

            // Brush is anchored at the cursor cell (cursor = TOP-LEFT of the N×N footprint).
            // Footprint extends RIGHT and DOWN, so the visual rect's centre shifts +X / -Y.
            Vector3 cs = tilemap.cellSize;
            float offset = (_state.BrushSize - 1) * 0.5f;
            Vector3 brushCenter = worldPos + new Vector3(offset * cs.x, -offset * cs.y, 0f);
            _gridCursor.UpdateCursor(brushCenter, _state.BrushSize, cs, _state.CurrentTool);

            // GREEN selection indicator at last-interacted cell (also top-left anchor)
            if (_state.SelectedCellPos.HasValue)
            {
                Vector3 selWorld = GetCellWorldCenter(tilemap, _state.SelectedCellPos.Value);
                Vector3 selCenter = selWorld + new Vector3(offset * cs.x, -offset * cs.y, 0f);
                _gridCursor.SetSelection(selCenter, _state.BrushSize, cs);
            }
            else
            {
                _gridCursor.ClearSelection();
            }

            // Push selection + brush-stroke data to GL overlay
            if (_gridOverlay != null)
            {
                _gridOverlay.SetSelectedCell(_state.SelectedCellPos);
                _gridOverlay.SetBrushStrokeCells(_state.BrushStrokeCells);
                _gridOverlay.SetBrushSize(_state.BrushSize);
                _gridOverlay.SetCurrentTool(_state.CurrentTool);
                _gridOverlay.SetCollisionTilemap(GetCollisionTilemap());
                _gridOverlay.SetShowColliderOverlay(_state.ShowColliderOverlay);
                _gridOverlay.SetShowGridLines(_state.ShowGridLines);
                // Persistent Select-tool selection (drawn green, independent from
                // the brush stroke yellow preview) and the live rect-drag preview.
                _gridOverlay.SetSelectedCells(_state.SelectedCells);
                _gridOverlay.SetRectDragPreview(_state.RectDragStart, _state.RectDragCurrent);
                
                // Configure Fill preview when using Fill tool
                if (_state.CurrentTool == TileEditorState.Tool.Fill)
                {
                    var currentTilemap = GetCurrentTilemap();
                    if (currentTilemap != null)
                    {
                        _gridOverlay.SetFillPreview(currentTilemap, _state.SelectedTile);
                    }
                }
                else
                {
                    _gridOverlay.ClearFillPreview();
                }
            }
        }

        // ── View panel hover ──

        // Highest visual TilemapLayer index. Hover-scan iterates from this down to 0
        // so the TOPMOST rendered tile (drawn last) wins over deeper layers.
        private const int TopmostHoverLayerIndex = 8; // OverheadDetails

        private partial void UpdateViewPanelHover()
        {
            if (_ui == null) return;

            if (_input.IsPointerOverUI())
            {
                _ui.UpdateViewPanelHovered(null, "", "");
                return;
            }

            // Any tilemap shares the same grid — use the active one only as a cell-coord source.
            var probe = GetCurrentTilemap();
            if (probe == null || worldGridBuilder == null)
            {
                _ui.UpdateViewPanelHovered(null, "", "");
                return;
            }

            Vector3Int cellPos = GetCellUnderMouse(probe);

            if (TryFindHoveredVisibleLayer(cellPos, out var layer, out var tileBase))
            {
                Sprite sprite = null;
                if (tileBase is Tile t) sprite = t.sprite;
                _ui.UpdateViewPanelHovered(sprite, tileBase.name, $"{(int)layer}: {layer}");
            }
            else
            {
                _ui.UpdateViewPanelHovered(null, $"({cellPos.x},{cellPos.y}) empty", "");
            }
        }

        /// <summary>
        /// Scans visible layers top-down and returns the topmost one that has a tile
        /// at <paramref name="cellPos"/>. Skips the Collision layer (its tiles are
        /// alpha-zero authoring metadata, not what the user visually hovers) and any
        /// layer the user has hidden via the Layers panel toggle.
        ///
        /// <para>Returns <c>false</c> when no visible non-collision layer has a tile
        /// at the cell (or when the manager hasn't been wired yet).</para>
        /// </summary>
        internal bool TryFindHoveredVisibleLayer(Vector3Int cellPos,
            out TilemapLayerSetup.TilemapLayer layer, out TileBase tile)
        {
            layer = default;
            tile = null;
            if (worldGridBuilder == null || _ui == null) return false;

            for (int li = TopmostHoverLayerIndex; li >= 0; li--)
            {
                if (li == (int)TilemapLayerSetup.TilemapLayer.Collision) continue;
                if (!_ui.IsLayerVisible(li)) continue;

                var l = (TilemapLayerSetup.TilemapLayer)li;
                var tm = worldGridBuilder.GetTilemap(l);
                if (tm == null) continue;

                var tb = tm.GetTile(cellPos);
                if (tb == null) continue;

                layer = l;
                tile  = tb;
                return true;
            }
            return false;
        }
    }
}
