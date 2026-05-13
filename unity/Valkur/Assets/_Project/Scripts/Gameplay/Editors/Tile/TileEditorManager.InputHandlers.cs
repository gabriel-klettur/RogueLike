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
                _state.CurrentTool = TileEditorState.Tool.Select;
                _state.BrushStrokeCells.Clear();
                _state.CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
                _state.CurrentColliderMode = TileEditorState.ColliderMode.None;
                _ui.RefreshToolHighlights();
                _ui.RefreshLayerLabel();
                _ui.RefreshBrushSizeLabel();
                _ui.RefreshTilePicker();
                _ui.RefreshColliderToggles();
                _ui.SetStatus("Tile Editor active. F8 to close. Player movement enabled.");
                if (_borderOverlayGo != null) _borderOverlayGo.SetActive(true);
                if (_gridCursor != null) _gridCursor.gameObject.SetActive(true);
                if (_gridOverlayGo != null) _gridOverlayGo.SetActive(true);
                // Sync the Map Editor's zone-border overlay with the View panel's
                // "Zone Grid" toggle (default: hidden). Authors enable it explicitly
                // from View → Zone Grid when they want to see zone boundaries.
                ApplyViewOverlayVisibility();
                UpdateBorderToolLabel();
                // Pull persisted terrain data + auto-cure variants. Safe to call here
                // even if no overlays exist on disk — it short-circuits.
                LoadAllTerrainsFromDisk();
                // Camera stays attached so the player can walk and test tile colliders.
                // Middle-mouse pan is still available via HandleCameraPan() → DetachFollow.

                // ── Force uncapped FPS while editing so we can see real perf ──
                _savedTargetFrameRate = Application.targetFrameRate;
                _savedVSyncCount = QualitySettings.vSyncCount;
                Application.targetFrameRate = 120;
                QualitySettings.vSyncCount = 0;
                Debug.Log($"[TileEditor] FPS cap override: target=120 vSync=0 (was {_savedTargetFrameRate}/{_savedVSyncCount})");

                Debug.Log("[TileEditor] Activated (F8)");
            }
            else
            {
                _undo.EndStroke();
                // Flush any unsaved tile edits so changes aren't lost when the editor is closed.
                if (Application.isPlaying) _persistence?.SaveAllDirty();
                _state.SelectedCellPos = null;
                _lastMapCursorCell = null;
                _state.BrushStrokeCells.Clear();
                // Clear the map-side clipboard outline on deactivate so it doesn't
                // linger as a ghost on the next editor session.
                ClearCopiedMapCells();
                // Clear the picker-side copy-highlight (yellow CopyHL overlays) too.
                _ui?.ClearTilesetCopyHighlight();
                HideBrushPreview();
                if (_borderOverlayGo != null) _borderOverlayGo.SetActive(false);
                if (_gridCursor != null) _gridCursor.gameObject.SetActive(false);
                if (_gridOverlayGo != null) _gridOverlayGo.SetActive(false);
                // Release the zone-border overlay request so it hides unless
                // the Map Editor itself is still active.
                if (Valkur.Gameplay.MapEditor.MapEditorManager.HasInstance)
                    Valkur.Gameplay.MapEditor.MapEditorManager.Instance.SetExternalOverlayRequest(false);
                // Keep perf probe state — re-enabled on activate via CreatePerfProbe defaults
                _cameraPan.Reset();
                _doubleClick.Reset();
                // Re-attach in case middle-mouse pan had detached the camera during editing.
                if (Valkur.Gameplay.CameraSetup.Instance != null)
                    Valkur.Gameplay.CameraSetup.Instance.ReattachFollow();

                // Restore previous FPS cap
                Application.targetFrameRate = _savedTargetFrameRate;
                QualitySettings.vSyncCount = _savedVSyncCount;
                Debug.Log($"[TileEditor] FPS cap restored: target={_savedTargetFrameRate} vSync={_savedVSyncCount}");

                Debug.Log("[TileEditor] Deactivated (F8)");
            }
        }

        // ── Input dispatch ──

        private partial void HandleToolShortcuts()
        {
            var tool = _input.PollToolShortcut();
            if (tool.HasValue) OnToolChanged(tool.Value);
        }

        private partial void HandleCameraZoom()
        {
            float scrollDelta = _input.PollZoom();
            if (Mathf.Abs(scrollDelta) < 0.1f) return;

            var camSetup = Valkur.Gameplay.CameraSetup.Instance;
            if (camSetup == null) return;

            // Multiplicative zoom — same model as gameplay (CameraSetup.Update).
            // No clamp by design: zoom is intentionally unbounded so we can find
            // the rendering pipeline's breaking point.
            float currentSize = camSetup.GetCurrentOrthographicSize();
            float zoomFactor = 1f - Mathf.Sign(scrollDelta) * 0.25f;
            float newSize = currentSize * zoomFactor;

            camSetup.SetTileEditorZoom(newSize);
        }

        private partial void HandleUndoRedo()
        {
            int action = _input.PollUndoRedo();
            if (action == 1)
            {
                // BUG 1 fix: close any in-flight stroke first. Without this, pressing
                // Ctrl+Z while still dragging the brush would (a) skip the open batch
                // (it never gets pushed to the undo stack), and (b) leak it into the
                // next stroke when EndStroke fires on mouse-up — producing phantom
                // undo entries that don't match anything visible.
                _undo?.EndStroke();
                var batch = _undo?.Undo();
                if (batch != null) { _persistence?.MarkBatchDirty(batch.Edits); _ui?.SetStatus("Undo"); RegenerateColliderIfNeeded(batch); }
            }
            else if (action == 2)
            {
                _undo?.EndStroke();
                var batch = _undo?.Redo();
                if (batch != null) { _persistence?.MarkBatchDirty(batch.Edits); _ui?.SetStatus("Redo"); RegenerateColliderIfNeeded(batch); }
            }

            // (Ctrl+S removed — every edit path auto-flushes on mouse-up via
            // _persistence.SaveAllDirty(). Manual save was redundant.)

            // ── Select tool: clipboard hotkeys (Ctrl+C/X/V) and Esc to clear ──
            // Gated on CurrentTool so they don't shadow other editors' shortcuts.
            if (_state.CurrentTool == TileEditorState.Tool.Select)
            {
                bool ctrl = Valkur.Core.Input.KeyboardInputManager.IsCtrlHeld();
                if (ctrl && Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.C, KeyCode.C))
                    OnCopyClicked();
                else if (ctrl && Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.X, KeyCode.X))
                    OnCutClicked();
                else if (ctrl && Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.V, KeyCode.V))
                    OnPasteClicked();
                else if (Valkur.Core.Input.KeyboardInputManager.WasEscapePressedThisFrame())
                    ClearSelection();
            }
        }

        // ── Mouse input ──

        private partial void HandleMouseInput()
        {
            if (IsPointerOverUiCached())
            {
                // If the user released LMB while the pointer was over UI (e.g. the
                // TILES PICKER), any in-flight drag on the map must be cancelled here —
                // the tool handlers never see the release event because this guard
                // returns early, leaving _state.IsDragging = true indefinitely and
                // preventing the picker from registering subsequent clicks correctly.
                if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame()
                    && _state.IsDragging)
                {
                    // Commit any pending Rect selection (uses the last known drag end).
                    if (_state.CurrentTool == TileEditorState.Tool.Select
                        && _state.CurrentSelectMode == TileEditorState.SelectMode.Rect)
                    {
                        CommitRectSelection();
                        var releaseTilemap = GetCurrentTilemap();
                        if (releaseTilemap != null) UpdateSelectionStatusForUI(releaseTilemap);
                        _ui?.RefreshClipboardButtons();
                        ApplySelectionOverlay();
                    }
                    _state.IsDragging      = false;
                    _state.RectDragStart   = null;
                    _state.RectDragCurrent = null;
                    // Close any brush/eraser stroke that was released over UI too.
                    _undo?.EndStroke();
                    if (Application.isPlaying) _persistence?.SaveAllDirty();
                }
                return;
            }

            // Collider edit modes (Draw / Erase) take priority over the regular tool
            // dispatch — they always target the Collision tilemap and ignore SelectedTile.
            if (IsColliderEditModeActive())
            {
                HandleColliderInput();
                return;
            }

            var tilemap = GetCurrentTilemap();
            if (tilemap == null) return;

            Vector3Int cellPos = GetCellUnderMouse(tilemap);

            switch (_state.CurrentTool)
            {
                case TileEditorState.Tool.Brush:          HandleBrushInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Eraser:         HandleEraserInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Fill:           HandleFillInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Eyedropper:     HandleEyedropperInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Select:         HandleSelectInput(tilemap, cellPos); break;
                case TileEditorState.Tool.AutoTileRegion: HandleAutoTileRegionInput(tilemap, cellPos); break;
            }
        }

        private bool _brushDiagLogged;

        private void HandleBrushInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (_state.SelectedTile == null) return;

            // Use MouseInputManager so the legacy backend kicks in if the new
            // InputSystem package is dropping OS events.
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _state.BrushStrokeCells.Clear();
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(tilemap);
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
                _state.IsDragging = true;

                if (!_brushDiagLogged)
                {
                    _brushDiagLogged = true;
                    TileEditorDiagnostics.LogBrushDiagnostics(this, tilemap, cellPos, _state.SelectedTile);
                }
            }
            else if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed() && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
            }
            else if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                _state.BrushStrokeCells.Clear();
                // Auto-persist: flush dirty zones immediately so painted tiles
                // survive a scene reload without requiring an explicit Save click.
                if (Application.isPlaying) _persistence?.SaveAllDirty();
            }
        }

        /// <summary>Mark all cells in the brush footprint anchored at <paramref name="anchor"/> (cursor cell = top-left, footprint extends right + down).</summary>
        private void AddCellsToBrushStroke(Vector3Int anchor)
        {
            for (int dy = 0; dy < _state.BrushSize; dy++)
                for (int dx = 0; dx < _state.BrushSize; dx++)
                    _state.BrushStrokeCells.Add(new Vector3Int(anchor.x + dx, anchor.y - dy, 0));
        }

    }
}