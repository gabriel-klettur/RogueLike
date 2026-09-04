using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Builds the UI for the Spells Runtime Editor (F4).
    ///
    /// Mirrors the menu-bar + floating-dropdown-panel architecture established by
    /// ItemsEditorUIBuilder and BuildingsEditorUIBuilder:
    ///   • 30 px menu bar at top   — brand + Modes / Spells / Properties / View / Tutorial + PERF
    ///   • Modes panel      (60 px, top-left)   — Add / Remove / Reload / Undo / Redo / Save
    ///   • Spells panel     (256 px, picker)    — search + 4-col grid catalog (+ Table view)
    ///   • Properties panel (320 px, top-right) — TabStrip [Properties | Assets/Particles]
    ///   • View panel       (~420x624)          — live spell preview surface + transport
    ///   • Tutorial panel   (~360x300)          — 6-step guided walkthrough
    ///
    /// All callbacks are wired by SpellsRuntimeEditor.
    ///
    /// Partials:
    ///   SpellsEditorUIBuilder.Panels.cs   — MenuBar, ModesPanel, PropertiesPanel, TutorialPanel
    ///   SpellsEditorUIBuilder.SpellsPanel.cs — SpellsPanel, resize handle, scrollbar helper
    ///   SpellsEditorUIBuilder.ViewPanel.cs   — ViewPanel, speed button, character toggle
    ///   SpellsEditorUIBuilder.Helpers.cs     — MakeDrop, ApplyDock, button/label primitives
    /// </summary>
    public static partial class SpellsEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject       MenuBar;
            public Image            ModesMenuBtnImg;     public TextMeshProUGUI ModesMenuBtnTmp;
            public Image            SpellsMenuBtnImg;    public TextMeshProUGUI SpellsMenuBtnTmp;
            public Image            PropsMenuBtnImg;     public TextMeshProUGUI PropsMenuBtnTmp;
            public Image            ViewMenuBtnImg;      public TextMeshProUGUI ViewMenuBtnTmp;
            public Image            TutorialMenuBtnImg;  public TextMeshProUGUI TutorialMenuBtnTmp;
            public Image            PerfProbeMenuBtnImg; public TextMeshProUGUI PerfProbeMenuBtnTmp;

            // Panel roots + drag components
            public GameObject       ModesDropdown;     public DraggablePanel ModesPanelDrag;
            public GameObject       SpellsDropdown;    public DraggablePanel SpellsPanelDrag;
            public GameObject       PropsDropdown;     public DraggablePanel PropsPanelDrag;
            public GameObject       ViewDropdown;      public DraggablePanel ViewPanelDrag;
            public GameObject       TutorialDropdown;  public DraggablePanel TutorialPanelDrag;

            // Modes panel — action buttons
            public Image            AddBtnImg;
            public Image            RemoveBtnImg;
            public Image            ReloadBtnImg;
            public Image            UndoBtnImg;
            public Image            RedoBtnImg;
            public Image            SaveBtnImg;

            // Spells panel — shared
            public TabStrip         SpellAudienceTabs;
            public TMP_InputField   SearchBox;
            public TextMeshProUGUI  StatusText;

            // Spells panel — Grid view
            public RectTransform    PickerContent;

            // Spells panel — Tree view
            public TabStrip         SpellsViewTabs;
            public TabStrip         SpellsTreeSchoolTabs;
            public ScrollRect       SpellsTreeScroll;
            public RectTransform    SpellsTreeContent;

            // Spells panel — Table view
            public ScrollRect       SpellsTableHeaderScroll;
            public RectTransform    SpellsTableHeaderContent;
            public ScrollRect       SpellsTableBodyScroll;
            public RectTransform    SpellsTableBodyContent;
            public Button           SpellsColumnsCfgBtn;
            public TextMeshProUGUI  SpellsColumnsCfgLabel;

            // Properties panel
            public TabStrip         PropsTabStrip;
            public PropertyForm     PropsForm;
            public RectTransform    PropsAssetsRoot;
            public Image            AssetPreviewImage;
            public TextMeshProUGUI  AssetNameTmp;

            // Properties panel — Gather tab (cast flourish knobs)
            public PropertyForm     PropsGatherForm;
            public RectTransform    PropsGatherRoot;
            public TextMeshProUGUI  GatherFamilyTmp;

            // View panel — live preview surface
            public RawImage         ViewRawImage;
            public RectTransform    ViewPreviewArea;
            public TextMeshProUGUI  ViewSpellNameTmp;
            public TextMeshProUGUI  ViewStatusTmp;
            public Button           ViewDirNBtn;
            public Button           ViewDirSBtn;
            public Button           ViewDirEBtn;
            public Button           ViewDirWBtn;
            public Button           ViewZoomInBtn;
            public Button           ViewZoomOutBtn;

            // View panel — character overlay toggle
            public Button                        ViewCharacterToggleBtn;
            public Image                         ViewCharacterToggleBtnImg;
            public TMPro.TextMeshProUGUI         ViewCharacterToggleLabel;

            // View panel — transport row (play/pause, speed, frame scrubber)
            public Button           ViewPlayPauseBtn;
            public TextMeshProUGUI  ViewPlayPauseBtnLabel;
            public Button           ViewSpeed025Btn;
            public Image            ViewSpeed025BtnImg;
            public Button           ViewSpeed05Btn;
            public Image            ViewSpeed05BtnImg;
            public Button           ViewSpeed1Btn;
            public Image            ViewSpeed1BtnImg;
            public Button           ViewPrevFrameBtn;
            public Button           ViewNextFrameBtn;
            public Slider           ViewFrameSlider;
            public TextMeshProUGUI  ViewFrameCounterLabel;

            // Tutorial panel
            public TextMeshProUGUI  TutorialStepLabel;
            public TextMeshProUGUI  TutorialBodyTmp;
            public Button           TutorialPrevBtn;
            public Button           TutorialNextBtn;
            public Button           TutorialCloseBtn;
        }

        // ── Panel sizes ───────────────────────────────────────────────────────────

        private const float MODES_W    = TOOLS_DROP_W;          // 60
        private const float MODES_H    = 320f + PANEL_HDR_H;
        // Wider than TILES_DROP_W (256) so 4 columns of 64×64 cells fit cleanly.
        private const float SPELLS_W   = 312f;
        private const float SPELLS_H   = TILES_DROP_H;          // 564
        private const float PROPS_W    = 340f;
        private const float PROPS_H    = 560f + PANEL_HDR_H;
        private const float TUT_W      = 360f;
        private const float TUT_H      = 300f + PANEL_HDR_H;
        // View panel — square preview surface + direction selector + character toggle
        // + zoom row + transport row + scrubber row + status.
        private const float VIEW_W     = 420f;
        private const float VIEW_H     = 624f + PANEL_HDR_H;

        // ── Menu button widths ────────────────────────────────────────────────────

        private const float TITLE_BTN_W    = 130f;
        private const float MODES_BTN_W    = 70f;
        private const float SPELLS_BTN_W   = 70f;
        private const float PROPS_BTN_W    = 98f;
        private const float VIEW_BTN_W     = 60f;
        private const float TUTORIAL_BTN_W = 84f;
        private const float HELP_BTN_W     = 40f;
        private const float PERF_BTN_W     = 46f;

        private const float BTN_H = 32f;

        // ── BuildAll ──────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action         onAdd,
            Action         onRemove,
            Action         onReload,
            Action         onUndo,
            Action         onRedo,
            Action         onSave,
            Action<string> onSearchChanged,
            Action         onTutorialPrev,
            Action         onTutorialNext,
            Action         onTutorialClose,
            Action         onPerfToggle = null)
        {
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();
            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onPerfToggle);
            BuildModesPanel(canvasT, ref refs, onAdd, onRemove, onReload, onUndo, onRedo, onSave);
            BuildSpellsPanel(canvasT, ref refs, onSearchChanged);
            BuildPropertiesPanel(canvasT, ref refs);
            BuildViewPanel(canvasT, ref refs);
            BuildTutorialPanel(canvasT, ref refs, onTutorialPrev, onTutorialNext, onTutorialClose);
            return refs;
        }

        // ── Public helpers ────────────────────────────────────────────────────────

        public static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null) img.color = isOpen ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? ACCENT          : TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        public static void ApplyToolBtnStyle(Image img, bool active, bool danger = false)
        {
            if (img == null) return;
            if (danger)
            {
                img.color = active
                    ? UITheme.DANGER
                    : UITheme.DANGER_IDLE;
            }
            else
            {
                img.color = active ? BTN_ACTIVE : BTN_NORMAL;
            }
        }
    }
}
