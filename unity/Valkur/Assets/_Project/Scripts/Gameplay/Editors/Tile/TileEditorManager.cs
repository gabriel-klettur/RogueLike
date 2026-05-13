using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Thin coordinator for the in-game tile editor.
    /// Delegates input to TileEditorInputHandler, undo/redo to TileEditorUndoSystem.
    /// Toggle with F8.
    /// </summary>
    public partial class TileEditorManager : SingletonMonoBehaviour<TileEditorManager>, GameEditorManager.IGameEditor, IAllowsPlayerMovement, ISuspendsPlayerCombat
    {
        [Header("Tile Catalog")]
        [SerializeField] private TileCatalog tileCatalog;

        [Header("Grid Reference")]
        [Tooltip("If null, will search for WorldGridBuilder at runtime.")]
        [SerializeField] private WorldGridBuilder worldGridBuilder;

        private TileEditorState _state = new TileEditorState();
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

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();
        // Double-click on a zone → centre + frame it on screen.
        private readonly EditorDoubleClickDetector _doubleClick = new EditorDoubleClickDetector();

        /// <summary>
        /// The last tilemap cell the cursor was hovering over the map (updated every
        /// frame that <c>IsPointerOverUI()</c> is false). Used by
        /// <c>OnPasteClicked</c> as the paste anchor when the pointer is over the
        /// picker panel or another UI element — so pressing Ctrl+V while hovering
        /// the picker panel pastes at the last map position, not at origin or the
        /// stale <see cref="TileEditorState.SelectedCellPos"/>.
        /// </summary>
        private Vector3Int? _lastMapCursorCell;

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
            _state ??= new TileEditorState();
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
                OnLayerVisibilityChanged, OnUndoClicked, OnRedoClicked,
                OnShowCollidersClicked, OnDrawCollidersClicked, OnEraseCollidersClicked,
                onPerfToggle: null,
                onShowGridLinesClicked: OnShowGridLinesClicked,
                onShowZoneGridClicked:  OnShowZoneGridClicked,
                onSelectModeChanged:    OnSelectModeChanged,
                onCopyClicked:          OnCopyClicked,
                onCutClicked:           OnCutClicked,
                onPasteClicked:         OnPasteClicked,
                onClearSelectionClicked: ClearSelection);

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
            // Resolve which map slot is active so per-slot overlay routing
            // hits the right directory from the very first edit. Defaults to
            // WorldId.Base (legacy flat layout) when the active-slot pointer
            // is missing — i.e. on first run before any slot has been picked.
            WorldId worldId = MapEditorMapSlots.ResolveBootActiveWorldId();
            _persistence = new TileOverlayPersistence(zoneManager, worldGridBuilder, repository: null, worldId: worldId);
            // OnDirtyChanged is no longer wired to the UI — the manual Save button +
            // dirty indicator were removed once every edit path became auto-flushing
            // on mouse-up. The event is still raised by TileOverlayPersistence in
            // case a future debug/diagnostic surface wants to subscribe.
            _persistence.OnZoneSaved   += zone => _ui?.SetStatus($"Saved zone '{zone}'");
            _persistence.OnSaveFailed  += (zone, ex) => _ui?.SetStatus($"Save failed for '{zone}': {ex.Message}");
            // Hand the persistence layer the auto-tile terrain map so saves include
            // the per-cell terrain matrix alongside the layer matrices.
            _persistence.TerrainMap = TerrainMap;
        }

        /// <summary>
        /// Re-bind the tile-overlay persistence layer to a new <see cref="WorldId"/>.
        /// Called by the Map Editor when the active map slot changes so subsequent
        /// edits write to the new slot's directory instead of the previous one.
        ///
        /// Any pending dirty zones are flushed to the OUTGOING slot's directory
        /// before the bind flips — losing them silently on a slot switch is the
        /// worse failure mode. Subscribers (status messages on save / failure)
        /// are re-attached on the new instance.
        /// </summary>
        public void RebindToWorld(WorldId worldId)
        {
            // Close any in-flight stroke first so its undo entry isn't stranded
            // half-recorded across the rebind.
            _undo?.EndStroke();

            if (_persistence != null)
            {
                if (_persistence.WorldId == worldId) return;
                if (_persistence.HasUnsavedChanges)
                {
                    int flushed = _persistence.SaveAllDirty();
                    if (flushed > 0)
                        Debug.Log($"[TileEditor] Flushed {flushed} dirty zone(s) to outgoing world '{_persistence.WorldId}' before slot switch.");
                }
            }

            // Rebuild against the new world id. ZoneManager + WorldGridBuilder
            // are scene-singletons, safe to re-resolve via FindObjectOfType.
            if (worldGridBuilder == null)
                worldGridBuilder = FindObjectOfType<WorldGridBuilder>();
            var zoneManager = FindObjectOfType<ZoneManager>();
            if (worldGridBuilder == null || zoneManager == null)
            {
                Debug.LogWarning($"[TileEditor] RebindToWorld('{worldId}') skipped — grid or zone manager missing.");
                return;
            }

            _persistence = new TileOverlayPersistence(zoneManager, worldGridBuilder, repository: null, worldId: worldId);
            _persistence.OnZoneSaved  += zone => _ui?.SetStatus($"Saved zone '{zone}'");
            _persistence.OnSaveFailed += (zone, ex) => _ui?.SetStatus($"Save failed for '{zone}': {ex.Message}");
            _persistence.TerrainMap = TerrainMap;
            Debug.Log($"[TileEditor] Tile-overlay persistence rebound to world '{worldId}'.");
        }

        /// <summary>Save every dirty zone to <c>persistentDataPath/MapOverrides</c>. Returns the count saved.</summary>
        public int SaveAllChanges()
        {
            // Always close the active stroke first — even if persistence is unavailable
            // (e.g. ZoneManager not yet resolved). The user invoked "save my work";
            // committing the in-flight batch to the undo stack is the correct response
            // regardless of whether the disk write can proceed.
            _undo?.EndStroke();
            if (_persistence == null) { _ui?.SetStatus("Persistence not ready."); return 0; }
            int n = _persistence.SaveAllDirty();
            _ui?.SetStatus(n == 0 ? "No unsaved changes" : $"Saved {n} zone(s) to disk");
            return n;
        }

        private void Update()
        {
            // Guard against the rare frame where Update() runs before
            // OnSingletonAwake() has wired _input/_state — happens after a
            // domain reload when the manager component is reactivated by
            // Unity before its lifecycle methods fire.
            if (_state == null || _input == null) return;

            if (_input.WasTogglePressed())
            {
                GameEditorManager.EnsureInstance().ToggleExclusive(this);
            }

            if (!_state.Active) return;

            // Reset per-frame caches (tilemap lookups + pointer-over-UI) so the
            // first reader pays the resolve cost and every later reader in the
            // same frame hits the cache. The tilemap path saves ~6 Transform.Find
            // calls/frame; the pointer-over-UI cache cuts EventSystem raycasts
            // from 3 (HandleMouseInput, UpdateGridCursor, UpdateViewPanelHover)
            // down to 1.
            InvalidateTilemapFrameCache();
            InvalidatePointerOverUiFrameCache();

            // Shift+F8 toggles the perf probe overlay (only useful while editor is active).
            // Routed through KeyboardInputManager for legacy fallback.
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F8, KeyCode.F8) &&
                Valkur.Core.Input.KeyboardInputManager.IsShiftHeld() && _perfProbe != null)
            {
                _perfProbe.Visible = !_perfProbe.Visible;
                Debug.Log($"[TileEditor] Perf probe -> {(_perfProbe.Visible ? "ON" : "OFF")}");
            }

            HandleCameraPan();
            HandleToolShortcuts();
            HandleCameraZoom();
            HandleUndoRedo();
            HandleMouseInput();
            HandleDoubleClickFrame();
            UpdateBrushPreview();
            UpdateGridCursor();
            UpdateViewPanelHover();
        }

        // Double-click on a zone → centre + frame it on screen. Coexists with
        // the single-click handler (which still paints / picks tiles) — the
        // first click registers normally, only the second click triggers the
        // framing.
        private void HandleDoubleClickFrame()
        {
            if (!_doubleClick.PollLeftDouble()) return;
            var zoneManager = FindObjectOfType<ZoneManager>();
            if (zoneManager == null) return;
            string framed = EditorZoneFraming.TryFrameZoneAtCursor(zoneManager);
            if (!string.IsNullOrEmpty(framed))
                _ui?.SetStatus($"Centered on zone '{framed}'.");
        }

        // ------------------------------------------------------------------
        // Input Handlers (partial — see TileEditorManager.InputHandlers.cs)
        // ------------------------------------------------------------------
        private partial void HandleToggle();
        private partial void HandleToolShortcuts();
        private partial void HandleCameraZoom();
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

    }
}
