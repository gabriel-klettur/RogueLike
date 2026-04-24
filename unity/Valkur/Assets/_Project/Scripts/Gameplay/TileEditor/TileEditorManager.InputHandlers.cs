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
                UpdateBorderToolLabel();
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
                _state.SelectedCellPos = null;
                _state.BrushStrokeCells.Clear();
                HideBrushPreview();
                if (_borderOverlayGo != null) _borderOverlayGo.SetActive(false);
                if (_gridCursor != null) _gridCursor.gameObject.SetActive(false);
                if (_gridOverlayGo != null) _gridOverlayGo.SetActive(false);
                // Keep perf probe state — re-enabled on activate via CreatePerfProbe defaults
                _isPanning = false;
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
            if (action == 1)
            {
                var batch = _undo.Undo();
                if (batch != null) { _persistence?.MarkBatchDirty(batch.Edits); _ui.SetStatus("Undo"); }
            }
            else if (action == 2)
            {
                var batch = _undo.Redo();
                if (batch != null) { _persistence?.MarkBatchDirty(batch.Edits); _ui.SetStatus("Redo"); }
            }

            // Ctrl+S → save all dirty zones to disk
            var kb = Keyboard.current;
            if (kb != null && kb.sKey.wasPressedThisFrame &&
                (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed))
            {
                SaveAllChanges();
            }
        }

        // ── Mouse input ──

        private partial void HandleMouseInput()
        {
            if (_input.IsPointerOverUI()) return;

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
                _state.BrushStrokeCells.Clear();
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(tilemap);
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
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
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                _state.BrushStrokeCells.Clear();
            }
        }

        /// <summary>Mark all cells in the brush footprint anchored at <paramref name="anchor"/> (cursor cell = top-left, footprint extends right + down).</summary>
        private void AddCellsToBrushStroke(Vector3Int anchor)
        {
            for (int dy = 0; dy < _state.BrushSize; dy++)
                for (int dx = 0; dx < _state.BrushSize; dx++)
                    _state.BrushStrokeCells.Add(new Vector3Int(anchor.x + dx, anchor.y - dy, 0));
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

        // ── Middle-mouse camera pan ──
        // Mirrors Python camera_pan.py: handle_pan_state()
        //   MOUSEBUTTONDOWN 2 → save anchor
        //   MOUSEMOTION while panning → camera.offset -= rel / zoom
        //   MOUSEBUTTONUP 2 → stop panning

        private partial void HandleCameraPan()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            // Camera normally follows the player so the developer can walk and test
            // tile colliders. Middle-mouse drag temporarily detaches and pans; on
            // release the camera re-attaches to the player.
            var camSetup = Valkur.Gameplay.CameraSetup.Instance;
            if (camSetup == null) return;

            if (mouse.middleButton.wasPressedThisFrame)
            {
                camSetup.DetachFollow();
                Transform anchorT = camSetup.GetDetachedTransform();
                if (anchorT != null)
                {
                    _isPanning = true;
                    _panAnchorScreenPos = mouse.position.ReadValue();
                    _panAnchorCamPos = anchorT.position;
                }
            }
            else if (mouse.middleButton.wasReleasedThisFrame)
            {
                _isPanning = false;
                camSetup.ReattachFollow();
            }

            if (_isPanning && mouse.middleButton.isPressed)
            {
                Transform vcamT = camSetup.GetDetachedTransform();
                if (vcamT == null) return;

                Vector2 currentScreenPos = mouse.position.ReadValue();
                Vector2 screenDelta = currentScreenPos - _panAnchorScreenPos;

                float unitsPerPixel = _mainCamera.orthographicSize * 2f / Screen.height;
                Vector3 worldDelta = new Vector3(screenDelta.x, screenDelta.y, 0f) * unitsPerPixel;
                Vector3 newPos = _panAnchorCamPos - worldDelta;
                newPos.z = vcamT.position.z;
                vcamT.position = newPos;
            }
        }
    }
}
