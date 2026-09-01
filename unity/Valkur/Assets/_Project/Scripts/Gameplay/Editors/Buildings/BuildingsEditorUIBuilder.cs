using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Builds all UI panels for the Buildings Runtime Editor using the same
    /// professional menu-bar + floating-panel architecture as the Tile Editor (F8).
    ///
    /// Layout mirrors TileEditor exactly:
    ///   • 30 px menu bar at top   — brand + dropdown buttons + tutorial shortcut
    ///   • Modes panel  (60 px, top-left)  ≡ TileEditor "Tools" panel
    ///   • Buildings panel (256 px, next)  ≡ TileEditor "Tiles" panel
    ///   • Properties panel (250 px, right) ≡ TileEditor "Inspector" panel
    ///
    /// All floating panels use DraggablePanel + PanelChrome so they participate
    /// in the shared TileEditorTheme live-repaint system.
    /// </summary>
    public static partial class BuildingsEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject      MenuBar;
            public Image           ModesMenuBtnImg;     public TextMeshProUGUI ModesMenuBtnTmp;
            public Image           BuildingsMenuBtnImg; public TextMeshProUGUI BuildingsMenuBtnTmp;
            public Image           CollidersMenuBtnImg; public TextMeshProUGUI CollidersMenuBtnTmp;
            public Image           PropsMenuBtnImg;     public TextMeshProUGUI PropsMenuBtnTmp;

            // Panel roots + drag components
            public GameObject    ModesDropdown;     public DraggablePanel ModesPanelDrag;
            public GameObject    BuildingsDropdown; public DraggablePanel BuildingsPanelDrag;
            public GameObject    CollidersDropdown; public DraggablePanel CollidersPanelDrag;
            public GameObject    PropsDropdown;     public DraggablePanel PropsPanelDrag;

            // Menu bar extras
            public Image           BuildingVisibilityMenuBtnImg;
            public TextMeshProUGUI BuildingVisibilityMenuBtnTmp;
            public Image           PerfProbeMenuBtnImg;
            public TextMeshProUGUI PerfProbeMenuBtnTmp;

            // Modes panel refs
            public Image SelectBtnImg, PlaceBtnImg, ResizeBtnImg, DeleteBtnImg;
            public Image AddBtnImg, RemoveBtnImg;
            public Image FillBtnImg;   // Fill tool button
            public Image EraseBtnImg;  // Erase tool button
            public Image DoorBtnImg;   // Door tool button

            // Erase scope sub-panel (flyout below TOOLS)
            public GameObject EraseSubPanel;
            public Image      EraseTilesAreaBtnImg;
            public Image      EraseZoneBtnImg;

            // Door authoring sub-panel (flyout below TOOLS). Template-scope controls are
            // deliberately in their own panel rather than in the per-instance Properties
            // inspector, because they change every placement of the art.
            public GameObject      DoorSubPanel;
            public TextMeshProUGUI DoorStatusText;
            public Image           DoorHasDoorBtnImg;  public TextMeshProUGUI DoorHasDoorBtnLabel;
            public TMP_InputField  DoorTargetField;
            public TMP_InputField  DoorSpawnXField;
            public TMP_InputField  DoorSpawnYField;
            public TextMeshProUGUI DoorAnchorXVal, DoorAnchorYVal, DoorSizeVal;
            public Image           DoorApplyBtnImg;

            // Buildings panel refs
            public TMP_InputField  SearchBox;
            public TabStrip        CategoryTabStrip;
            public RectTransform   PickerContent;
            public TextMeshProUGUI StatusText;

            // Properties panel refs
            public TextMeshProUGUI PropsText;       // hint when idle OR rich-text building info
            public GameObject      InspectorRoot;   // hidden until a building is selected
            public Slider          SplitSlider;
            public TextMeshProUGUI ZBottomVal, ZTopVal;
            public TextMeshProUGUI GridColsVal, GridRowsVal;   // collider grid resolution stepper values
            public Image           ScopeBtnImg;
            public TextMeshProUGUI ScopeBtnLabel;
            public Image           InteractableBtnImg;
            public TextMeshProUGUI InteractableBtnLabel;

            // Colliders panel refs (redesigned: ON/OFF toggle + Paint/Erase action + scope + size).
            public Image           CollVisibilityBtnImg;   public TextMeshProUGUI CollVisibilityBtnLabel;
            public Image           CollScopeBtnImg;        public TextMeshProUGUI CollScopeBtnLabel;
            public Image           CollBrushToggleImg;     public TextMeshProUGUI CollBrushToggleLabel;
            public Image           CollPaintBtnImg;        // # action button
            public Image           CollEraseBtnImg;        // . action button
            // Brush-size preset buttons (sizes 1–8, matching TileEditor style)
            public List<Image>           CollBrushSizePresetImgs;
            public List<TextMeshProUGUI> CollBrushSizePresetLabels;
            public TextMeshProUGUI       CollBrushSizeLabel;         // stepper centre value
            public TextMeshProUGUI CollTargetText;         // "ID 142 | Scope CG\nimage:..."
            public TextMeshProUGUI CollStateText;          // "Grid 8x6 | Solids 12 | Dirty | ON #"
            public TextMeshProUGUI CollHintText;
        }

        // ── Panel sizes (mirrors TileEditor constants) ────────────────────────────

        private const float MODES_W     = TOOLS_DROP_W;          // 60 px
        // Tools: Undo + Redo + Fill + Erase + Door. The multiplier is the count of BTN_H-tall
        // buttons BEYOND the two the 88f base already covers - a 6th button needs a 4 here, or
        // it is clipped off the bottom of the panel with no other symptom.
        private const float MODES_H     = 88f + BTN_H * 3 + PANEL_HDR_H;
        private const float ERASE_SUB_W = 130f;
        private const float ERASE_SUB_H = PANEL_HDR_H + BTN_H * 2 + 12f;
        private const float BUILDINGS_W = TILES_DROP_W;          // 256 px
        private const float BUILDINGS_H = TILES_DROP_H;          // 564 px
        private const float COLLIDERS_W = 220f;                  // narrower than props
        private const float COLLIDERS_H = 470f + PANEL_HDR_H;
        private const float PROPS_W     = INSPECTOR_DROP_W;      // 250 px
        private const float PROPS_H     = 480f + PANEL_HDR_H;    // 504 px (extra room for grid resolution rows)

        // ── Menu button widths ─────────────────────────────────────────────────

        private const float TITLE_BTN_W     = 145f;
        private const float MODES_BTN_W     = 70f;
        private const float BUILDINGS_BTN_W = 92f;
        private const float COLLIDERS_BTN_W = 92f;
        private const float PROPS_BTN_W      = 98f;
        private const float VIS_BTN_W        = 40f;
        private const float TUTORIAL_BTN_W   = 40f;
        private const float PERF_BTN_W       = 46f;

        private const float BTN_H = 44f;   // mode/tool button height (same as TileEditor)

        // ── BuildAll ─────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action         onUndo,         Action onRedo,
            Action         onSave,         Action onReload,
            Action         onModeSelect,   Action onModePlace,
            Action         onModeResize,   Action onModeDelete,
            Action         onAddBuilding,  Action onRemoveBuilding, Action onAddOnSystem,
            Action         onToggleTutorial,
            Action<string> onSearchChanged,
            Action<float>  onSplitChanged,
            Action         onZBottomMinus, Action onZBottomPlus,
            Action         onZTopMinus,    Action onZTopPlus,
            Action         onGridColsMinus, Action onGridColsPlus,
            Action         onGridRowsMinus, Action onGridRowsPlus,
            Action         onColliderScope,
            Action         onInteractable,
            Action         onPaintSolid,   Action onPaintWalk, Action onSaveCU,
            Action         onDeleteBuilding,
            Action         onResetBuilding,
            // Colliders panel callbacks (redesigned)
            Action         onToggleCollidersVisible,
            Action         onCollScopeToggle,
            Action         onBrushPaint,                  // # → action = Paint (toggle)
            Action         onBrushErase,                  // . → action = Erase (toggle)
            Action<int>    onCollBrushSizeChanged,
            Action         onCollBrushSizeStepDown,
            Action         onCollBrushSizeStepUp,
            Action         onToggleBuildingsVisible,
            Action         onPerfToggle = null,
            Action         onFill       = null,
            Action         onErase           = null,
            Action         onEraseTilesArea  = null,
            Action         onEraseZone       = null,
            Action<string> onCategoryChanged = null,
            // Door authoring callbacks
            Action         onDoor            = null,
            Action         onDoorToggleHasDoor = null,
            Action<string> onDoorTargetCommit  = null,
            Action<string> onDoorSpawnXCommit  = null,
            Action<string> onDoorSpawnYCommit  = null,
            Action         onDoorAnchorXMinus  = null, Action onDoorAnchorXPlus = null,
            Action         onDoorAnchorYMinus  = null, Action onDoorAnchorYPlus = null,
            Action         onDoorSizeMinus     = null, Action onDoorSizePlus    = null,
            Action         onDoorApply         = null, Action onDoorClear       = null)
        {
            // Reserve space below the menu bar so draggable panels cannot occlude it
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();
            refs.CollBrushSizePresetImgs   = new List<Image>();
            refs.CollBrushSizePresetLabels = new List<TextMeshProUGUI>();
            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial,
                onToggleBuildingsVisible, onPerfToggle);
            BuildModesPanel(canvasT, ref refs,
                onModeSelect, onModePlace, onModeResize, onModeDelete,
                onAddBuilding, onRemoveBuilding, onAddOnSystem,
                onUndo, onRedo, onSave, onReload, onFill, onErase, onDoor);
            BuildEraseSubPanel(canvasT, ref refs, onEraseTilesArea, onEraseZone);
            BuildDoorSubPanel(canvasT, ref refs,
                onDoorToggleHasDoor,
                onDoorTargetCommit, onDoorSpawnXCommit, onDoorSpawnYCommit,
                onDoorAnchorXMinus, onDoorAnchorXPlus,
                onDoorAnchorYMinus, onDoorAnchorYPlus,
                onDoorSizeMinus,    onDoorSizePlus,
                onDoorApply,        onDoorClear);
            BuildBuildingsPanel(canvasT, ref refs, onSearchChanged, onCategoryChanged);
            BuildCollidersPanel(canvasT, ref refs,
                onToggleCollidersVisible,
                onCollScopeToggle,
                onBrushPaint, onBrushErase,
                onCollBrushSizeChanged, onCollBrushSizeStepDown, onCollBrushSizeStepUp);
            BuildPropertiesPanel(canvasT, ref refs, onSplitChanged,
                onZBottomMinus, onZBottomPlus, onZTopMinus, onZTopPlus,
                onGridColsMinus, onGridColsPlus, onGridRowsMinus, onGridRowsPlus,
                onColliderScope, onInteractable, onPaintSolid, onPaintWalk, onSaveCU, onDeleteBuilding, onResetBuilding);
            return refs;
        }

        // ── Public helper: menu-button highlight ──────────────────────────────────

        /// <summary>
        /// Called by BuildingsRuntimeEditor to reflect open/closed dropdown state
        /// on the corresponding menu bar button (mirrors TileEditorUI.ApplyMenuBtnStyle).
        /// </summary>
        public static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null) img.color = isOpen ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? ACCENT      : TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // ── Menu Bar ──────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onTutorial, Action onToggleBuildingsVisible, Action onPerfToggle)
        {
            var go = CreateUI("BuildingsMenuBar", canvasT);
            var r  = go.GetComponent<RectTransform>();
            r.anchorMin        = new Vector2(0f, 1f);
            r.anchorMax        = new Vector2(1f, 1f);
            r.pivot            = new Vector2(0.5f, 1f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta        = new Vector2(0f, MENUBAR_HEIGHT);
            refs.MenuBar       = go;

            var bg           = go.AddComponent<Image>();
            bg.color         = MENUBAR_BG;
            bg.raycastTarget = true;

            var ol            = go.AddComponent<Outline>();
            ol.effectColor    = BORDER;
            ol.effectDistance = new Vector2(0f, -1f);

            var chrome           = go.AddComponent<MenuBarChrome>();
            chrome.BgImage       = bg;
            chrome.BorderOutline = ol;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding             = new RectOffset((int)MENUBAR_PAD_H, (int)MENUBAR_PAD_H, 0, 0);
            hlg.spacing             = MENUBAR_SPACING;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var t = go.transform;

            // Brand
            var brand           = CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_BTN_W;
            var brandTmp        = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text             = "BUILDINGS EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ModesMenuBtnImg     = AddMenuBtn(t, "Tools v",      MODES_BTN_W,
                () => onToggle?.Invoke("modes"),     out refs.ModesMenuBtnTmp);
            refs.BuildingsMenuBtnImg = AddMenuBtn(t, "Buildings v",  BUILDINGS_BTN_W,
                () => onToggle?.Invoke("buildings"), out refs.BuildingsMenuBtnTmp);
            refs.CollidersMenuBtnImg = AddMenuBtn(t, "Colliders v",  COLLIDERS_BTN_W,
                () => onToggle?.Invoke("colliders"), out refs.CollidersMenuBtnTmp);
            refs.PropsMenuBtnImg     = AddMenuBtn(t, "Properties v", PROPS_BTN_W,
                () => onToggle?.Invoke("props"),     out refs.PropsMenuBtnTmp);

            // Flexible spacer
            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            refs.BuildingVisibilityMenuBtnImg = AddMenuBtn(t, "VIS", VIS_BTN_W,
                () => onToggleBuildingsVisible?.Invoke(), out refs.BuildingVisibilityMenuBtnTmp);
            AddMenuDivider(t);
            AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
            AddMenuDivider(t);
            refs.PerfProbeMenuBtnImg = AddMenuBtn(t, "PERF", PERF_BTN_W,
                () => onPerfToggle?.Invoke(), out refs.PerfProbeMenuBtnTmp);
        }
    }
}
