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
        : SingletonMonoBehaviour<SpawnerEditorManager>, GameEditorManager.IGameEditor
    {
        [Header("References")]
        [Tooltip("Catalog of spawner templates for the picker grid.")]
        [SerializeField] private SpawnerTemplateCatalog _catalog;

        [Tooltip("Camera used for screen-to-world conversion.")]
        [SerializeField] private Camera _camera;

        // ── Input ────────────────────────────────────────────────────────────────

        private InputAction _toggleAction;
        private InputAction _ctrlModifier;
        private InputAction _clickAction;
        private InputAction _rightClickAction;
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
            RefreshModeButtons();
            RefreshPropertiesPanel();
            SetStatus("Spawner Editor active. F3 to close.");
        }

        public void Deactivate()
        {
            _active = false;
            if (_root != null) _root.SetActive(false);
            _selectedTemplate = null;
            _selectedInstance = null;
            _dragging = false;
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

            _rightClickAction = new InputAction("SpawnerEditorRightClick", InputActionType.Button);
            _rightClickAction.AddBinding("<Mouse>/rightButton");
            _rightClickAction.Enable();

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

            if (_escapeAction != null && _escapeAction.WasPerformedThisFrame())
                CancelCurrentMode();

            HandleMapInteraction();
            UpdateStatus();
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) { _toggleAction?.Disable(); _toggleAction?.Dispose(); }
            if (_ownsCtrlModifier) { _ctrlModifier?.Disable(); _ctrlModifier?.Dispose(); }
            _clickAction?.Disable();      _clickAction?.Dispose();
            _rightClickAction?.Disable(); _rightClickAction?.Dispose();
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
                onUndo:           () => { _undo.Undo(); SetStatus("Undo"); },
                onRedo:           () => { _undo.Redo(); SetStatus("Redo"); },
                onSave:           SaveInstancesToJson,
                onReload:         () => { RefreshPicker(); SetStatus("Reload: catalog refreshed"); },
                onSearchChanged:  v => { _searchFilter = v ?? string.Empty; RefreshPicker(); },
                onModeSelect:     () => SetMode(EditorMode.Select),
                onModePlace:      () => SetMode(EditorMode.Place),
                onModeDelete:     () => SetMode(EditorMode.Delete),
                onToggleTutorial: ToggleTutorial);

            _tutorial = TutorialOverlay.Build(_root.transform, "SPAWNER HOTKEYS", new[]
            {
                ("F3",     "Toggle Spawner Editor"),
                ("LMB",    "Select / Place / Delete (mode-aware)"),
                ("RMB",    "Drag selected spawner on the map"),
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
            SetDropdownOpen("modes",  true);
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
            var go = GetDropdown(name);
            if (go == null) return;
            if (open) _openDropdowns.Add(name);
            else      _openDropdowns.Remove(name);
            go.SetActive(open);
        }

        private GameObject GetDropdown(string name) => name switch
        {
            "tools"  => _ui.ToolsDropdown,
            "picker" => _ui.PickerDropdown,
            "modes"  => _ui.ModesDropdown,
            "props"  => _ui.PropsDropdown,
            _        => null
        };

        private void RefreshMenuBtnHighlights()
        {
            EditorUIHelpers.ApplyMenuBtnStyle(_ui.ToolsMenuBtnImg,  _ui.ToolsMenuBtnTmp,  _openDropdowns.Contains("tools"));
            EditorUIHelpers.ApplyMenuBtnStyle(_ui.PickerMenuBtnImg, _ui.PickerMenuBtnTmp, _openDropdowns.Contains("picker"));
            EditorUIHelpers.ApplyMenuBtnStyle(_ui.ModesMenuBtnImg,  _ui.ModesMenuBtnTmp,  _openDropdowns.Contains("modes"));
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
            RefreshModeButtons();
            UpdateStatus();
        }

        private void RefreshModeButtons()
        {
            if (_ui.SelectBtnImg == null) return;
            _ui.SelectBtnImg.color = _mode == EditorMode.Select ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
            _ui.PlaceBtnImg.color  = _mode == EditorMode.Place  ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
            _ui.DeleteBtnImg.color = _mode == EditorMode.Delete ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
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
            Place,
            Delete
        }
    }
}
