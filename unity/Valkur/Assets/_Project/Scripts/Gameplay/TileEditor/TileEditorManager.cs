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
    /// Toggle with F8.
    /// </summary>
    public partial class TileEditorManager : SingletonMonoBehaviour<TileEditorManager>, GameEditorManager.IGameEditor
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

        // IGameEditor
        public string EditorName => "Tile Editor";

        public void Activate()
        {
            if (_state != null && !_state.Active)
                HandleToggle();
        }

        public void Deactivate()
        {
            if (_state != null && _state.Active)
                HandleToggle();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
        }

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

            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);

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
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    HandleToggle();
            }

            if (!_state.Active) return;

            HandleToolShortcuts();
            HandleLayerScroll();
            HandleUndoRedo();
            HandleMouseInput();
            UpdateBrushPreview();
            UpdateGridCursor();
            UpdateViewPanelHover();
        }

        // ------------------------------------------------------------------
        // Input Handlers (partial — see TileEditorManager.InputHandlers.cs)
        // ------------------------------------------------------------------
        private partial void HandleToggle();
        private partial void HandleToolShortcuts();
        private partial void HandleLayerScroll();
        private partial void HandleUndoRedo();
        private partial void HandleMouseInput();

        // ------------------------------------------------------------------
        // Visuals (partial — see TileEditorManager.Visuals.cs)
        // ------------------------------------------------------------------
        private partial void CreateScreenBorderOverlay();
        private partial void CreateGridCursor();
        private partial void CreateBrushPreview();
        private partial void UpdateBrushPreview();
        private partial void UpdateGridCursor();
        private partial void UpdateViewPanelHover();
        // â”€â”€ Callbacks â”€â”€

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
