using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Runtime in-game Lighting Editor (Ctrl+F3) — full UI/UX redesign that mirrors
    /// the menu-bar + draggable-panel architecture of the Tile (F8), Buildings (F10)
    /// and Items (F7) editors. Lets a designer:
    ///
    ///  • Watch the live day/night cycle and scrub time freely.
    ///  • Pause / resume the cycle, change its real-time length, edit the global
    ///    minimum-intensity floor, and configure the Python-parity "lights off
    ///    while it is bright outside" window.
    ///  • Toggle the Global Light 2D and the entire point-light system on/off.
    ///  • Pick a preset (Torch / Lamp / Magic …) from the catalog grid and drop
    ///    fresh point lights anywhere on the map.
    ///  • Drag-select existing lights to move them, click to focus, and delete
    ///    them either from the world or from the instances list.
    ///  • Save the resulting placement back to <c>StreamingAssets/Lights/light_instances.json</c>.
    ///  • Undo / redo every authoring action (50-frame stack).
    ///
    /// Talks to <see cref="DayNightCycle"/> and <see cref="WorldLightLoader"/> for
    /// every gameplay-side change — no duplicate state.
    /// </summary>
    public partial class LightingRuntimeEditor : SingletonMonoBehaviour<LightingRuntimeEditor>,
        GameEditorManager.IGameEditor, IAllowsPlayerMovement
    {
        // ── State ────────────────────────────────────────────────────────────
        private bool _active;
        private bool _uiBuilt;

        // Cached for FKeyBindingParityTests reflection — never read at runtime
        // (the live binding is resolved on every frame by the stateless API).
        private InputAction _toggleAction;
        private bool        _ownsToggleAction;
        private InputAction _ctrlModifier;
        private bool        _ownsCtrlModifier;

        private enum EditorMode { Select, Spawn, Delete }
        private EditorMode _mode = EditorMode.Select;

        // ── UI ───────────────────────────────────────────────────────────────
        private Canvas       _canvas;
        private GameObject   _root;
        private LightingEditorUIBuilder.UIRefs _ui;
        private GameObject   _tutorial;
        private string       _searchFilter = "";

        // Dropdown open/close state — mirrors BuildingsRuntimeEditor.UI.cs.
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // ── Data sources (resolved lazily) ───────────────────────────────────
        private LightPresetCatalog _catalog;
        private string             _selectedPresetKey;
        private GameObject         _selectedLight;     // active map selection
        private GameObject         _hoveredLight;      // mouse-hovered map light

        // ── Runtime gates ────────────────────────────────────────────────────
        private bool _ambientEnabled    = true;
        private bool _pointLightsEnabled = true;
        private float _cachedDayLightIntensity;       // restored when ambient flips back on

        // ── Drag-to-move ─────────────────────────────────────────────────────
        private bool      _moving;
        private GameObject _movingLight;
        private Vector3   _moveStartWorldPos;

        // ── Cycle UI suppression (skip onChanged events while we sync the UI from the live cycle) ──
        private bool _suppressCycleEvents;
        private float _instancesRefreshNext;
        private const float INSTANCES_REFRESH_INTERVAL = 0.5f;

        // ── Undo (50 ops, mirrors Tile / Items) ──────────────────────────────
        private readonly UndoStack _undo = new UndoStack(50);

        /// <summary>
        /// The <see cref="WorldLightLoader.WorldGeneration"/> the undo history was recorded
        /// against. When they diverge the history names lights that no longer exist under those
        /// ids — see DiscardHistoryIfWorldChanged.
        /// </summary>
        private int _undoWorldGeneration = -1;

        // ── Camera helpers ───────────────────────────────────────────────────
        private Camera _mainCamera;
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // ── IGameEditor ──────────────────────────────────────────────────────
        public string EditorName => "Lighting Editor";
        public bool   IsActive   => _active;

        // ── Lifecycle ────────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            // Cached purely for FKeyBindingParityTests reflection. Live
            // resolution still happens through the stateless EditorHotkeyBindings
            // API in Update so the editor is immune to the zombie-action bug.
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleLighting, out _ownsToggleAction);
            _ctrlModifier = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.CtrlModifier, out _ownsCtrlModifier);
        }

        private void Start()
        {
            // Lazy UI build (mirrors Buildings / Items): nothing is created or
            // visible until the user presses Ctrl+F3 the first time.
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (_ownsCtrlModifier) _ctrlModifier?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            // Ctrl+F3 only — bare F3 belongs to the Spawner Editor.
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleLighting) &&
                EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.CtrlModifier))
            {
                if (GameEditorManager.HasInstance) GameEditorManager.Instance.ToggleExclusive(this);
                else                               ToggleActive();
            }

            if (!_active) return;

            // Middle-mouse pan runs unconditionally so dragging the camera works
            // even during light-drag interactions.
            _cameraPan.Tick();

            HandleKeyboardShortcuts();
            SyncCycleFromLive();
            HandleMapInteraction();
            MaybeRefreshInstances();
        }

        public void Activate()
        {
            if (!_uiBuilt)
            {
                try { BuildUI(); _uiBuilt = true; }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[LightingEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            OpenAllPanels();
            EnsureCatalog();
            RefreshPresetList();
            RefreshPresetProperties();
            RebuildInstancesList();
            ApplyMode();

            _mainCamera = Camera.main;
            CameraSetup.Instance?.DetachFollow();
            if (WorldLightLoader.Instance != null)
                _undoWorldGeneration = WorldLightLoader.Instance.WorldGeneration;

            SetStatus("Lighting Editor active. Ctrl+F3 to close.");
            Debug.Log("[LightingEditor] Activated (Ctrl+F3)");
        }

        public void Deactivate()
        {
            _active = false;
            CancelMove();
            ClearDragLatch();
            if (_root != null) _root.SetActive(false);
            _hoveredLight  = null;
            _selectedLight = null;
            // Drop the history with the session. Every command in it addresses a light by id, and
            // ids are only meaningful against the world that was loaded when they were recorded —
            // reload the world, or switch map slot, and the same ids name different lights. An
            // undo surviving into the next session is not a convenience, it is an edit applied to
            // the wrong map.
            _undo.Clear();
            _cameraPan.Reset();
            CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[LightingEditor] Deactivated (Ctrl+F3)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI build ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("LightingEditorCanvas", 111);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _ui = LightingEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle:        ToggleDropdown,
                onModeSelect:            () => SetMode(EditorMode.Select),
                onModeSpawn:             () => SetMode(EditorMode.Spawn),
                onModeDelete:            () => SetMode(EditorMode.Delete),
                onToggleAmbient:         ToggleAmbient,
                onTogglePointLights:     TogglePointLights,
                onScrubTime:             OnScrubTime,
                onPause:                 ToggleCyclePaused,
                onDayLengthChanged:      OnDayLengthChanged,
                onMinIntensityChanged:   OnMinIntensityChanged,
                onToggleLightsWindow:    ToggleLightsWindow,
                onLightsWindowStart:     OnLightsWindowStart,
                onLightsWindowEnd:       OnLightsWindowEnd,
                onJumpDawn:              () => JumpToTime(0.25f),
                onJumpNoon:              () => JumpToTime(0.50f),
                onJumpDusk:              () => JumpToTime(0.75f),
                onJumpMidnight:          () => JumpToTime(0.00f),
                onSearchChanged:         OnSearchChanged,
                onSave:                  DoSave,
                onUndo:                  DoUndo,
                onRedo:                  DoRedo,
                onToggleTutorial:        ToggleTutorial);

            WireOnClose(_ui.ModesPanelDrag,     "modes");
            WireOnClose(_ui.CyclePanelDrag,     "cycle");
            WireOnClose(_ui.PresetsPanelDrag,   "presets");
            WireOnClose(_ui.InstancesPanelDrag, "instances");

            BuildTutorial();
            RefreshMenuBtnHighlights();
        }

        private void WireOnClose(DraggablePanel drag, string key)
        {
            if (drag == null) return;
            drag.OnClose = () =>
            {
                _openDropdowns.Remove(key);
                RefreshMenuBtnHighlights();
            };
        }

        // ── Tutorial overlay ─────────────────────────────────────────────────

        private void BuildTutorial()
        {
            _tutorial = TutorialOverlay.Build(_root.transform, "LIGHTING HOTKEYS", new[]
            {
                ("Ctrl+F3",  "Toggle Lighting Editor"),
                ("LMB click","Select / spawn / delete (per mode)"),
                ("LMB drag", "Move a hovered light"),
                ("MMB drag", "Pan the camera"),
                ("WASD",     "Move the player"),
                ("Type",     "Filter presets"),
                ("Ctrl+S",   "Save light_instances.json"),
                ("Ctrl+Z",   "Undo"),
                ("Ctrl+Y",   "Redo"),
                ("Esc",      "Cancel move / close editor"),
            });
            _tutorial.SetActive(false);
        }

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            _tutorial.SetActive(!_tutorial.activeSelf);
        }

        // ── Dropdown management (mirrors Items / Buildings) ──────────────────

        private void ToggleDropdown(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_openDropdowns.Contains(name))
            {
                SetDropdownOpen(name, false);
                _openDropdowns.Remove(name);
            }
            else
            {
                SetDropdownOpen(name, true);
                _openDropdowns.Add(name);
            }
            RefreshMenuBtnHighlights();
        }

        private void OpenAllPanels()
        {
            foreach (var n in new[] { "modes", "cycle", "presets", "instances" })
            {
                SetDropdownOpen(n, true);
                _openDropdowns.Add(n);
            }
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            var go = name switch
            {
                "modes"     => _ui.ModesDropdown,
                "cycle"     => _ui.CycleDropdown,
                "presets"   => _ui.PresetsDropdown,
                "instances" => _ui.InstancesDropdown,
                _           => null
            };
            if (go != null) go.SetActive(open);
        }

        private void RefreshMenuBtnHighlights()
        {
            LightingEditorUIBuilder.ApplyMenuBtnStyle(_ui.ModesMenuBtnImg,     _ui.ModesMenuBtnTmp,     _openDropdowns.Contains("modes"));
            LightingEditorUIBuilder.ApplyMenuBtnStyle(_ui.CycleMenuBtnImg,     _ui.CycleMenuBtnTmp,     _openDropdowns.Contains("cycle"));
            LightingEditorUIBuilder.ApplyMenuBtnStyle(_ui.PresetsMenuBtnImg,   _ui.PresetsMenuBtnTmp,   _openDropdowns.Contains("presets"));
            LightingEditorUIBuilder.ApplyMenuBtnStyle(_ui.InstancesMenuBtnImg, _ui.InstancesMenuBtnTmp, _openDropdowns.Contains("instances"));
        }

        // ── Status helpers ───────────────────────────────────────────────────

        private void SetStatus(string msg)
        {
            if (_ui.StatusText != null) _ui.StatusText.text = msg;
        }

        private void Toast(string msg)
        {
            SetStatus(msg);
            Debug.Log($"[LightingEditor] {msg}");
        }

        // ── Keyboard shortcuts ───────────────────────────────────────────────

        private void HandleKeyboardShortcuts()
        {
            bool ctrl = KeyboardInputManager.IsCtrlHeld();
            // Not while a drag is in flight. The drag writes the light's position every frame, so
            // an undo applied mid-drag is overwritten before it is ever seen — and the CommitMove
            // that follows clears the redo branch, leaving that history step unreachable in both
            // directions. Finish or cancel the drag first.
            if (ctrl && _moving && (KeyboardInputManager.WasKeyPressedThisFrame(Key.Z, KeyCode.Z) ||
                                    KeyboardInputManager.WasKeyPressedThisFrame(Key.Y, KeyCode.Y)))
            {
                SetStatus("Finish the drag (release LMB) or cancel it (Esc) before undoing.");
                return;
            }
            if (ctrl && KeyboardInputManager.WasKeyPressedThisFrame(Key.Z, KeyCode.Z)) DoUndo();
            if (ctrl && KeyboardInputManager.WasKeyPressedThisFrame(Key.Y, KeyCode.Y)) DoRedo();
            if (ctrl && KeyboardInputManager.WasKeyPressedThisFrame(Key.S, KeyCode.S)) DoSave();

            if (KeyboardInputManager.WasEscapePressedThisFrame())
            {
                if (_moving)                                                CancelMove();
                else if (_tutorial != null && _tutorial.activeSelf) _tutorial.SetActive(false);
                else                                                        Deactivate();
            }
        }
    }
}
