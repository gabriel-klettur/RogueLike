using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>
    /// Builds all UI panels for the FSM Runtime Editor (F12) using the same
    /// professional menu-bar + draggable-panel architecture as the Buildings Editor (F10)
    /// and the Tile Editor (F8).
    ///
    /// Mirrors the Python <c>roguelike_editors/fsm</c> package layout:
    ///   • Tools panel       ≡ <c>fsm_toolbar</c> (undo/redo + save/reload + tutorial)
    ///   • Sets panel        ≡ <c>fsm_sets_panel</c> (search + scrollable FSM Set list)
    ///   • Entities panel    ≡ <c>fsm_assigment_entities</c> (entity → FSM Set mapping)
    ///   • Animations panel  ≡ <c>fsm_assigment_animations</c> (state → animation mapping)
    ///   • Graph panel       ≡ <c>fsm_graph_panel</c> (nodal canvas + tool buttons +
    ///                        legend; select / connect / delete / zoom / mark_ini /
    ///                        mark_end). Centre-docked, NOT a draggable floating panel.
    ///   • Properties panel  ≡ <c>fsm_properties_panel</c> (tabbed: State / Transition /
    ///                        Actions / Conditions / Blackboard).
    ///
    /// Visual identity is delegated to <see cref="TileEditorTheme"/> so the FSM Editor
    /// participates in the live-repaint UX theming used by the other runtime editors.
    /// </summary>
    public static partial class FSMEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject MenuBar;
            public Image      ToolsMenuBtnImg;       public TextMeshProUGUI ToolsMenuBtnTmp;
            public Image      SetsMenuBtnImg;        public TextMeshProUGUI SetsMenuBtnTmp;
            public Image      EntitiesMenuBtnImg;    public TextMeshProUGUI EntitiesMenuBtnTmp;
            public Image      AnimationsMenuBtnImg;  public TextMeshProUGUI AnimationsMenuBtnTmp;
            public Image      PropsMenuBtnImg;       public TextMeshProUGUI PropsMenuBtnTmp;
            public Image      PerfProbeMenuBtnImg;   public TextMeshProUGUI PerfProbeMenuBtnTmp;

            // Panel roots + drag components
            public GameObject ToolsDropdown;       public DraggablePanel ToolsPanelDrag;
            public GameObject SetsDropdown;        public DraggablePanel SetsPanelDrag;
            public GameObject EntitiesDropdown;    public DraggablePanel EntitiesPanelDrag;
            public GameObject AnimationsDropdown;  public DraggablePanel AnimationsPanelDrag;
            public GameObject PropsDropdown;       public DraggablePanel PropsPanelDrag;

            // Tools panel
            public Image UndoBtnImg, RedoBtnImg, SaveBtnImg, ReloadBtnImg;
            public Image BuiltInBtnImg;   public TextMeshProUGUI BuiltInBtnTmp;

            // Sets panel
            public TMP_InputField  SearchBox;
            public RectTransform   SetsContent;
            public TextMeshProUGUI StatusText;

            // Entities panel
            public RectTransform   EntitiesContent;
            public TextMeshProUGUI EntitiesHintText;

            // Animations panel
            public RectTransform   AnimationsContent;
            public TextMeshProUGUI AnimationsHintText;

            // Properties panel (tabs + content)
            public Image           StateTabImg;       public TextMeshProUGUI StateTabTmp;
            public Image           TransitionTabImg;  public TextMeshProUGUI TransitionTabTmp;
            public Image           ActionsTabImg;     public TextMeshProUGUI ActionsTabTmp;
            public Image           ConditionsTabImg;  public TextMeshProUGUI ConditionsTabTmp;
            public Image           BlackboardTabImg;  public TextMeshProUGUI BlackboardTabTmp;
            public TextMeshProUGUI PropsText;

            // Graph panel (centre, NOT a floating dropdown)
            public GameObject     GraphPanel;
            public RectTransform  GraphArea;
            public RectTransform  GraphContent;
            public TextMeshProUGUI GraphInfoText;
            public TextMeshProUGUI GraphZoomLabel;
            // Graph toolbar buttons (Python: select, connect, delete, zoom_in,
            // zoom_out, mark_ini, mark_end). Stored so the editor can highlight
            // the active tool.
            public Image SelectToolImg;
            public Image ConnectToolImg;
            public Image DeleteToolImg;
            public Image MarkIniToolImg;
            public Image MarkEndToolImg;
            public Image AddNodeToolImg;
            public Image CloneNodeToolImg;
            public Image DisconnectToolImg;
        }

        // ── Panel sizes ───────────────────────────────────────────────────────────

        private const float TOOLS_W      = TOOLS_DROP_W;          // 60 px (narrow)
        private const float TOOLS_H      = 220f + PANEL_HDR_H;    // Undo/Redo/Save/Reload

        private const float SETS_W       = 230f;
        private const float SETS_H       = 540f + PANEL_HDR_H;

        private const float ENTITIES_W   = 280f;
        private const float ENTITIES_H   = 420f + PANEL_HDR_H;

        private const float ANIMATIONS_W = 280f;
        private const float ANIMATIONS_H = 420f + PANEL_HDR_H;

        private const float PROPS_W      = INSPECTOR_DROP_W;      // 250 px
        private const float PROPS_H      = 540f + PANEL_HDR_H;

        // Graph panel insets (left = Tools + Sets gutter; right = Properties gutter).
        private const float GRAPH_LEFT_INSET  = PANEL_GAP + TOOLS_W + PANEL_GAP + SETS_W + PANEL_GAP;
        private const float GRAPH_RIGHT_INSET = PANEL_GAP + PROPS_W + PANEL_GAP;
        private const float GRAPH_TOP_INSET   = PANEL_TOP_OFFSET;
        private const float GRAPH_BOTTOM_INSET = PANEL_GAP;

        // ── Menu button widths ────────────────────────────────────────────────────

        private const float TITLE_BTN_W       = 120f;
        private const float TOOLS_BTN_W       = 60f;
        private const float SETS_BTN_W        = 60f;
        private const float ENTITIES_BTN_W    = 78f;
        private const float ANIMATIONS_BTN_W  = 96f;
        private const float PROPS_BTN_W       = 96f;
        private const float TUTORIAL_BTN_W    = 40f;
        private const float PERF_BTN_W2       = 46f;

        private const float BTN_H = 40f;

        // ── BuildAll ──────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action         onUndo,    Action onRedo,
            Action         onSave,    Action onReload,
            Action         onToggleBuiltIn,
            Action<string> onSearchChanged,
            Action         onTabState,      Action onTabTransition,
            Action         onTabActions,    Action onTabConditions,
            Action         onTabBlackboard,
            Action         onToolSelect,    Action onToolConnect,
            Action         onToolDelete,    Action onZoomIn,
            Action         onZoomOut,       Action onToolMarkIni,
            Action         onToolMarkEnd,
            Action         onToolAddNode      = null,
            Action         onToolCloneNode    = null,
            Action         onToolDisconnect   = null,
            Action         onToggleTutorial   = null,
            Action         onPerfToggle       = null)
        {
            // Reserve space below the menu bar so draggable panels cannot occlude it.
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();

            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial, onPerfToggle);
            BuildToolsPanel(canvasT, ref refs, onUndo, onRedo, onSave, onReload, onToggleBuiltIn);
            BuildSetsPanel(canvasT, ref refs, onSearchChanged);
            BuildEntitiesPanel(canvasT, ref refs);
            BuildAnimationsPanel(canvasT, ref refs);
            BuildGraphPanel(canvasT, ref refs,
                onToolSelect, onToolConnect, onToolDelete,
                onZoomIn, onZoomOut, onToolMarkIni, onToolMarkEnd,
                onToolAddNode, onToolCloneNode, onToolDisconnect);
            BuildPropertiesPanel(canvasT, ref refs,
                onTabState, onTabTransition, onTabActions, onTabConditions, onTabBlackboard);
            return refs;
        }

        // ── Public helper: menu-button highlight ──────────────────────────────────

        /// <summary>
        /// Reflects open/closed dropdown state on the corresponding menu bar button
        /// (mirrors <see cref="Valkur.Gameplay.Buildings.BuildingsEditorUIBuilder.ApplyMenuBtnStyle"/>).
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
            Action<string> onToggle, Action onTutorial, Action onPerfToggle)
        {
            var go = CreateUI("FSMMenuBar", canvasT);
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
            brandTmp.text             = "FSM EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ToolsMenuBtnImg      = AddMenuBtn(t, "Tools v",      TOOLS_BTN_W,
                () => onToggle?.Invoke("tools"),      out refs.ToolsMenuBtnTmp);
            refs.SetsMenuBtnImg       = AddMenuBtn(t, "Sets v",       SETS_BTN_W,
                () => onToggle?.Invoke("sets"),       out refs.SetsMenuBtnTmp);
            refs.EntitiesMenuBtnImg   = AddMenuBtn(t, "Entities v",   ENTITIES_BTN_W,
                () => onToggle?.Invoke("entities"),   out refs.EntitiesMenuBtnTmp);
            refs.AnimationsMenuBtnImg = AddMenuBtn(t, "Animations v", ANIMATIONS_BTN_W,
                () => onToggle?.Invoke("animations"), out refs.AnimationsMenuBtnTmp);
            refs.PropsMenuBtnImg      = AddMenuBtn(t, "Properties v", PROPS_BTN_W,
                () => onToggle?.Invoke("props"),      out refs.PropsMenuBtnTmp);

            // Flexible spacer
            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
            AddMenuDivider(t);
            refs.PerfProbeMenuBtnImg = AddMenuBtn(t, "PERF", PERF_BTN_W2,
                () => onPerfToggle?.Invoke(), out refs.PerfProbeMenuBtnTmp);
        }
    }
}
