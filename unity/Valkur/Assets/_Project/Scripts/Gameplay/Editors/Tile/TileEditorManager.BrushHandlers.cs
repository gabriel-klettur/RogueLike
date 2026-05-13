using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Core.Input;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorManager
    {
        // Read mouse-button state through MouseInputManager so the legacy
        // backend kicks in when the new InputSystem package drops OS events
        // (recurring Unity 2022.3 Editor bug — see MouseInputManager XML).

        private void HandleEraserInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (MouseInputManager.WasLeftMouseButtonPressedThisFrame())
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
            else if (MouseInputManager.IsLeftMouseButtonPressed() && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                var edits = TileBrush.Erase(tilemap, cellPos, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
            }
            else if (MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
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

            if (MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(tilemap);
                var edits = TileBrush.FloodFill(tilemap, cellPos, _state.SelectedTile, canEditCell: CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                _undo.EndStroke();
                // Auto-persist: fill is atomic so we save immediately after the operation.
                if (Application.isPlaying) _persistence?.SaveAllDirty();
                // The flood we just painted invalidates the cached BFS preview
                // — every cell in the old set now matches the new tile, so the
                // hover-cell key alone isn't enough to detect the change.
                _gridOverlay?.InvalidateFillPreview();

                if (edits.Count == 0 && !CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F11 Map Editor.");
            }
        }

        private void HandleEyedropperInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (MouseInputManager.WasLeftMouseButtonPressedThisFrame())
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

        // Select tool input is dispatched by sub-mode (Single / Rect / Multi) from
        // TileEditorManager.SelectHandlers.cs — see HandleSelectInputDispatch there.
        private void HandleSelectInput(Tilemap tilemap, Vector3Int cellPos)
            => HandleSelectInputDispatch(tilemap, cellPos);

        // Middle-mouse camera pan is handled by the shared EditorCameraPanController
        // (Scripts/Gameplay/Editors/EditorCameraPanController.cs). The previous
        // ~50-line implementation lived here and was duplicated in BuildingsRuntimeEditor.
        private partial void HandleCameraPan() => _cameraPan.Tick();
    }
}