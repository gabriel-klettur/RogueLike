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

            if (_state.CurrentTool == TileEditorState.Tool.Select ||
                _state.CurrentTool == TileEditorState.Tool.Eyedropper)
            {
                OnToolChanged(TileEditorState.Tool.Brush);
            }
        }

        private void OnToolChanged(TileEditorState.Tool tool)
        {
            _undo.EndStroke();
            _state.CurrentTool = tool;
            _state.IsDragging = false;
            _state.BrushStrokeCells.Clear();
            _ui.RefreshToolHighlights();
            _ui.SetStatus($"Tool: {tool}");
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
            _state.CurrentLayer = layer;
            _ui.RefreshLayerLabel();
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
            _state.BrushSize = Mathf.Clamp(newSize, 1, 5);
            _ui.RefreshBrushSizeLabel();
        }

        private void OnUndoClicked()
        {
            // End any active stroke first so the in-progress batch is committed before undoing.
            _undo.EndStroke();
            var batch = _undo.Undo();
            if (batch != null)
            {
                _persistence?.MarkBatchDirty(batch.Edits);
                _ui.SetStatus("Undo");
            }
            else
                _ui.SetStatus("Nothing to undo");
        }

        private void OnRedoClicked()
        {
            _undo.EndStroke();
            var batch = _undo.Redo();
            if (batch != null)
            {
                _persistence?.MarkBatchDirty(batch.Edits);
                _ui.SetStatus("Redo");
            }
            else
                _ui.SetStatus("Nothing to redo");
        }

        private void OnSaveClicked()
        {
            SaveAllChanges();
        }


        // â”€â”€ Helpers â”€â”€

        private Tilemap GetCurrentTilemap()
        {
            if (worldGridBuilder == null) return null;
            return worldGridBuilder.GetTilemap(_state.CurrentLayer);
        }

        private Vector3Int GetCellUnderMouse(Tilemap tilemap)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            var mouse = Mouse.current;
            Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(
                mouse != null ? (Vector3)mouse.position.ReadValue() : Vector3.zero);
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