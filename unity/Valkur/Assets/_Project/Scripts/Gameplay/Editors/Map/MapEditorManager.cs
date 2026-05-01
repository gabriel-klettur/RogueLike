using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Runtime map editor migrated from Python workflow.
    /// Toggle with F7 to manage zones and define editable areas consumed by TileEditor.
    /// </summary>
    public partial class MapEditorManager : SingletonMonoBehaviour<MapEditorManager>, GameEditorManager.IGameEditor
    {
        [Header("References")]
        [SerializeField] private ZoneManager zoneManager;
        [SerializeField] private WorldGridBuilder worldGridBuilder;
        [SerializeField] private TileEditorManager tileEditorManager;

        [Header("Overlay")]
        [Tooltip("Minimum zone-border thickness in world units (close zoom).")]
        [SerializeField] private float overlayLineWidth = 0.12f;
        [Tooltip("Target on-screen thickness in pixels for zone borders. The line " +
                 "width is scaled with camera zoom so borders stay visible at any zoom.")]
        [SerializeField] private float overlayLinePixelWidth = 3.5f;
        [Tooltip("Maximum zone-border thickness in world units (far zoom cap).")]
        [SerializeField] private float overlayLineMaxWidth = 1.6f;

        private MapEditorState _state;
        private MapEditorInputHandler _input;
        private MapEditorUI _ui;
        private Camera _mainCamera;

        private readonly List<GameObject> _zoneOverlayObjects = new List<GameObject>();
        private Material _overlayLineMaterial;
        private GameObject _overlayRoot;

        private bool _isAddZoneFlowActive;
        private bool _hasPendingAddTarget;
        private Vector2Int _pendingAddZoneOffset;
        private GameObject _addZonePreviewObject;

        // Frame on which BeginAddZoneFlow was invoked. We ignore left-click on
        // the same frame so the click that activated the flow (over the
        // "Add Zone" UI button) cannot also race ahead and mark a target —
        // EventSystem.IsPointerOverGameObject can lag the UI raycast by one
        // frame depending on script execution order, which would otherwise
        // immediately drop the target wherever the button happened to sit.
        private int _addZoneFlowStartedFrame = -1;
        private string _pendingDeleteZoneName;

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        public bool IsActive => _state != null && _state.Active;

        // IGameEditor
        public string EditorName => "Map Editor";

        public void Activate()
        {
            if (_state != null && !_state.Active)
                ToggleActive();
        }

        public void Deactivate()
        {
            if (_state != null && _state.Active)
                ToggleActive();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
        }

        // NB: persistence DTOs are intentionally non-nested and internal.
        // Unity's JsonUtility has historically had issues serialising private
        // nested types (the "T must be a class with [Serializable]"
        // restriction interacts badly with nested generics like List<T>).
        // Keeping them at namespace scope removes any ambiguity and makes the
        // round-trip deterministic across Unity versions.

        private string PersistencePath => Path.Combine(Application.persistentDataPath, "map_editor_zones.json");

        protected override void OnSingletonAwake()
        {
            EnsureCoreInitialized();
        }

        /// <summary>
        /// Ensures non-serialized core state (state, input handler) is created.
        /// Safe to call repeatedly. Defends against hot-reload nulling private
        /// fields while in Play Mode.
        /// </summary>
        private void EnsureCoreInitialized()
        {
            if (_state == null)
                _state = new MapEditorState();
            if (_input == null)
            {
                _input = new MapEditorInputHandler();
                _input.CreateActions();
            }
        }

        protected virtual void OnEnable()
        {
            EnsureCoreInitialized();
        }

        private void Start()
        {
            _mainCamera = Camera.main;

            if (zoneManager == null)
                zoneManager = FindObjectOfType<ZoneManager>();
            if (worldGridBuilder == null)
                worldGridBuilder = FindObjectOfType<WorldGridBuilder>();
            if (tileEditorManager == null)
                tileEditorManager = TileEditorManager.Instance != null ? TileEditorManager.Instance : FindObjectOfType<TileEditorManager>();

            if (zoneManager == null)
            {
                var zoneManagerGo = new GameObject("ZoneManager");
                zoneManager = zoneManagerGo.AddComponent<ZoneManager>();
                Debug.LogWarning("[MapEditor] ZoneManager not found. Created runtime ZoneManager so F7 map editor can start.");
            }

            CreateOverlayRoot();
            CreateUI();
            LoadZonesFromDisk();
            HandleZonesChanged();

            zoneManager.OnZonesChanged += HandleZonesChanged;
            ApplyTileEditorConstraint();

            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        private void Update()
        {
            if (_input == null || zoneManager == null) return;

            if (_input.WasTogglePressed())
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }

            if (!_state.Active) return;

            UpdateOverlayLineWidths();
            HandleCameraPan();

            if (_ui != null && _ui.IsTypingInput)
                return;

            if (_ui != null && _ui.IsModalOpen)
            {
                // Dialog is open — block all map interactions until confirmed/cancelled
                return;
            }

            if (_isAddZoneFlowActive && _input.WasSelectPressed() && !_input.IsPointerOverUI()
                && Time.frameCount != _addZoneFlowStartedFrame)
            {
                MarkAddZoneTargetAtCursor();
                return;
            }

            if (_input.WasSelectPressed() && !_input.IsPointerOverUI())
                SelectZoneAtCursor();

            if (_input.WasCreatePressed())
                BeginAddZoneFlow();

            if (_input.WasDuplicatePressed())
                DuplicateSelectedZone();

            if (_input.WasDeletePressed())
                RequestDeleteSelectedZone();

            if (_input.WasRenamePressed())
                RenameSelectedZone(_ui != null ? _ui.NameInput : string.Empty);

            if (_input.WasToggleEditablePressed())
                ToggleSelectedZoneEditable();
        }

        private void ToggleActive()
        {
            _state.Active = !_state.Active;

            if (_ui != null)
                _ui.SetVisible(_state.Active);
            UpdateOverlayVisibility();

            if (_state.Active)
            {
                if (_ui != null)
                    _ui.SetStatus("Map Editor active. F7 to close.");
                Debug.Log("[MapEditor] Activated (F7).");
            }
            else
            {
                _cameraPan.Reset();
                Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
                CancelAddZoneFlow();
                if (_ui != null)
                    _ui.SetStatus("Map Editor inactive.");
                Debug.Log("[MapEditor] Deactivated (F7).");
            }
        }

        // ── External overlay sharing ────────────────────────────────────────────
        //
        // Other runtime editors (currently Tile Editor F8) can request that the
        // zone-border overlay be shown without activating the full Map Editor
        // UI. Useful for visualising zone boundaries while painting tiles so
        // the user can see where each zone starts and ends.

        private bool _externalOverlayRequested;

        /// <summary>
        /// Show or hide the zone-border overlay on behalf of an external
        /// caller (e.g. Tile Editor). The overlay stays visible while either
        /// the Map Editor itself is active OR an external request is held.
        /// Safe to call before <see cref="Start"/> — the request is honoured
        /// as soon as the overlay root is created.
        /// </summary>
        public void SetExternalOverlayRequest(bool show)
        {
            if (_externalOverlayRequested == show) return;
            _externalOverlayRequested = show;
            UpdateOverlayVisibility();
        }

        private void UpdateOverlayVisibility()
        {
            if (_overlayRoot == null) return;
            bool show = (_state != null && _state.Active) || _externalOverlayRequested;
            _overlayRoot.SetActive(show);
        }

        // Middle-mouse camera pan is handled by the shared EditorCameraPanController
        // (Scripts/Gameplay/Editors/EditorCameraPanController.cs). The previous
        // ~40-line implementation lived here and was duplicated in TileEditorManager
        // and BuildingsRuntimeEditor.
        private void HandleCameraPan() => _cameraPan.Tick();

        private void CreateOverlayRoot()
        {
            _overlayRoot = new GameObject("MapEditorZoneOverlayRoot");
            _overlayRoot.transform.SetParent(transform, false);
            _overlayRoot.SetActive(false);

            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                _overlayLineMaterial = new Material(shader);
            else
                Debug.LogWarning("[MapEditor] Shader 'Sprites/Default' not found. Zone overlays may not render.");
        }

        private void CreateUI()
        {
            var uiGo = new GameObject("MapEditorUI");
            uiGo.transform.SetParent(transform, false);
            _ui = uiGo.AddComponent<MapEditorUI>();
            _ui.Initialize(
                _state,
                OnZoneSelected,
                BeginAddZoneFlow,
                ConfirmAddZone,
                CancelAddZoneFlow,
                DuplicateSelectedZone,
                RequestDeleteSelectedZone,
                ConfirmDeleteSelectedZone,
                RenameSelectedZone,
                RenameZoneByName,
                ToggleSelectedZoneEditable,
                ToggleZoneEditableByName,
                SetRestrictTileEditing);
            _ui.SetVisible(false);
            _ui.SetRestrictToggle(_state.RestrictTileEditingToEditableZones);
        }

        private void OnZoneSelected(string zoneName)
        {
            _state.SelectZone(zoneName);
            if (_isAddZoneFlowActive && zoneManager.TryGetZone(zoneName, out var zone))
                _ui?.SetAddZoneSource(zone.zoneName, zone.editableInTileEditor);
            RefreshSelectionUIAndOverlay();
        }

        protected override void OnDestroy()
        {
            if (zoneManager != null)
                zoneManager.OnZonesChanged -= HandleZonesChanged;

            _input?.Dispose();

            if (tileEditorManager != null)
                tileEditorManager.ClearEditConstraint();

            if (_overlayLineMaterial != null)
                Destroy(_overlayLineMaterial);

            if (_addZonePreviewObject != null)
                Destroy(_addZonePreviewObject);

            base.OnDestroy();
        }
    }
}
