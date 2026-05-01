using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Runtime in-game Particles Editor (F1).
    /// Browse particle presets, place/move/delete instances on the map.
    ///
    /// UI/UX mirrors the Python <c>roguelike_editors/particles</c> package:
    ///   • Title bar               → "PARTICLES EDITOR"
    ///   • Toolbar (Select/Place/Delete) → particles_tool_bar_panel
    ///   • Add System / Remove row → particles_add_remove_panel
    ///   • Search + Group-by-Kind  → particles_picker_panel toggle (ALL / GROUP)
    ///   • Picker grid             → particles_picker_panel (presets w/ icons)
    ///   • Preset properties panel → particles_properties_panel
    ///   • Spells using this preset (collapsible) → particles_spells_list_panel
    ///   • Tutorial overlay (F1, LMB, RMB, Type, Ctrl+Z, Ctrl+Y, Esc)
    ///
    /// Layout matches the existing TILES EDITOR (F8) and BUILDINGS EDITOR (F10):
    /// left sidebar = picker, right sidebar = properties + usage.
    /// </summary>
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Particle preset catalog")]
        private ParticlePresetCatalog _catalog;

        private bool _active;
        private InputAction _toggleAction;
        private bool _ownsToggleAction;

        private enum EditorMode { Select, Place, Delete }
        private EditorMode _mode = EditorMode.Select;
        private string _selectedPresetId;

        // Drag
        private bool _dragging;
        private GameObject _dragTarget;
        private Vector3 _dragOffset;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _pickerContent;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private TextMeshProUGUI _instanceTmp;     // INSTANCE PROPERTIES section
        private Image _selectBtnImg, _placeBtnImg, _deleteBtnImg;
        private Image _addSystemBtnImg, _removeBtnImg;

        // Group-by-kind toggle (mirrors Python ALL / GROUP toggle in picker_view)
        private bool _groupByKind;
        private TextMeshProUGUI _groupToggleLabel;
        private Image _groupToggleImg;

        // Spells-using-this-preset panel (mirrors Python particles_spells_list_panel)
        private GameObject _spellsPanelRoot;
        private RectTransform _spellsContent;
        private TextMeshProUGUI _spellsHeaderTmp;     // ▼/▶ Spells label
        private bool _spellsExpanded = true;

        // EditorKit extras
        private string _searchFilter = "";
        private TMP_InputField _searchBox;
        private GameObject _tutorial;
        private readonly UndoStack _undo = new UndoStack(64);

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // IGameEditor
        public string EditorName => "Particles Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleParticles, out _ownsToggleAction);
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            // Plain F1 — no modifier (mirrors TILES (F8) / BUILDINGS (F10) hotkey style).
            if (_toggleAction.WasPerformedThisFrame())
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }
            if (!_active) return;

            // Middle-mouse camera pan — same UX as every other runtime editor.
            _cameraPan.Tick();

            HandleMapInteraction();
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            RefreshPicker();
            RefreshModeButtons();
            RefreshSpellsPanel();
            _statusTmp.text = "Particles Editor active. F1 to close.";
            Debug.Log("[ParticlesEditor] Activated (F1)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedPresetId = null;
            _dragging = false;
            _dragTarget = null;
            // Reattach the camera follow target if MMB pan had detached it.
            _cameraPan.Reset();
            Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[ParticlesEditor] Deactivated (F1)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("ParticlesEditorCanvas", 110);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            BuildLeftSidebar();
            BuildRightSidebar();

            _tutorial = TutorialOverlay.Build(_root.transform, "PARTICLES HOTKEYS", new[]
            {
                ("F1",     "Toggle Particles Editor"),
                ("LMB",    "Select / Place / Delete"),
                ("RMB",    "Drag to move instance"),
                ("Type",   "Filter by name / id"),
                ("Group",  "Toggle ALL / GROUP-by-kind"),
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("Esc",    "Close all editors"),
            });
            _tutorial.SetActive(false);
        }

        // Left sidebar — mirrors Python: title bar + toolbar + add/remove + search + picker grid + status.
        private void BuildLeftSidebar()
        {
            var left = EditorUIHelpers.MakeSidebar("PickerPanel", _root.transform, 320f);
            EditorUIHelpers.AddVLG(left, 8, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "PARTICLES EDITOR");

            // Toolbar — mirrors particles_tool_bar_panel (Select / Place / Delete modes).
            var toolbar = EditorUIHelpers.CreateUI("Toolbar", left.transform);
            toolbar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            var selectBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Select", () => SetMode(EditorMode.Select), 28f, 11f);
            _selectBtnImg = selectBtn.GetComponent<Image>();
            var placeBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Place", () => SetMode(EditorMode.Place), 28f, 11f);
            _placeBtnImg = placeBtn.GetComponent<Image>();
            var deleteBtn = EditorUIHelpers.MakeDangerButton(toolbar.transform, "Delete", () => SetMode(EditorMode.Delete), 28f);
            _deleteBtnImg = deleteBtn.GetComponent<Image>();

            // Add / Remove row — mirrors Python particles_add_remove_panel
            // ("Add System" + "Remove" with their cyan / red blink styling).
            var addRemRow = EditorUIHelpers.CreateUI("AddRemoveRow", left.transform);
            addRemRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var arHlg = addRemRow.AddComponent<HorizontalLayoutGroup>();
            arHlg.spacing = 4f; arHlg.childForceExpandWidth = true;
            var addSysBtn = EditorUIHelpers.MakeButton(addRemRow.transform, "+ Add System",
                () => OnAddSystemClicked(), 28f, 11f);
            _addSystemBtnImg = addSysBtn.GetComponent<Image>();
            var removeBtn = EditorUIHelpers.MakeDangerButton(addRemRow.transform, "− Remove",
                () => OnRemoveClicked(), 28f);
            _removeBtnImg = removeBtn.GetComponent<Image>();

            // Save / Undo / Redo — utility row (Buildings/Tiles editor parity)
            var utilRow = EditorUIHelpers.CreateUI("UtilRow", left.transform);
            utilRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var uhlg = utilRow.AddComponent<HorizontalLayoutGroup>();
            uhlg.spacing = 4f; uhlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(utilRow.transform, "Save", () => SaveInstances(), 28f, 11f);
            EditorUIHelpers.MakeButton(utilRow.transform, "Undo", () => _undo.Undo(), 28f, 11f);
            EditorUIHelpers.MakeButton(utilRow.transform, "Redo", () => _undo.Redo(), 28f, 11f);

            EditorUIHelpers.BuildSeparator(left.transform);

            // Search box (filters by id / display name).
            _searchBox = SearchBox.Create(left.transform, "Search presets…",
                v => { _searchFilter = v ?? ""; RefreshPicker(); });

            // Group-by-kind toggle row — mirrors Python picker_view "ALL / GROUP" pill.
            var groupRow = EditorUIHelpers.CreateUI("GroupRow", left.transform);
            groupRow.AddComponent<LayoutElement>().preferredHeight = 24f;
            var grHlg = groupRow.AddComponent<HorizontalLayoutGroup>();
            grHlg.spacing = 4f; grHlg.childForceExpandWidth = true;
            EditorUIHelpers.AddLabel(groupRow.transform, "Display:", 11f);
            var groupBtn = EditorUIHelpers.MakeButton(groupRow.transform, "ALL",
                () => ToggleGroupByKind(), 22f, 10f);
            _groupToggleImg   = groupBtn.GetComponent<Image>();
            _groupToggleLabel = groupBtn.GetComponentInChildren<TextMeshProUGUI>();

            // Picker grid — mirrors particles_picker_panel (preset cells with icons).
            var (_, content) = EditorUIHelpers.MakeGridPicker(left.transform, "PresetGrid", 4, 64f, 4f);
            _pickerContent = content;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);
        }

        // Right sidebar — mirrors Python: properties_panel + spells_list_panel.
        private void BuildRightSidebar()
        {
            var right = EditorUIHelpers.MakeRightPanel("PropsPanel", _root.transform, 320f);
            EditorUIHelpers.AddVLG(right, 8, 4f);

            // Section 1 — PRESET PROPERTIES (kind, lifetime, count, etc.)
            EditorUIHelpers.BuildSectionHeader(right.transform, "PRESET PROPERTIES");
            var (_, pContent) = EditorUIHelpers.MakeScrollView(right.transform, "PropsScroll");
            pContent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 2f;
            _propsTmp = EditorUIHelpers.AddLabel(pContent, "Select a preset to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;

            EditorUIHelpers.BuildSeparator(right.transform);

            // Section 2 — INSTANCE PROPERTIES (id, zone, world position).
            EditorUIHelpers.BuildSectionHeader(right.transform, "INSTANCE PROPERTIES");
            var (_, iContent) = EditorUIHelpers.MakeScrollView(right.transform, "InstanceScroll");
            iContent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _instanceTmp = EditorUIHelpers.AddLabel(iContent, "Select an instance on the map.", 11f);
            _instanceTmp.color = EditorUIHelpers.TEXT_SECONDARY;

            EditorUIHelpers.BuildSeparator(right.transform);

            // Section 3 — SPELLS USING THIS PRESET (collapsible) — mirrors particles_spells_list_panel.
            BuildSpellsPanel(right.transform);
        }

        private void BuildSpellsPanel(Transform parent)
        {
            _spellsPanelRoot = EditorUIHelpers.CreateUI("SpellsPanel", parent);
            var le = _spellsPanelRoot.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 80f;
            var vlg = _spellsPanelRoot.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Collapsible header — clickable "▼ SPELLS USING THIS PRESET" / "▶ ..."
            var headerBtn = EditorUIHelpers.MakeButton(_spellsPanelRoot.transform,
                "▼ SPELLS USING THIS PRESET",
                () => ToggleSpellsExpanded(), 24f, 11f);
            _spellsHeaderTmp = headerBtn.GetComponentInChildren<TextMeshProUGUI>();
            _spellsHeaderTmp.alignment = TextAlignmentOptions.Left;
            _spellsHeaderTmp.margin = new Vector4(8f, 0f, 0f, 0f);

            var (_, sContent) = EditorUIHelpers.MakeScrollView(_spellsPanelRoot.transform, "SpellsScroll");
            sContent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _spellsContent = sContent;
        }

        // ── Mode ──
    }
}