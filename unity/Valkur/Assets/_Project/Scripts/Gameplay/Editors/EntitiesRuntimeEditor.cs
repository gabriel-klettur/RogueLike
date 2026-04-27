using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Runtime in-game Entities Editor (F5).
    ///
    /// UI/UX layer mirrors the professional menu-bar + draggable-panel
    /// architecture used by the Buildings (F10), FSM (F12) and Tile (F8)
    /// editors. The Python source of truth is <c>roguelike_editors/entities</c>
    /// (panels: tool_bar, picker, add_remove, properties, tutorial).
    ///
    /// This first migration phase wires the full UI shell. Functional
    /// integrations (real spawn/save/load) are stubbed and surface their
    /// status through the picker status label so feel/parity work can begin
    /// without blocking on the data layer.
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Monster catalog asset (drives Hostiles / Neutrals / Specials picker)")]
        private MonsterCatalog _monsterCatalog;

        // ── State ────────────────────────────────────────────────────────────────

        private bool _active;
        private InputAction _toggleAction;

        private enum EditorMode { Select, Spawn, Delete, AddOnSystem }
        private EditorMode _mode = EditorMode.Select;
        private string     _selectedKey;
#pragma warning disable CS0414 // assigned-but-not-yet-read; reserved for Phase 2 player/monster property dispatch
        private bool       _selectedIsPlayer;
#pragma warning restore CS0414

        private enum EntityCategory { Hostiles, Neutrals, Specials, Players }
        private EntityCategory _category = EntityCategory.Hostiles;

        private string _searchFilter = "";
        private readonly UndoStack _undo = new UndoStack(64);

        // ── UI ───────────────────────────────────────────────────────────────────

        private Canvas        _canvas;
        private GameObject    _root;
        private GameObject    _tutorial;
        private EntitiesEditorUIBuilder.UIRefs _ui;

        // Open-dropdown tracking (mirrors BuildingsRuntimeEditor.UI pattern).
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // ── IGameEditor ─────────────────────────────────────────────────────────

        public string EditorName => "Entities Editor";
        public bool   IsActive   => _active;

        // ── Lifecycle ───────────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            // F5 binding — DO NOT rename _toggleAction. FKeyBindingParityTests
            // reflects this field to verify the bound key.
            _toggleAction = new InputAction("ToggleEntitiesEditor", InputActionType.Button, "<Keyboard>/f5");
            _toggleAction.Enable();
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (_toggleAction.WasPerformedThisFrame())
            {
                if (GameEditorManager.HasInstance) GameEditorManager.Instance.ToggleExclusive(this);
                else                               ToggleActive();
            }

            if (!_active) return;
            UpdatePickerDrag();
            // Suppress click-spawn while a drag is active so releasing over the
            // map only triggers the drag-spawn path (HandleMapInteraction would
            // otherwise fire Spawn/Delete on the same release frame).
            if (_pickerDragging) return;
            HandleMapInteraction();
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            OpenDefaultDropdowns();
            RefreshCategoryTabs();
            RefreshPicker();
            RefreshModeButtons();
            SetStatus("Entities Editor active. F5 to close.");
            Debug.Log("[EntitiesEditor] Activated (F5)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedKey = null;
            CancelPickerDrag();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[EntitiesEditor] Deactivated (F5)");
        }

        private void ToggleActive() { if (_active) Deactivate(); else Activate(); }

        // ── UI Construction ─────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("EntitiesEditorCanvas", 106);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _ui = EntitiesEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle: ToggleDropdown,
                onUndo:           () => { _undo.Undo();  SetStatus("Undo"); },
                onRedo:           () => { _undo.Redo();  SetStatus("Redo"); },
                onSave:           () => SetStatus("Save: not yet wired (UI-only phase)"),
                onReload:         () => { RefreshPicker(); SetStatus("Reload: catalog refreshed"); },
                onCatHostiles:    () => SelectCategory(EntityCategory.Hostiles),
                onCatNeutrals:    () => SelectCategory(EntityCategory.Neutrals),
                onCatSpecials:    () => SelectCategory(EntityCategory.Specials),
                onCatPlayers:     () => SelectCategory(EntityCategory.Players),
                onSearchChanged:  v => { _searchFilter = v ?? ""; RefreshPicker(); },
                onAdd:            () => SetMode(EditorMode.Spawn),
                onRemove:         () => SetMode(EditorMode.Delete),
                onAddOnSystem:    () => SetMode(EditorMode.AddOnSystem),
                onConfirm:        OnConfirmAddOnSystem,
                onToggleTutorial: ToggleTutorial);

            // Tutorial overlay (F5-aware hotkey list)
            _tutorial = TutorialOverlay.Build(_root.transform, "ENTITIES HOTKEYS", new[]
            {
                ("F5",     "Toggle Entities Editor"),
                ("Click",  "Select / Spawn / Delete on map"),
                ("Drag",   "Drag picker slot → map to spawn"),
                ("Type",   "Filter picker by name"),
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("MMB",    "Pan camera (drag)"),
                ("Esc",    "Close all editors"),
            });
            _tutorial.SetActive(false);
        }

        // ── Dropdown management ────────────────────────────────────────────────

        private void OpenDefaultDropdowns()
        {
            _openDropdowns.Clear();
            // Open the working set on activation, matching Python entities_editor:
            // tools, categories+picker (browsing), add/remove (mode switching), props.
            SetDropdownOpen("tools",      true);
            SetDropdownOpen("categories", true);
            SetDropdownOpen("picker",     true);
            SetDropdownOpen("addremove",  true);
            SetDropdownOpen("props",      true);
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
            "tools"      => _ui.ToolsDropdown,
            "categories" => _ui.CategoriesDropdown,
            "picker"     => _ui.PickerDropdown,
            "addremove"  => _ui.AddRemoveDropdown,
            "props"      => _ui.PropsDropdown,
            _            => null
        };

        private void RefreshMenuBtnHighlights()
        {
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.ToolsMenuBtnImg,      _ui.ToolsMenuBtnTmp,      _openDropdowns.Contains("tools"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.CategoriesMenuBtnImg, _ui.CategoriesMenuBtnTmp, _openDropdowns.Contains("categories"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.PickerMenuBtnImg,     _ui.PickerMenuBtnTmp,     _openDropdowns.Contains("picker"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.AddRemoveMenuBtnImg,  _ui.AddRemoveMenuBtnTmp,  _openDropdowns.Contains("addremove"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.PropsMenuBtnImg,      _ui.PropsMenuBtnTmp,      _openDropdowns.Contains("props"));
        }

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            _tutorial.SetActive(!_tutorial.activeSelf);
        }

        // ── Status helper ──────────────────────────────────────────────────────

        private void SetStatus(string msg)
        {
            if (_ui.StatusText != null) _ui.StatusText.text = msg;
        }

        // ── Confirm (Add-On-System stub) ───────────────────────────────────────

        private void OnConfirmAddOnSystem()
        {
            if (_mode != EditorMode.AddOnSystem)
            {
                SetStatus("Confirm: switch to Add-On-System mode first.");
                return;
            }
            SetStatus("Confirm: persistence not wired (UI-only phase).");
        }
    }
}
