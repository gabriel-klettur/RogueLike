using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Runtime in-game Items Editor (F7).
    /// Browse item catalog, view properties, spawn/delete items on map.
    /// Mirrors Python's items_editor (F7): picker grid, properties panel,
    /// spawn/delete modes, map drops list.
    /// </summary>
    public partial class ItemsRuntimeEditor : SingletonMonoBehaviour<ItemsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private bool _active;
        private InputAction _toggleAction;

        private enum EditorMode { Select, Spawn, Delete }
        private EditorMode _mode = EditorMode.Select;
        private string _selectedItemId;
        private ItemDefinition _selectedDef;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _pickerContent;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private Image _spawnBtnImg;
        private Image _deleteBtnImg;
        private Image _selectBtnImg;

        // Items loaded from Resources
        private ItemDefinition[] _allItems;

        // EditorKit extras
        private string _searchFilter = "";
        private TMP_InputField _searchBox;
        private GameObject _tutorial;
        private readonly UndoStack _undo = new UndoStack(64);

        // IGameEditor
        public string EditorName => "Items Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleItemsEditor", InputActionType.Button, "<Keyboard>/f7");
            _toggleAction.Enable();
        }

        private void Start()
        {
            _allItems = Resources.LoadAll<ItemDefinition>("Items");
            if (_allItems == null || _allItems.Length == 0)
                _allItems = Resources.LoadAll<ItemDefinition>("");
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
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }
            if (!_active) return;
            HandleMapInteraction();
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            RefreshPicker();
            RefreshModeButtons();
            _statusTmp.text = "Items Editor active. F7 to close.";
            Debug.Log("[ItemsEditor] Activated (F7)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedItemId = null;
            _selectedDef = null;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[ItemsEditor] Deactivated (F7)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("ItemsEditorCanvas", 108);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            // Left sidebar — Picker
            var left = EditorUIHelpers.MakeSidebar("PickerPanel", _root.transform, 320f);
            EditorUIHelpers.AddVLG(left, 8, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "ITEMS EDITOR");

            // Toolbar
            var toolbar = EditorUIHelpers.CreateUI("Toolbar", left.transform);
            toolbar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            var selectBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Select", () => SetMode(EditorMode.Select), 28f, 11f);
            _selectBtnImg = selectBtn.GetComponent<Image>();
            var spawnBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Spawn", () => SetMode(EditorMode.Spawn), 28f, 11f);
            _spawnBtnImg = spawnBtn.GetComponent<Image>();
            var deleteBtn = EditorUIHelpers.MakeDangerButton(toolbar.transform, "Delete", () => SetMode(EditorMode.Delete), 28f);
            _deleteBtnImg = deleteBtn.GetComponent<Image>();

            var undoBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Undo", () => _undo.Undo(), 28f, 11f);
            var redoBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Redo", () => _undo.Redo(), 28f, 11f);

            EditorUIHelpers.BuildSeparator(left.transform);

            // Search filter
            _searchBox = SearchBox.Create(left.transform, "Search items\u2026",
                v => { _searchFilter = v ?? ""; RefreshPicker(); });

            var (scroll, content) = EditorUIHelpers.MakeGridPicker(left.transform, "ItemGrid", 4, 64f, 4f);
            _pickerContent = content;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);

            // Right sidebar — Properties
            var right = EditorUIHelpers.MakeRightPanel("PropsPanel", _root.transform, 360f);
            EditorUIHelpers.AddVLG(right, 8, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "ITEM PROPERTIES");

            var (pScroll, pContent) = EditorUIHelpers.MakeScrollView(right.transform, "PropsScroll");
            _propsTmp = EditorUIHelpers.AddLabel(pContent, "Select an item to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;

            // Tutorial overlay (hotkey hints), hidden by default.
            _tutorial = TutorialOverlay.Build(_root.transform, "ITEMS HOTKEYS", new[]
            {
                ("F7",     "Toggle Items Editor"),
                ("Click",  "Select / spawn / delete"),
                ("Type",   "Filter by name"),
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("Esc",    "Close all editors"),
            });
            _tutorial.SetActive(false);
        }

        // ── Mode ──
    }
}