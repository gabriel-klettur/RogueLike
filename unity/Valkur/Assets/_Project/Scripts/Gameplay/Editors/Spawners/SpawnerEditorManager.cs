using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Runtime in-game Spawner Editor (F3).
    ///
    /// UI/UX layer mirrors the professional menu-bar + draggable-panel
    /// architecture used by the Buildings (F10), FSM (F12), Tile (F8) and
    /// Entities (F5) editors. Builds upon the shared <see cref="EditorUIHelpers"/>
    /// primitives so the chrome stays in sync with the live UX-panel theme.
    ///
    /// Python source of truth: <c>roguelike_editors/spawners</c>
    /// (panels: tool_bar, picker, modes, properties, tutorial).
    /// </summary>
    public partial class SpawnerEditorManager
        : SingletonMonoBehaviour<SpawnerEditorManager>, GameEditorManager.IGameEditor, IAllowsPlayerMovement
    {
        [Header("References")]
        [Tooltip("Catalog of spawner templates for the picker grid.")]
        [SerializeField] private SpawnerTemplateCatalog _catalog;

        /// <summary>
        /// Injects the template catalog from the bootstrap. The only assignment used to be
        /// a <c>SerializedObject</c>/<c>FindProperty</c> write inside <c>#if UNITY_EDITOR</c>
        /// while this manager is created in every build — so a shipped player's F3 picker
        /// reported "No catalog assigned." and could place nothing. Twin of
        /// <c>EntitiesRuntimeEditor.SetMonsterCatalog</c>.
        /// </summary>
        internal void SetCatalog(SpawnerTemplateCatalog catalog)
        {
            if (catalog != null) _catalog = catalog;
        }

        /// <summary>
        /// Last-resort lookup for a catalog nobody injected. Deliberately NOT in Awake:
        /// the bootstrap registers the catalog during its own Start, which can run after
        /// this component's Awake.
        /// </summary>
        private void ResolveCatalogFallback()
        {
            if (_catalog != null) return;
            if (ServiceLocator.TryGet<SpawnerTemplateCatalog>(out var catalog) && catalog != null)
                _catalog = catalog;
        }

        [Tooltip("Camera used for screen-to-world conversion.")]
        [SerializeField] private Camera _camera;

        // ── Input ────────────────────────────────────────────────────────────────

        private InputAction _toggleAction;
        private InputAction _ctrlModifier;
        private InputAction _clickAction;
        private InputAction _escapeAction;
        private bool _ownsToggleAction;
        private bool _ownsCtrlModifier;

        // ── State ────────────────────────────────────────────────────────────────

        private bool _active;
        private EditorMode _mode = EditorMode.Select;

        private SpawnerTemplateData _selectedTemplate;
        private SpawnerInstance _selectedInstance;
        private bool _dragging;
        private Vector3 _dragOffset;
        private Vector3 _dragStartWorldPos;
        private string _searchFilter = string.Empty;

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();
        private readonly UndoStack _undo = new UndoStack(64);

        // ── UI ───────────────────────────────────────────────────────────────────

        private Canvas _canvas;
        private GameObject _root;
        private GameObject _tutorial;
        private SpawnerEditorUIBuilder.UIRefs _ui;

        private readonly HashSet<string> _openDropdowns = new HashSet<string>();
        private readonly List<GameObject> _pickerRows = new List<GameObject>();

        // ── IGameEditor ──────────────────────────────────────────────────────────

        public string EditorName => "Spawner Editor";
        public bool IsActive => _active;
        public bool IsVisible => _active;

        public void Activate()
        {
            _active = true;
            if (_root != null) _root.SetActive(true);
            _mode = EditorMode.Select;
            OpenDefaultDropdowns();
            RefreshPicker();
            RefreshPropertiesPanel();
            SetStatus("Spawner Editor active. F3 to close.");
        }

        public void Deactivate()
        {
            // Flush anything the debounce has not written yet. Placing a spawner and closing
            // within the debounce window is the obvious way to lose an edit, and it is exactly
            // what someone does when they place one last spawner and hit F3.
            if (_active) FlushAutosave();

            _active = false;
            if (_root != null) _root.SetActive(false);
            _selectedTemplate = null;
            _selectedInstance = null;
            _dragging = false;
            CancelPickerDrag();
            _showAllOutlines = false;
            HideAllOutlineFx();
            _cameraPan.Reset();
            CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleSpawner, out _ownsToggleAction);
            _ctrlModifier = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.CtrlModifier, out _ownsCtrlModifier);

            _clickAction = new InputAction("SpawnerEditorClick", InputActionType.Button);
            _clickAction.AddBinding("<Mouse>/leftButton");
            _clickAction.Enable();

            // RMB is read directly through MouseInputManager (move-drag pickup +
            // active-drag release) — no dedicated InputAction is required.

            _escapeAction = new InputAction("SpawnerEditorEscape", InputActionType.Button);
            _escapeAction.AddBinding("<Keyboard>/escape");
            _escapeAction.Enable();
        }

        private void Start()
        {
            BuildUI();
            if (_root != null) _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleSpawner) &&
                !EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.CtrlModifier))
            {
                if (GameEditorManager.HasInstance) GameEditorManager.Instance.ToggleExclusive(this);
                else                               ToggleActive();
            }

            if (!_active) return;

            // Middle-mouse pan runs unconditionally so dragging the camera works
            // even while interacting with the picker / properties panels.
            _cameraPan.Tick();

            // Ctrl+S, matching Buildings (F10), Tile (F6) and Lighting (Ctrl+F3). This editor
            // was the only one without it, and without any automatic save either — its single
            // save trigger was the toolbar button, so a session of placing spawners was lost on
            // restart unless the user happened to click it. That read as broken persistence
            // when the persistence itself was fine.
            if (Valkur.Core.Input.KeyboardInputManager.IsCtrlHeld() &&
                Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(
                    UnityEngine.InputSystem.Key.S, KeyCode.S))
                SaveInstancesToJson();

            if (_escapeAction != null && _escapeAction.WasPerformedThisFrame())
                CancelCurrentMode();

            TickAutosave();
            UpdatePickerDrag();
            UpdateOutlineState();
            HandleMapInteraction();
            UpdateStatus();
        }

        /// <summary>
        /// Last chance to persist. Stopping Play Mode with the editor still open is the other
        /// obvious way to lose an edit inside the debounce window, and OnApplicationQuit still
        /// runs while Application.isPlaying is true, so the write guard lets it through.
        /// </summary>
        private void OnApplicationQuit() => FlushAutosave();

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) { _toggleAction?.Disable(); _toggleAction?.Dispose(); }
            if (_ownsCtrlModifier) { _ctrlModifier?.Disable(); _ctrlModifier?.Dispose(); }
            _clickAction?.Disable();      _clickAction?.Dispose();
            _escapeAction?.Disable();     _escapeAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void ToggleActive() { if (_active) Deactivate(); else Activate(); }

        // ── Build root canvas + delegate panel construction to the UI builder ───

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("SpawnerEditorCanvas", 104);
            _canvas.transform.SetParent(transform, worldPositionStays: false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, worldPositionStays: false);
            EditorUIHelpers.StretchFill(_root);

            _ui = SpawnerEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle: ToggleDropdown,
                onUndo:           () => { _undo.Undo(); MarkInstancesDirty(); SetStatus("Undo"); },
                onRedo:           () => { _undo.Redo(); MarkInstancesDirty(); SetStatus("Redo"); },
                onSave:           () => SaveInstancesToJson(),
                onReload:         () => { RefreshPicker(); SetStatus("Reload: catalog refreshed"); },
                onSearchChanged:  v => { _searchFilter = v ?? string.Empty; RefreshPicker(); },
                onDeleteSelected: DeleteSelectedInstance,
                onToggleTutorial: ToggleTutorial);

            _tutorial = TutorialOverlay.Build(_root.transform, "SPAWNER HOTKEYS", new[]
            {
                ("F3",     "Toggle Spawner Editor"),
                ("LMB",    "Select (or place when a template is picked)"),
                ("Drag",   "Drag a template from the picker onto the map to place"),
                ("Alt",    "Toggle on-map spawner outlines (click centre to inspect)"),
                ("RMB",    "Drag any spawner on the map (any mode)"),
                ("Del",    "Properties → Delete spawner (after selection)"),
                ("Type",   "Filter picker by template id"),
                ("Esc",    "Cancel current mode (or close)"),
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("MMB",    "Pan camera (drag)"),
            });
            _tutorial.SetActive(false);
        }

        // ── Dropdown management (mirrors EntitiesRuntimeEditor pattern) ──────────

        private void OpenDefaultDropdowns()
        {
            _openDropdowns.Clear();
            SetDropdownOpen("tools",  true);
            SetDropdownOpen("picker", true);
            SetDropdownOpen("props",  true);
            RefreshMenuBtnHighlights();
        }

        private void ToggleDropdown(string name)
        {
            bool willOpen = !_openDropdowns.Contains(name);
            SetDropdownOpen(name, willOpen);
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            // Update logical state first — _openDropdowns is the source of truth
            // and stays coherent even if the UI isn't fully wired yet (e.g. when
            // OpenDefaultDropdowns runs before BuildUI completes, or in EditMode
            // tests that exercise selection without bringing up the canvas).
            if (open) _openDropdowns.Add(name);
            else      _openDropdowns.Remove(name);

            var go = GetDropdown(name);
            if (go != null) go.SetActive(open);
        }

        private GameObject GetDropdown(string name) => name switch
        {
            "tools"  => _ui.ToolsDropdown,
            "picker" => _ui.PickerDropdown,
            "props"  => _ui.PropsDropdown,
            _        => null
        };

        private void RefreshMenuBtnHighlights()
        {
            EditorUIHelpers.ApplyMenuBtnStyle(_ui.ToolsMenuBtnImg,  _ui.ToolsMenuBtnTmp,  _openDropdowns.Contains("tools"));
            EditorUIHelpers.ApplyMenuBtnStyle(_ui.PickerMenuBtnImg, _ui.PickerMenuBtnTmp, _openDropdowns.Contains("picker"));
            EditorUIHelpers.ApplyMenuBtnStyle(_ui.PropsMenuBtnImg,  _ui.PropsMenuBtnTmp,  _openDropdowns.Contains("props"));
        }

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            _tutorial.SetActive(!_tutorial.activeSelf);
        }

        // ── Mode / status helpers ────────────────────────────────────────────────

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            UpdateStatus();
        }

        private void SetStatus(string msg)
        {
            if (_ui.StatusText != null) _ui.StatusText.text = msg;
        }

        private void UpdateStatus()
        {
            if (_ui.StatusText == null) return;
            string modeStr = _mode.ToString();
            if (_mode == EditorMode.Place && _selectedTemplate != null)
                modeStr += $" ({_selectedTemplate.templateId})";
            _ui.StatusText.text = $"Mode: {modeStr}";
        }

        private enum EditorMode
        {
            Select,
            Place
        }
    }
}
