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
                // Autosave is DEBOUNCED (TileOverlayPersistence.Autosave): the edits above already
                // armed it through MarkBatchDirty, and it flushes off-thread after a quiet
                // period. Forcing a synchronous SaveAllDirty() here measured 6.48 ms on the
                // main thread for a single painted cell. Explicit saves (F8 close, slot
                // switch, Save button) still flush synchronously and wait for this one.
            }
        }

        private void HandleFillInput(Tilemap tilemap, Vector3Int cellPos)
        {
            // Same contract as HandleBrushInput: never fail silently on a click.
            if (_state.SelectedTile == null)
            {
                if (MouseInputManager.WasLeftMouseButtonPressedThisFrame())
                    _ui?.SetStatus(TileEditorConstants.NoTileSelectedHint);
                return;
            }

            if (MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(tilemap);
                var edits = TileBrush.FloodFill(tilemap, cellPos, _state.SelectedTile, canEditCell: CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                _undo.EndStroke();
                // Autosave is DEBOUNCED (TileOverlayPersistence.Autosave): the edits above already
                // armed it through MarkBatchDirty, and it flushes off-thread after a quiet
                // period. Forcing a synchronous SaveAllDirty() here measured 6.48 ms on the
                // main thread for a single painted cell. Explicit saves (F8 close, slot
                // switch, Save button) still flush synchronously and wait for this one.
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