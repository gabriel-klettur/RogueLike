using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorManager
    {

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
                _persistence?.MarkBatchDirty(edits);
                _state.IsDragging = true;

                if (edits.Count == 0 && !CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F11 Map Editor.");
            }
            else if (mouse.leftButton.isPressed && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                var edits = TileBrush.Erase(tilemap, cellPos, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                // Auto-persist: flush dirty zones immediately so erased tiles
                // survive a scene reload without requiring an explicit Save click.
                if (Application.isPlaying) _persistence?.SaveAllDirty();
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
                _persistence?.MarkBatchDirty(edits);
                _undo.EndStroke();
                // Auto-persist: fill is atomic so we save immediately after the operation.
                if (Application.isPlaying) _persistence?.SaveAllDirty();

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

                // Multi-tile selection: gather every tile under the brush footprint
                // (BrushSize x BrushSize, cursor = top-left, extends right + down).
                int count = 0;
                int empty = 0;
                TileBase firstTile = null;
                Vector3Int firstTilePos = cellPos;
                _state.BrushStrokeCells.Clear();

                for (int dy = 0; dy < _state.BrushSize; dy++)
                for (int dx = 0; dx < _state.BrushSize; dx++)
                {
                    var p = new Vector3Int(cellPos.x + dx, cellPos.y - dy, cellPos.z);
                    _state.BrushStrokeCells.Add(p);
                    var tile = tilemap.GetTile(p);
                    if (tile == null) { empty++; continue; }
                    if (firstTile == null) { firstTile = tile; firstTilePos = p; }
                    count++;
                }

                int total = _state.BrushSize * _state.BrushSize;
                string info;
                Sprite previewSprite = null;
                if (total == 1)
                {
                    info = firstTile != null ? firstTile.name : "(empty)";
                }
                else if (count == 0)
                {
                    info = $"(empty x{total})";
                }
                else
                {
                    info = $"{firstTile.name} (+{count - 1} more, {empty} empty)";
                }
                if (firstTile is Tile t) previewSprite = t.sprite;

                _ui.UpdateViewPanelSelected(previewSprite, info);
            }
        }

        // Middle-mouse camera pan is handled by the shared EditorCameraPanController
        // (Scripts/Gameplay/Editors/EditorCameraPanController.cs). The previous
        // ~50-line implementation lived here and was duplicated in BuildingsRuntimeEditor.
        private partial void HandleCameraPan() => _cameraPan.Tick();
    }
}