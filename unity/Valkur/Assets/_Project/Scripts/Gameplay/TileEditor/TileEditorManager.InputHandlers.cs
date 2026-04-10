using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorManager
    {
        // ── Toggle ──

        private partial void HandleToggle()
        {
            _state.Active = !_state.Active;
            _ui.SetVisible(_state.Active);

            if (_state.Active)
            {
                _state.CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
                _ui.RefreshToolHighlights();
                _ui.RefreshLayerLabel();
                _ui.RefreshBrushSizeLabel();
                _ui.RefreshTilePicker();
                _ui.SetStatus("Tile Editor active. F8 to close.");
                if (_borderOverlayGo != null) _borderOverlayGo.SetActive(true);
                if (_gridCursor != null) _gridCursor.gameObject.SetActive(true);
                UpdateBorderToolLabel();
                Debug.Log("[TileEditor] Activated (F8)");
            }
            else
            {
                _undo.EndStroke();
                _state.SelectedCellPos = null;
                HideBrushPreview();
                if (_borderOverlayGo != null) _borderOverlayGo.SetActive(false);
                if (_gridCursor != null) _gridCursor.gameObject.SetActive(false);
                Debug.Log("[TileEditor] Deactivated (F8)");
            }
        }

        // ── Input dispatch ──

        private partial void HandleToolShortcuts()
        {
            var tool = _input.PollToolShortcut();
            if (tool.HasValue) OnToolChanged(tool.Value);
        }

        private partial void HandleLayerScroll()
        {
            int delta = _input.PollLayerScroll();
            if (delta == 0) return;
            int val = (int)_state.CurrentLayer + delta;
            if (val < 0) val = 8;
            if (val > 8) val = 0;
            OnLayerChanged((TilemapLayerSetup.TilemapLayer)val);
        }

        private partial void HandleUndoRedo()
        {
            int action = _input.PollUndoRedo();
            if (action == 1 && _undo.Undo()) _ui.SetStatus("Undo");
            else if (action == 2 && _undo.Redo()) _ui.SetStatus("Redo");
        }

        // ── Mouse input ──

        private partial void HandleMouseInput()
        {
            if (_input.IsPointerOverUI()) return;

            var tilemap = GetCurrentTilemap();
            if (tilemap == null) return;

            Vector3Int cellPos = GetCellUnderMouse(tilemap);

            switch (_state.CurrentTool)
            {
                case TileEditorState.Tool.Brush:    HandleBrushInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Eraser:   HandleEraserInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Fill:     HandleFillInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Eyedropper: HandleEyedropperInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Select:   HandleSelectInput(tilemap, cellPos); break;
            }
        }

        private bool _brushDiagLogged;

        private void HandleBrushInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (_state.SelectedTile == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(tilemap);
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _state.IsDragging = true;

                if (edits.Count == 0 && !CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F11 Map Editor.");

                if (!_brushDiagLogged)
                {
                    _brushDiagLogged = true;
                    TileEditorDiagnostics.LogBrushDiagnostics(this, tilemap, cellPos, _state.SelectedTile);
                }
            }
            else if (mouse.leftButton.isPressed && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                _undo.RecordEdits(TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize, CanEditCell));
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                _undo.EndStroke();
                _state.IsDragging = false;
            }
        }

        private void HandleEraserInput(Tilemap tilemap, Vector3Int cellPos)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(tilemap);
                var edits = TileBrush.Erase(tilemap, cellPos, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _state.IsDragging = true;

                if (edits.Count == 0 && !CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F11 Map Editor.");
            }
            else if (mouse.leftButton.isPressed && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                _undo.RecordEdits(TileBrush.Erase(tilemap, cellPos, _state.BrushSize, CanEditCell));
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                _undo.EndStroke();
                _state.IsDragging = false;
            }
        }

        private void HandleFillInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (_state.SelectedTile == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(tilemap);
                var edits = TileBrush.FloodFill(tilemap, cellPos, _state.SelectedTile, canEditCell: CanEditCell);
                _undo.RecordEdits(edits);
                _undo.EndStroke();

                if (edits.Count == 0 && !CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F11 Map Editor.");
            }
        }

        private void HandleEyedropperInput(Tilemap tilemap, Vector3Int cellPos)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _state.SelectedCellPos = cellPos;
                var picked = TileBrush.Pick(tilemap, cellPos);
                if (picked != null)
                {
                    _state.SelectedTile = picked;
                    _ui.SetStatus($"Picked: {picked.name}");

                    Sprite sprite = null;
                    if (picked is Tile pickedTile) sprite = pickedTile.sprite;
                    _ui.UpdateViewPanelSelected(sprite, picked.name);
                    _ui.UpdateSelectedTilePreview(sprite, picked.name);

                    OnToolChanged(TileEditorState.Tool.Brush);
                }
            }
        }

        private void HandleSelectInput(Tilemap tilemap, Vector3Int cellPos)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _state.SelectedCellPos = cellPos;
                var tile = tilemap.GetTile(cellPos);
                string info = tile != null ? tile.name : "(empty)";
                _ui.SetStatus($"Cell ({cellPos.x},{cellPos.y}) Layer:{_state.CurrentLayer} Tile:{info}");

                Sprite sprite = null;
                if (tile is Tile t) sprite = t.sprite;
                _ui.UpdateViewPanelSelected(sprite, info);
            }
        }
    }
}
