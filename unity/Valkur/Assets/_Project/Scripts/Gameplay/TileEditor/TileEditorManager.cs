using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Thin coordinator for the in-game tile editor.
    /// Delegates input to TileEditorInputHandler, undo/redo to TileEditorUndoSystem.
    /// Toggle with F6.
    /// </summary>
    public class TileEditorManager : SingletonMonoBehaviour<TileEditorManager>
    {
        [Header("Tile Catalog")]
        [SerializeField] private TileCatalog tileCatalog;

        [Header("Grid Reference")]
        [Tooltip("If null, will search for WorldGridBuilder at runtime.")]
        [SerializeField] private WorldGridBuilder worldGridBuilder;

        private TileEditorState _state;
        private TileEditorUI _ui;
        private Camera _mainCamera;

        private TileEditorInputHandler _input;
        private TileEditorUndoSystem _undo;
        private System.Func<Vector3Int, bool> _editConstraint;

        // Brush preview
        private GameObject _brushPreviewGo;

        // Screen border overlay
        private GameObject _borderOverlayGo;
        private TileEditorGridCursor _gridCursor;

        public TileEditorState State => _state;
        public bool IsActive => _state != null && _state.Active;

        public void SetEditConstraint(System.Func<Vector3Int, bool> constraint)
        {
            _editConstraint = constraint;
        }

        public void ClearEditConstraint()
        {
            _editConstraint = null;
        }

        public void SetGridBuilder(WorldGridBuilder builder)
        {
            worldGridBuilder = builder;
        }

        protected override void OnSingletonAwake()
        {
            _state = new TileEditorState();
            _input = new TileEditorInputHandler();
            _input.CreateActions();
            _undo = new TileEditorUndoSystem();
        }

        private void Start()
        {
            if (_state == null) return;

            _mainCamera = Camera.main;

            if (worldGridBuilder == null)
                worldGridBuilder = FindObjectOfType<WorldGridBuilder>();

            if (tileCatalog == null)
                tileCatalog = TileCatalog.BuildFromResources();
            if (tileCatalog == null || tileCatalog.Entries.Count == 0)
                tileCatalog = Resources.Load<TileCatalog>("TileCatalog");
            if (tileCatalog != null)
                TileRegistry.Instance.Load(tileCatalog);
            else
                Debug.LogError("[TileEditor] No tiles found. Ensure sprites exist in Resources/Tiles/{category}/ folders.");

            var uiGo = new GameObject("TileEditorUI");
            uiGo.transform.SetParent(transform);
            _ui = uiGo.AddComponent<TileEditorUI>();
            _ui.Initialize(_state, tileCatalog,
                OnTileSelected, OnToolChanged, OnLayerChanged, OnBrushSizeChanged);

            CreateBrushPreview();
            CreateScreenBorderOverlay();
            CreateGridCursor();
        }

        private void Update()
        {
            if (_state == null) return;

            if (_input.WasTogglePressed())
                HandleToggle();

            if (!_state.Active) return;

            HandleToolShortcuts();
            HandleLayerScroll();
            HandleUndoRedo();
            HandleMouseInput();
            UpdateBrushPreview();
            UpdateGridCursor();
            UpdateViewPanelHover();
        }

        // ── Toggle ──

        private void HandleToggle()
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
                _ui.SetStatus("Tile Editor active. F6 to close.");
                if (_borderOverlayGo != null) _borderOverlayGo.SetActive(true);
                if (_gridCursor != null) _gridCursor.gameObject.SetActive(true);
                UpdateBorderToolLabel();
                Debug.Log("[TileEditor] Activated (F6)");
            }
            else
            {
                _undo.EndStroke();
                HideBrushPreview();
                if (_borderOverlayGo != null) _borderOverlayGo.SetActive(false);
                if (_gridCursor != null) _gridCursor.gameObject.SetActive(false);
                Debug.Log("[TileEditor] Deactivated (F6)");
            }
        }

        // ── Input dispatch ──

        private void HandleToolShortcuts()
        {
            var tool = _input.PollToolShortcut();
            if (tool.HasValue) OnToolChanged(tool.Value);
        }

        private void HandleLayerScroll()
        {
            int delta = _input.PollLayerScroll();
            if (delta == 0) return;
            int val = (int)_state.CurrentLayer + delta;
            if (val < 0) val = 8;
            if (val > 8) val = 0;
            OnLayerChanged((TilemapLayerSetup.TilemapLayer)val);
        }

        private void HandleUndoRedo()
        {
            int action = _input.PollUndoRedo();
            if (action == 1 && _undo.Undo()) _ui.SetStatus("Undo");
            else if (action == 2 && _undo.Redo()) _ui.SetStatus("Redo");
        }

        // ── Mouse input ──

        private void HandleMouseInput()
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
                _undo.StartStroke(tilemap);
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _state.IsDragging = true;

                if (edits.Count == 0 && !CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F7 Map Editor.");

                if (!_brushDiagLogged)
                {
                    _brushDiagLogged = true;
                    TileEditorDiagnostics.LogBrushDiagnostics(this, tilemap, cellPos, _state.SelectedTile);
                }
            }
            else if (mouse.leftButton.isPressed && _state.IsDragging)
            {
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
                _undo.StartStroke(tilemap);
                var edits = TileBrush.Erase(tilemap, cellPos, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _state.IsDragging = true;

                if (edits.Count == 0 && !CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F7 Map Editor.");
            }
            else if (mouse.leftButton.isPressed && _state.IsDragging)
            {
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
                _undo.StartStroke(tilemap);
                var edits = TileBrush.FloodFill(tilemap, cellPos, _state.SelectedTile, canEditCell: CanEditCell);
                _undo.RecordEdits(edits);
                _undo.EndStroke();

                if (edits.Count == 0 && !CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F7 Map Editor.");
            }
        }

        private void HandleEyedropperInput(Tilemap tilemap, Vector3Int cellPos)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
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
                var tile = tilemap.GetTile(cellPos);
                string info = tile != null ? tile.name : "(empty)";
                _ui.SetStatus($"Cell ({cellPos.x},{cellPos.y}) Layer:{_state.CurrentLayer} Tile:{info}");

                Sprite sprite = null;
                if (tile is Tile t) sprite = t.sprite;
                _ui.UpdateViewPanelSelected(sprite, info);
            }
        }

        // ── Visual helpers ──

        private void CreateScreenBorderOverlay()
        {
            _borderOverlayGo = new GameObject("TileEditorBorderOverlay");
            _borderOverlayGo.transform.SetParent(transform);
            var overlay = _borderOverlayGo.AddComponent<TileEditorBorderOverlay>();
            overlay.Initialize();
            _borderOverlayGo.SetActive(false);
        }

        private void CreateGridCursor()
        {
            var cursorGo = new GameObject("TileEditorGridCursor");
            cursorGo.transform.SetParent(transform);
            _gridCursor = cursorGo.AddComponent<TileEditorGridCursor>();
            _gridCursor.Initialize();
            cursorGo.SetActive(false);
        }

        private void CreateBrushPreview()
        {
            _brushPreviewGo = new GameObject("BrushPreview");
            _brushPreviewGo.transform.SetParent(transform);
            var sr = _brushPreviewGo.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 999;
            sr.color = new Color(1f, 1f, 1f, 0.4f);
            _brushPreviewGo.SetActive(false);
        }

        private void UpdateBrushPreview()
        {
            HideBrushPreview();
        }

        private void HideBrushPreview()
        {
            if (_brushPreviewGo != null) _brushPreviewGo.SetActive(false);
        }

        private void UpdateGridCursor()
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
            _gridCursor.UpdateCursor(worldPos, _state.BrushSize, _state.CurrentTool);
        }

        // ── Callbacks ──

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

        private void OnBrushSizeChanged(int newSize)
        {
            _state.BrushSize = Mathf.Clamp(newSize, 1, 5);
            _ui.RefreshBrushSizeLabel();
        }

        // ── View panel hover ──

        private void UpdateViewPanelHover()
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

        // ── Helpers ──

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

        private bool CanEditCell(Vector3Int cellPos)
        {
            return _editConstraint == null || _editConstraint(cellPos);
        }

        protected override void OnDestroy()
        {
            _input?.Dispose();
            base.OnDestroy();
        }
    }
}
