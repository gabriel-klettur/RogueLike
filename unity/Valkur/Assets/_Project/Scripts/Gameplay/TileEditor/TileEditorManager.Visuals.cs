using UnityEngine;
using UnityEngine.Tilemaps;

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
            }
        }

        // ── View panel hover ──

        private partial void UpdateViewPanelHover()
        {
            if (_ui == null) return;

            if (_input.IsPointerOverUI())
            {
                _ui.UpdateViewPanelHovered(null, "", "");
                return;
            }

            var tilemap = GetCurrentTilemap();
            if (tilemap == null)
            {
                _ui.UpdateViewPanelHovered(null, "", "");
                return;
            }

            Vector3Int cellPos = GetCellUnderMouse(tilemap);
            var tileBase = tilemap.GetTile(cellPos);
            if (tileBase != null)
            {
                Sprite sprite = null;
                if (tileBase is Tile t) sprite = t.sprite;
                string layerName = $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}";
                _ui.UpdateViewPanelHovered(sprite, tileBase.name, layerName);
            }
            else
            {
                _ui.UpdateViewPanelHovered(null, $"({cellPos.x},{cellPos.y}) empty",
                    $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}");
            }
        }
    }
}
