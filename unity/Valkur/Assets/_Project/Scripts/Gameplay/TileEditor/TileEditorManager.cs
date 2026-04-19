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
        private TileOverlayPersistence _persistence;
        private System.Func<Vector3Int, bool> _editConstraint;

        // Brush preview
        private GameObject _brushPreviewGo;

        // Screen border overlay
        private GameObject _borderOverlayGo;
        private TileEditorGridCursor _gridCursor;

        // Tile grid overlay (white cell borders)
        private GameObject _gridOverlayGo;
        private TileEditorGridOverlay _gridOverlay;

        // Perf probe overlay (Shift+F8)
        private TileEditorPerfProbe _perfProbe;

        // Saved FPS cap state (restored on Deactivate)
        private int _savedTargetFrameRate = -1;
        private int _savedVSyncCount = 1;

        // Middle-mouse camera pan (mirrors Python camera_pan.py)
        private bool _isPanning;
        private Vector2 _panAnchorScreenPos;
        private Vector3 _panAnchorCamPos;

        public TileEditorState State => _state;
        public bool IsActive => _state != null && _state.Active;

        /// <summary>
        /// Per-zone disk persistence for tile edits. Created in Start() once both the
        /// ZoneManager and the WorldGridBuilder are resolved. Survives play sessions
        /// via <c>Application.persistentDataPath/MapOverrides</c>.
        /// </summary>
        public TileOverlayPersistence Persistence => _persistence;

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

            if (GameEditorManager.EnsureInstance() != null) GameEditorManager.Instance.Register(this);

            var uiGo = new GameObject("TileEditorUI");
            uiGo.transform.SetParent(transform);
            _ui = uiGo.AddComponent<TileEditorUI>();
            _ui.Initialize(_state, tileCatalog,
                OnTileSelected, OnToolChanged, OnLayerChanged, OnBrushSizeChanged,
                OnLayerVisibilityChanged, OnUndoClicked, OnRedoClicked, OnSaveClicked,
                OnShowCollidersClicked, OnDrawCollidersClicked, OnEraseCollidersClicked);

            CreateBrushPreview();
            CreateScreenBorderOverlay();
            CreateGridCursor();
            CreateGridOverlay();
            CreatePerfProbe();

            InitializePersistence();
        }

        private void CreatePerfProbe()
        {
            var probeGo = new GameObject("TileEditorPerfProbe");
            probeGo.transform.SetParent(transform);
            _perfProbe = probeGo.AddComponent<TileEditorPerfProbe>();
            _perfProbe.Visible = false; // hidden by default; Shift+F8 to show
            Debug.Log("[TileEditor] Perf probe created (visible by default; Shift+F8 to toggle).");
        }

        private void InitializePersistence()
        {
            if (worldGridBuilder == null) return;
            var zoneManager = FindObjectOfType<ZoneManager>();
            if (zoneManager == null) return;
            _persistence = new TileOverlayPersistence(zoneManager, worldGridBuilder);
            _persistence.OnDirtyChanged += HandleDirtyChanged;
            _persistence.OnZoneSaved   += zone => _ui?.SetStatus($"Saved zone '{zone}'");
            _persistence.OnSaveFailed  += (zone, ex) => _ui?.SetStatus($"Save failed for '{zone}': {ex.Message}");
        }

        private void HandleDirtyChanged()
        {
            if (_ui == null || _persistence == null) return;
            _ui.SetDirtyState(_persistence.HasUnsavedChanges, _persistence.DirtyZoneCount);
        }

        /// <summary>Save every dirty zone to <c>persistentDataPath/MapOverrides</c>. Returns the count saved.</summary>
        public int SaveAllChanges()
        {
            if (_persistence == null) { _ui?.SetStatus("Persistence not ready."); return 0; }
            _undo.EndStroke();
            int n = _persistence.SaveAllDirty();
            _ui?.SetStatus(n == 0 ? "No unsaved changes" : $"Saved {n} zone(s) to disk");
            return n;
        }

        private void Update()
        {
            if (_state == null) return;

            if (_input.WasTogglePressed())
            {
                GameEditorManager.EnsureInstance().ToggleExclusive(this);
            }

            if (!_state.Active) return;

            // Shift+F8 toggles the perf probe overlay (only useful while editor is active).
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame &&
                (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) && _perfProbe != null)
            {
                _perfProbe.Visible = !_perfProbe.Visible;
                Debug.Log($"[TileEditor] Perf probe -> {(_perfProbe.Visible ? "ON" : "OFF")}");
            }

            HandleCameraPan();
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
        private partial void HandleCameraPan();

        // ------------------------------------------------------------------
        // Visuals (partial — see TileEditorManager.Visuals.cs)
        // ------------------------------------------------------------------
        private partial void CreateScreenBorderOverlay();
        private partial void CreateGridCursor();
        private partial void CreateGridOverlay();
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

        private bool CanEditCell(Vector3Int cellPos)
        {
            return _editConstraint == null || _editConstraint(cellPos);
        }

        protected override void OnDestroy()
        {
            _input?.Dispose();
            DisposeColliderTile();
            base.OnDestroy();
        }
    }
}
