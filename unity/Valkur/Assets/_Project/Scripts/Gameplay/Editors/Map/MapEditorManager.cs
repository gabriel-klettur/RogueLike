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
    /// Toggle with F11 to manage zones and define editable areas consumed by TileEditor.
    /// </summary>
    public partial class MapEditorManager : SingletonMonoBehaviour<MapEditorManager>, GameEditorManager.IGameEditor
    {
        /// <summary>
        /// Public bridge to <see cref="MapEditorMapSlots.ResetActiveSlotToDefaultOnDisk"/>
        /// so callers in other assemblies (e.g. <c>MainMenuUI.StartNewGame</c>)
        /// can reset the persistent <c>_active.txt</c> to the default slot
        /// before the gameplay scene boots. The slot store class itself is
        /// internal to keep the file-IO contract from leaking outside the
        /// Map Editor.
        /// </summary>
        public static void ResetActiveSlotToDefaultOnDisk()
            => MapEditorMapSlots.ResetActiveSlotToDefaultOnDisk();

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
        // Mouse-wheel zoom — shared controller used by every runtime editor.
        private readonly EditorCameraZoomController _cameraZoom = new EditorCameraZoomController();
        // Double-click detector — frames the clicked zone on screen.
        private readonly EditorDoubleClickDetector _doubleClick = new EditorDoubleClickDetector();

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
                Debug.LogWarning("[MapEditor] ZoneManager not found. Created runtime ZoneManager so F11 map editor can start.");
            }

            CreateOverlayRoot();
            CreateUI();

            // Wrap the entire boot-time zone hydration in the boot-sync flag
            // so any PersistZonesToDisk that fires during it (LoadZonesFromDisk
            // does this when it has to clean up intra-file duplicates) is
            // prevented from mirroring the half-loaded state into the active
            // slot's file — that mirror was the canonical regression that
            // ate slot data on every launch with a custom slot active.
            _isBootSyncInProgress = true;
            try
            {
                LoadZonesFromDisk();
                // If the user closed the game with a custom slot active, the
                // working-copy + DB merge above leaves the scene mixing default
                // DB zones with the previous slot's persisted zones. Sync from
                // the active slot's file directly so the boot scene matches the
                // map the user actually chose. No-op when the active slot is
                // the implicit "default" — that path uses DB zones authoritatively.
                BootSyncWithActiveSlotIfNeeded();
            }
            finally { _isBootSyncInProgress = false; }
            HandleZonesChanged();

            zoneManager.OnZonesChanged += HandleZonesChanged;
            ApplyTileEditorConstraint();

            // Backup scheduler: spawn the child component and route every
            // zone-change event into MarkDirty so the idle-timer + quit-hook
            // logic has something to react to. The scheduler is the missing
            // safety net for the "many small edits, no destructive event"
            // case that no existing trigger covers.
            EnsureBackupScheduler();
            zoneManager.OnZonesChanged += HandleZonesChangedForBackup;

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
            _cameraZoom.Tick();

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

            // Same gesture for portal placement: arm via toolbar, click-to-mark
            // on the next frame (the same-frame guard mirrors AddZone so the
            // click that activated the button can't race ahead and place the
            // portal under the toolbar itself).
            if (_isPlacePortalActive && _input.WasSelectPressed() && !_input.IsPointerOverUI()
                && Time.frameCount != _placePortalFlowStartedFrame)
            {
                MarkPortalSourceAtCursor();
                return;
            }

            // Stamp placement: same arm-then-click pattern as AddZone / PlacePortal.
            // The ignore-same-frame guard prevents the click that armed the flow
            // (over the Stamp panel's "Place" button) from immediately stamping.
            if (_isStampFlowActive && _input.WasSelectPressed() && !_input.IsPointerOverUI()
                && Time.frameCount != _stampFlowStartedFrame)
            {
                HandleStampClickAtCursor();
                return;
            }

            if (_input.WasSelectPressed() && !_input.IsPointerOverUI())
                SelectZoneAtCursor();

            if (_doubleClick.PollLeftDouble() && !_input.IsPointerOverUI())
                FrameZoneAtCursor();

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

            // Ctrl+Z / Ctrl+Y for undo/redo. IsTypingInput already returned
            // above, so these only fire when the focus is on the world view.
            if (_input.WasUndoPressed()) PerformUndo();
            if (_input.WasRedoPressed()) PerformRedo();
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
                    _ui.SetStatus("Map Editor active. F11 to close.");
                Debug.Log("[MapEditor] Activated (F11).");
            }
            else
            {
                _cameraPan.Reset();
                _doubleClick.Reset();
                Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
                CancelAddZoneFlow();
                if (_ui != null)
                    _ui.SetStatus("Map Editor inactive.");
                Debug.Log("[MapEditor] Deactivated (F11).");
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

            var slotCallbacks = new MapEditorUIBuilder.MapSlotCallbacks
            {
                OnLoad              = OnSlotLoad,
                OnDelete            = OnSlotDelete,
                OnRename            = OnSlotRename,
                OnNew               = OnSlotNew,
                ListSlots           = ListMapSlots,
                GetActive           = () => ActiveMapSlot,
                OnOpenBackupBrowser = OnOpenBackupBrowserFromF11,
                OnCreateBackupNow   = OnCreateBackupNowFromF11,
            };

            var portalCallbacks = new MapEditorUIBuilder.PortalCallbacks
            {
                OnBeginPlace   = OnBeginPlacePortalFromUI,
                OnCancelPlace  = OnCancelPlacePortalFromUI,
                OnConfirmPlace = OnConfirmPlacePortalFromUI,
            };

            var stampCallbacks = new MapEditorUIBuilder.StampCallbacks
            {
                DiscoverStamps = DiscoverStampManifests,
                OnPlaceStamp   = BeginStampFlow,
                OnCancelStamp  = CancelStampFlow,
            };

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
                SetRestrictTileEditing,
                OnConfirmGenerateBiomes,
                slotCallbacks,
                portalCallbacks,
                stampCallbacks);
            _ui.SetVisible(false);
            _ui.SetRestrictToggle(_state.RestrictTileEditingToEditableZones);

            // Subscribe AFTER Initialize so the UI's _refs are populated.
            OnMapSlotsChanged += RefreshMapsListInUI;
            RefreshMapsListInUI();
        }

        private void RefreshMapsListInUI()
        {
            if (_ui == null) return;
            _ui.RefreshMapsList(ListMapSlots(), ActiveMapSlot);
        }

        // ── Slot UI handlers ────────────────────────────────────────────────────

        private void OnSlotLoad(string slotName)
        {
            if (string.IsNullOrWhiteSpace(slotName))
            {
                _ui?.SetStatus("Pick a map from the list first.");
                return;
            }
            // Run the load through a coroutine so the loading overlay has at
            // least one frame to render before the synchronous LoadMapSlot
            // call begins clearing/respawning content.
            StartCoroutine(LoadSlotWithOverlay(slotName));
        }

        private System.Collections.IEnumerator LoadSlotWithOverlay(string slotName)
        {
            // The overlay's progress bar is rendered each frame from
            // MapEditorUI.Update; the staged progress + status reports below
            // give the lerp something concrete to ease toward, so the user
            // sees a continuously moving bar instead of a step jump from
            // 0 % to 100 % at the end. The actual LoadMapSlot call is still
            // synchronous — yields between checkpoints exist purely to give
            // the renderer a chance to draw frames between phase reports.
            _ui?.ShowMapsLoadingOverlay(slotName);
            _ui?.ReportMapsLoadingProgress(0.05f, "Preparing");
            yield return null;
            yield return null; // overlay + bar reach the screen
            _ui?.ReportMapsLoadingProgress(0.20f, "Reading slot data");
            yield return new WaitForSecondsRealtime(0.05f);
            _ui?.ReportMapsLoadingProgress(0.40f, "Switching active world");
            yield return new WaitForSecondsRealtime(0.05f);

            bool ok = LoadMapSlot(slotName);

            _ui?.ReportMapsLoadingProgress(0.85f, "Finalising scene");
            yield return new WaitForSecondsRealtime(0.05f);
            _ui?.ReportMapsLoadingProgress(1.00f, ok ? "Done" : "Failed");
            // Hold at 100 % briefly so the user sees the bar fill instead of
            // the overlay disappearing at the same instant the lerp completes.
            yield return new WaitForSecondsRealtime(0.30f);
            _ui?.HideMapsLoadingOverlay();
            _ui?.SetStatus(ok ? $"Loaded map '{slotName}'." : $"Load failed for '{slotName}'.");
        }

        private void OnSlotDelete(string slotName)
        {
            if (IsDefaultSlot(slotName))
            {
                _ui?.SetStatus("Cannot delete the 'default' map (it's the implicit baseline).");
                return;
            }
            bool wasActive = string.Equals(slotName, ActiveMapSlot,
                StringComparison.OrdinalIgnoreCase);
            bool ok = DeleteMapSlot(slotName);
            if (!ok)
            {
                _ui?.SetStatus($"Delete failed for '{slotName}'.");
                return;
            }
            // Deleting the slot the user is standing on would leave them
            // stranded inside ZoneManager state for a slot that no longer
            // has a backing file. Send them home to 'default' (also via the
            // loading overlay so the swap feels intentional).
            if (wasActive)
            {
                StartCoroutine(LoadSlotWithOverlay(MapEditorMapSlots.DEFAULT_SLOT));
                _ui?.SetStatus($"Deleted '{slotName}' — switched to 'default'.");
            }
            else
            {
                _ui?.SetStatus($"Deleted map '{slotName}'.");
            }
        }

        private void OnSlotRename(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
            {
                _ui?.SetStatus("Pick a map and type the new name.");
                return;
            }
            if (IsDefaultSlot(oldName))
            {
                _ui?.SetStatus("Cannot rename the 'default' map (it's the implicit baseline).");
                return;
            }
            if (IsDefaultSlot(newName))
            {
                _ui?.SetStatus("Cannot rename a map to 'default' (reserved name).");
                return;
            }
            bool ok = RenameMapSlot(oldName, newName);
            _ui?.SetStatus(ok ? $"Renamed '{oldName}' → '{newName}'." : $"Rename failed.");
        }

        private static bool IsDefaultSlot(string name)
        {
            string clean = MapEditorMapSlots.Sanitize(name);
            return string.Equals(clean, MapEditorMapSlots.DEFAULT_SLOT,
                                 StringComparison.OrdinalIgnoreCase);
        }

        private void OnSlotNew(string slotName)
        {
            string clean = string.IsNullOrWhiteSpace(slotName)
                ? MapEditorMapSlots.DEFAULT_SLOT : slotName;
            bool ok = BeginNewMap(clean);
            _ui?.SetStatus(ok ? $"New blank map '{clean}'." : "New map failed.");
        }

        private void OnConfirmGenerateBiomes(MapEditorUIBuilder.BiomeDialogResult result)
        {
            var req = new BiomeGenerationRequest
            {
                biome             = result.biome,
                randomPerZone     = result.randomPerZone,
                selectedZoneOnly  = result.selectedZoneOnly,
                selectedZoneName  = _state != null ? _state.SelectedZone : null,
                seed              = result.seed,
            };
            string status = GenerateBiomes(req);
            _ui?.SetStatus(status);
            Debug.Log($"[MapEditor] {status}");
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
            OnMapSlotsChanged -= RefreshMapsListInUI;

            if (zoneManager != null)
            {
                zoneManager.OnZonesChanged -= HandleZonesChanged;
                zoneManager.OnZonesChanged -= HandleZonesChangedForBackup;
            }

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
