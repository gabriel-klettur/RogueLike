using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Runtime in-game Inventory Editor (F6).
    ///
    /// UI/UX mirrors the Unity Items / Buildings / Tile editors:
    ///   • 30 px menu bar at top (brand + Modes/Entities/Slots/Items dropdowns + ? + PERF)
    ///   • Floating, draggable panels for each section.
    ///
    /// Content mirrors Python's inventory_editor (F6):
    ///   • Title + Toolbar (Show Default / Show Active / Save / Add Item / Delete Item)
    ///   • Left panel: category tabs (Player / Monsters / Map) + side tabs (Default / Active)
    ///                 + search + scrollable entity list
    ///   • Right panel: inventory grid (5 cols of slots) + entity owner header
    ///   • Item Selection panel: Default / Ground tabs + search + grid catalog
    ///                           + quantity input + "Add to Inventory" button
    ///   • Tutorial overlay
    ///
    /// PHASE 1: UI/UX scaffolding only.
    ///   • Mode/category/side toggles update visual state and status toast.
    ///   • Entity list, slot grid and item catalog are populated from runtime
    ///     data when available, but mutations (Add/Delete/Save/Drag) are
    ///     placeholders. Phase 2 will wire the data layer (defaults JSON,
    ///     active JSON, ECS sync, drag &amp; drop).
    /// </summary>
    public partial class InventoryRuntimeEditor
        : SingletonMonoBehaviour<InventoryRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private bool _active;
        private bool _uiBuilt;
        private InputAction _toggleAction;
        private bool _ownsToggleAction;

        // ── State ────────────────────────────────────────────────────────────────

        private enum EditorCategory { Player, Monsters, Map }
        private enum EditorSide     { Default, Active }
        private enum EditorMode     { View, AddItem, DeleteItem }
        private enum CatalogTab     { Default, Ground }

        private EditorCategory _category = EditorCategory.Player;
        private EditorSide     _side     = EditorSide.Active;
        private EditorMode     _mode     = EditorMode.View;
        private CatalogTab     _catalog  = CatalogTab.Default;

        private Inventory _selectedInventory;
        private string _selectedEntityName;

        private string _entitySearch  = "";
        private string _catalogSearch = "";
        private int    _spinnerQty    = 1;

        // ── Root UI ─────────────────────────────────────────────────────────────

        private Canvas _canvas;
        private GameObject _root;

        // UI builder refs
        private InventoryEditorUIBuilder.UIRefs _uiRefs;

        // Convenience aliases populated from _uiRefs after BuildUI()
        private RectTransform _entityListContent;
        private RectTransform _slotGridContent;
        private RectTransform _catalogGridContent;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _ownerTmp;
        private TMP_InputField  _entitySearchBox;
        private TMP_InputField  _catalogSearchBox;
        private TMP_InputField  _qtyInput;

        private Image _viewBtnImg;
        private Image _addItemBtnImg;
        private Image _deleteItemBtnImg;
        private Image _playerTabImg;
        private Image _monstersTabImg;
        private Image _mapTabImg;
        private Image _sideDefaultImg;
        private Image _sideActiveImg;
        private Image _catDefaultImg;
        private Image _catGroundImg;

        // Catalog data
        private ItemDefinition[] _allItems;

        // EditorKit extras
        private GameObject _tutorial;
        private readonly UndoStack _undo = new UndoStack(64);

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // Dropdown state — mirrors BuildingsRuntimeEditor.UI.cs
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // ── IGameEditor ──────────────────────────────────────────────────────────

        public string EditorName => "Inventory Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleInventory, out _ownsToggleAction);
        }

        private void Start()
        {
            // Lazy UI build (mirrors ItemsRuntimeEditor / BuildingsRuntimeEditor):
            // nothing is created until the user presses F6 the first time. This
            // avoids the menu bar being briefly drawn at scene start.
            _active = false;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleInventory))
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }

            // Middle-mouse camera pan — same UX as every other runtime editor.
            if (_active) _cameraPan.Tick();
        }

        public void Activate()
        {
            if (!_uiBuilt)
            {
                try
                {
                    // Resources/Items only. The former empty-path fallback walked all
                    // ~7 400 assets under Resources/ and logged a console error for
                    // every one whose script no longer resolved.
                    _allItems = Resources.LoadAll<ItemDefinition>("Items");
                    BuildUI();
                    _uiBuilt = true;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[InventoryEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }
            _active = true;
            _root.SetActive(true);
            _category = EditorCategory.Player;
            _side     = EditorSide.Active;
            _mode     = EditorMode.View;
            _catalog  = CatalogTab.Default;
            OpenAllPanels();
            RefreshAll();
            Toast("Inventory Editor active. F6 to close.");
            Debug.Log("[InventoryEditor] Activated (F6)");
        }

        public void Deactivate()
        {
            _active = false;
            if (_root != null) _root.SetActive(false);
            _selectedInventory  = null;
            _selectedEntityName = null;
            // Reattach the camera follow target if MMB pan had detached it.
            _cameraPan.Reset();
            Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[InventoryEditor] Deactivated (F6)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI Construction ─────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("InventoryEditorCanvas", 107);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _uiRefs = InventoryEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle: ToggleDropdown,
                onUndo:           () => { _undo.Undo(); Toast("Undo"); },
                onRedo:           () => { _undo.Redo(); Toast("Redo"); },
                onSave:           () => Toast("Save \u2014 Phase 2"),
                onShowDefault:    () => SetSide(EditorSide.Default),
                onShowActive:     () => SetSide(EditorSide.Active),
                onModeView:       () => SetMode(EditorMode.View),
                onModeAddItem:    () => SetMode(EditorMode.AddItem),
                onModeDeleteItem: () => SetMode(EditorMode.DeleteItem),
                onCatPlayer:      () => SetCategory(EditorCategory.Player),
                onCatMonsters:    () => SetCategory(EditorCategory.Monsters),
                onCatMap:         () => SetCategory(EditorCategory.Map),
                onEntitySearch:   v => { _entitySearch  = v ?? ""; RefreshEntityList(); },
                onCatalogSearch:  v => { _catalogSearch = v ?? ""; RefreshCatalog(); },
                onCatalogTabDefault: () => SetCatalogTab(CatalogTab.Default),
                onCatalogTabGround:  () => SetCatalogTab(CatalogTab.Ground),
                onQtyMinus:       () => AdjustQty(-1),
                onQtyPlus:        () => AdjustQty(+1),
                onAddToInventory: () => Toast("Add to Inventory \u2014 Phase 2"),
                onToggleTutorial: ToggleTutorial,
                onPerfToggle:     null);

            // Sync dropdown highlights when a panel is closed via its X
            if (_uiRefs.ModesPanelDrag    != null)
                _uiRefs.ModesPanelDrag.OnClose    = () => { _openDropdowns.Remove("modes");    RefreshMenuBtnHighlights(); };
            if (_uiRefs.EntitiesPanelDrag != null)
                _uiRefs.EntitiesPanelDrag.OnClose = () => { _openDropdowns.Remove("entities"); RefreshMenuBtnHighlights(); };
            if (_uiRefs.SlotsPanelDrag    != null)
                _uiRefs.SlotsPanelDrag.OnClose    = () => { _openDropdowns.Remove("slots");    RefreshMenuBtnHighlights(); };
            if (_uiRefs.CatalogPanelDrag  != null)
                _uiRefs.CatalogPanelDrag.OnClose  = () => { _openDropdowns.Remove("catalog");  RefreshMenuBtnHighlights(); };

            // Map UIBuilder refs to private fields
            _entityListContent  = _uiRefs.EntityListContent;
            _slotGridContent    = _uiRefs.SlotGridContent;
            _catalogGridContent = _uiRefs.CatalogGridContent;
            _statusTmp          = _uiRefs.StatusText;
            _ownerTmp           = _uiRefs.OwnerText;
            _entitySearchBox    = _uiRefs.EntitySearchBox;
            _catalogSearchBox   = _uiRefs.CatalogSearchBox;
            _qtyInput           = _uiRefs.QtyInput;

            _viewBtnImg         = _uiRefs.ViewBtnImg;
            _addItemBtnImg      = _uiRefs.AddItemBtnImg;
            _deleteItemBtnImg   = _uiRefs.DeleteItemBtnImg;

            _playerTabImg       = _uiRefs.PlayerTabImg;
            _monstersTabImg     = _uiRefs.MonstersTabImg;
            _mapTabImg          = _uiRefs.MapTabImg;
            _sideDefaultImg     = _uiRefs.SideDefaultImg;
            _sideActiveImg      = _uiRefs.SideActiveImg;
            _catDefaultImg      = _uiRefs.CatDefaultImg;
            _catGroundImg       = _uiRefs.CatGroundImg;

            BuildTutorial();
        }

        // ── Dropdown management (mirrors BuildingsRuntimeEditor.UI.cs) ──────────

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
            foreach (var n in new[] { "modes", "entities", "slots", "catalog" })
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
                "modes"    => _uiRefs.ModesDropdown,
                "entities" => _uiRefs.EntitiesDropdown,
                "slots"    => _uiRefs.SlotsDropdown,
                "catalog"  => _uiRefs.CatalogDropdown,
                _          => null
            };
            go?.SetActive(open);
        }

        private void RefreshMenuBtnHighlights()
        {
            InventoryEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.ModesMenuBtnImg,    _uiRefs.ModesMenuBtnTmp,    _openDropdowns.Contains("modes"));
            InventoryEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.EntitiesMenuBtnImg, _uiRefs.EntitiesMenuBtnTmp, _openDropdowns.Contains("entities"));
            InventoryEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.SlotsMenuBtnImg,    _uiRefs.SlotsMenuBtnTmp,    _openDropdowns.Contains("slots"));
            InventoryEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.CatalogMenuBtnImg,  _uiRefs.CatalogMenuBtnTmp,  _openDropdowns.Contains("catalog"));
        }

        // ── Tutorial overlay ────────────────────────────────────────────────────

        private void BuildTutorial()
        {
            _tutorial = TutorialOverlay.Build(_root.transform, "INVENTORY HOTKEYS", new[]
            {
                ("F6",     "Toggle Inventory Editor"),
                ("Tabs",   "Switch Player / Monsters / Map"),
                ("D / A",  "Show Default / Show Active"),
                ("Click",  "Select entity / slot / item"),
                ("Type",   "Filter entities or items"),
                ("+ / -",  "Adjust quantity"),
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("Esc",    "Close all editors"),
            });
            _tutorial.SetActive(false);
        }

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            _tutorial.SetActive(!_tutorial.activeSelf);
        }

        // ── Status toast ────────────────────────────────────────────────────────

        private void Toast(string msg)
        {
            if (_statusTmp != null) _statusTmp.text = msg;
            Debug.Log($"[InventoryEditor] {msg}");
        }
    }
}
