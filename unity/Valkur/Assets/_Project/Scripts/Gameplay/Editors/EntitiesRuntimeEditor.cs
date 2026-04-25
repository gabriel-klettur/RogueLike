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
    /// Browse, inspect, spawn/delete entities on the map.
    /// Mirrors Python's entities_editor (F5): picker grid, properties panel,
    /// spawn/delete modes. Supports players, hostiles, neutrals.
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Monster catalog asset")]
        private MonsterCatalog _monsterCatalog;

        private bool _active;
        private InputAction _toggleAction;

        private enum EditorMode { Select, Spawn, Delete }
        private EditorMode _mode = EditorMode.Select;
        private string _selectedKey;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _pickerContent;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private Image _spawnBtnImg;
        private Image _deleteBtnImg;
        private Image _selectBtnImg;

        // Category
        private enum EntityCategory { Hostiles, Players }
        private EntityCategory _category = EntityCategory.Hostiles;

        // EditorKit extras
        private string _searchFilter = "";
        private TMP_InputField _searchBox;
        private GameObject _tutorial;
        private readonly UndoStack _undo = new UndoStack(64);

        // IGameEditor
        public string EditorName => "Entities Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
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
            _statusTmp.text = "Entities Editor active. F5 to close.";
            Debug.Log("[EntitiesEditor] Activated (F5)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedKey = null;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[EntitiesEditor] Deactivated (F5)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI Construction ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("EntitiesEditorCanvas", 106);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            // Left sidebar — Picker
            var left = EditorUIHelpers.MakeSidebar("PickerPanel", _root.transform, 320f);
            EditorUIHelpers.AddVLG(left, 8, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "ENTITIES EDITOR");

            // Category tabs
            var tabRow = EditorUIHelpers.CreateUI("TabRow", left.transform);
            tabRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var hlg = tabRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeButton(tabRow.transform, "Hostiles", () =>
            {
                _category = EntityCategory.Hostiles; RefreshPicker();
            }, 26f, 11f);
            EditorUIHelpers.MakeButton(tabRow.transform, "Players", () =>
            {
                _category = EntityCategory.Players; RefreshPicker();
            }, 26f, 11f);

            // Toolbar
            var toolbar = EditorUIHelpers.CreateUI("Toolbar", left.transform);
            toolbar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var toolHlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            toolHlg.spacing = 4f; toolHlg.childForceExpandWidth = true;

            var selectBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Select", () => SetMode(EditorMode.Select), 28f, 11f);
            _selectBtnImg = selectBtn.GetComponent<Image>();
            var spawnBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Spawn", () => SetMode(EditorMode.Spawn), 28f, 11f);
            _spawnBtnImg = spawnBtn.GetComponent<Image>();
            var deleteBtn = EditorUIHelpers.MakeDangerButton(toolbar.transform, "Delete", () => SetMode(EditorMode.Delete), 28f);
            _deleteBtnImg = deleteBtn.GetComponent<Image>();
            EditorUIHelpers.MakeButton(toolbar.transform, "Undo", () => _undo.Undo(), 28f, 11f);
            EditorUIHelpers.MakeButton(toolbar.transform, "Redo", () => _undo.Redo(), 28f, 11f);

            EditorUIHelpers.BuildSeparator(left.transform);

            // Search filter
            _searchBox = SearchBox.Create(left.transform, "Search entities\u2026",
                v => { _searchFilter = v ?? ""; RefreshPicker(); });

            var (scroll, content) = EditorUIHelpers.MakeGridPicker(left.transform, "EntityGrid", 4, 72f, 4f);
            _pickerContent = content;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);

            // Right sidebar — Properties
            var right = EditorUIHelpers.MakeRightPanel("PropsPanel", _root.transform, 340f);
            EditorUIHelpers.AddVLG(right, 8, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "ENTITY PROPERTIES");

            var (pScroll, pContent) = EditorUIHelpers.MakeScrollView(right.transform, "PropsScroll");
            _propsTmp = EditorUIHelpers.AddLabel(pContent, "Select an entity to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;

            // Tutorial overlay
            _tutorial = TutorialOverlay.Build(_root.transform, "ENTITIES HOTKEYS", new[]
            {
                ("F5",     "Toggle Entities Editor"),
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