using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
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
        private string _pendingDeleteZoneName;

        // Camera pan (middle-mouse drag — mirrors TileEditor behaviour)
        private bool _isPanning;
        private Vector2 _panAnchorScreenPos;
        private Vector3 _panAnchorCamPos;

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

        [Serializable]
        private class ZonePersistenceFile
        {
            public bool restrictTileEditingToEditableZones;
            public int nextZoneIndex;
            public List<ZonePersistenceEntry> zones = new List<ZonePersistenceEntry>();
        }

        [Serializable]
        private class ZonePersistenceEntry
        {
            public string zoneName;
            public int gridOffsetX;
            public int gridOffsetY;
            public bool editableInTileEditor;
        }

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

            if (_isAddZoneFlowActive && _input.WasSelectPressed() && !_input.IsPointerOverUI())
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
            if (_overlayRoot != null)
                _overlayRoot.SetActive(_state.Active);

            if (_state.Active)
            {
                if (_ui != null)
                    _ui.SetStatus("Map Editor active. F7 to close.");
                Debug.Log("[MapEditor] Activated (F7).");
            }
            else
            {
                _isPanning = false;
                Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
                CancelAddZoneFlow();
                if (_ui != null)
                    _ui.SetStatus("Map Editor inactive.");
                Debug.Log("[MapEditor] Deactivated (F7).");
            }
        }

        private void HandleCameraPan()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            var camSetup = Valkur.Gameplay.CameraSetup.Instance;
            if (camSetup == null) return;

            if (mouse.middleButton.wasPressedThisFrame)
            {
                camSetup.DetachFollow();
                Transform anchorT = camSetup.GetDetachedTransform();
                if (anchorT != null)
                {
                    _isPanning        = true;
                    _panAnchorScreenPos = mouse.position.ReadValue();
                    _panAnchorCamPos    = anchorT.position;
                }
            }
            else if (mouse.middleButton.wasReleasedThisFrame)
            {
                _isPanning = false;
            }

            if (_isPanning && mouse.middleButton.isPressed)
            {
                Transform vcamT = camSetup.GetDetachedTransform();
                if (vcamT == null) return;

                Vector2 currentScreenPos = mouse.position.ReadValue();
                Vector2 screenDelta      = currentScreenPos - _panAnchorScreenPos;
                float unitsPerPixel      = _mainCamera.orthographicSize * 2f / Screen.height;
                Vector3 worldDelta       = new Vector3(screenDelta.x, screenDelta.y, 0f) * unitsPerPixel;
                Vector3 newPos           = _panAnchorCamPos - worldDelta;
                newPos.z         = vcamT.position.z;
                vcamT.position   = newPos;
            }
        }

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
