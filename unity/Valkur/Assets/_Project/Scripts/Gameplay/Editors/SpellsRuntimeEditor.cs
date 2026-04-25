using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Runtime in-game Spells Editor (F4).
    /// Browse, inspect, and edit SpellDefinition assets at runtime.
    /// Mirrors Python's spells_editor (F4) with picker grid + properties panel.
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Spell catalog asset")]
        private SpellCatalog _catalog;

        private bool _active;
        private InputAction _toggleAction;
        private InputAction _ctrlModifier;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _pickerContent;
        private TextMeshProUGUI _titleTmp;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private ScrollRect _propsScroll;
        private string _selectedKey;
        private string _searchFilter = "";
        private TMP_InputField _searchBox;
        private GameObject _tutorial;
        private EditorToolbar _toolbar;

        // IGameEditor
        public string EditorName => "Spells Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleSpellsEditor", InputActionType.Button, "<Keyboard>/f4");
            _toggleAction.Enable();
            _ctrlModifier = new InputAction("Ctrl", InputActionType.Button, "<Keyboard>/leftCtrl");
            _ctrlModifier.Enable();
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
            _ctrlModifier?.Dispose();
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
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            RefreshPicker();
            _statusTmp.text = "Spells Editor active. F4 to close.";
            Debug.Log("[SpellsEditor] Activated (F4)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedKey = null;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[SpellsEditor] Deactivated (F4)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI Construction ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("SpellsEditorCanvas", 105);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            // Left sidebar — Picker
            var left = EditorUIHelpers.MakeSidebar("PickerPanel", _root.transform, 320f);
            EditorUIHelpers.AddVLG(left, 8, 4f);
            _titleTmp = EditorUIHelpers.MakeTitleBar(left.transform, "SPELLS EDITOR");

            // Search filter
            _searchBox = SearchBox.Create(left.transform, "Search spells…",
                v => { _searchFilter = v ?? ""; RefreshPicker(); });

            var (scroll, content) = EditorUIHelpers.MakeGridPicker(left.transform, "SpellGrid", 4, 72f, 4f);
            _pickerContent = content;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);

            // Right sidebar — Properties
            var right = EditorUIHelpers.MakeRightPanel("PropsPanel", _root.transform, 340f);
            EditorUIHelpers.AddVLG(right, 8, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "SPELL PROPERTIES");

            var (pScroll, pContent) = EditorUIHelpers.MakeScrollView(right.transform, "PropsScroll");
            _propsScroll = pScroll;
            _propsTmp = EditorUIHelpers.AddLabel(pContent, "Select a spell to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;

            // Tutorial overlay (hotkey hints), docked right, hidden by default.
            _tutorial = TutorialOverlay.Build(_root.transform, "SPELLS HOTKEYS", new[]
            {
                ("F4",     "Toggle Spells Editor"),
                ("Click",  "Select a spell"),
                ("Type",   "Filter by name"),
                ("Esc",    "Close all editors"),
            });
            _tutorial.SetActive(false);
        }

        // ── Picker ──

    }
}