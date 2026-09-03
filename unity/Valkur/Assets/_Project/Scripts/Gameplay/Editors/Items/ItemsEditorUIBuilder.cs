using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Builds all UI panels for the Items Runtime Editor (F7) using the same
    /// professional menu-bar + floating-panel architecture as the Tile Editor (F8)
    /// and the Buildings Editor (F10).
    ///
    /// Layout:
    ///   • 30 px menu bar at top   — brand + dropdown buttons + tutorial + perf
    ///   • Modes panel       (60 px, narrow, top-left)  — Select / Spawn / Delete
    ///                                                   + Add / Remove / Add-on-System
    ///                                                   + Undo / Redo
    ///   • Items panel       (256 px, next)             — search + grid catalog
    ///   • Properties panel  (250 px, top-right)        — selected item inspector hint
    ///   • Instances panel   (280 px, bottom-right)     — map drops list hint
    ///
    /// Mirrors Python's items_editor sub-panels (Title / Toolbar / AddRemove /
    /// Picker / Properties / Instances / Tutorial), remapped onto the Unity editor
    /// chrome.
    /// </summary>
    public static partial class ItemsEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject       MenuBar;
            public Image            ModesMenuBtnImg;     public TextMeshProUGUI ModesMenuBtnTmp;
            public Image            ItemsMenuBtnImg;     public TextMeshProUGUI ItemsMenuBtnTmp;
            public Image            PropsMenuBtnImg;     public TextMeshProUGUI PropsMenuBtnTmp;
            public Image            InstancesMenuBtnImg; public TextMeshProUGUI InstancesMenuBtnTmp;
            public Image            PerfProbeMenuBtnImg; public TextMeshProUGUI PerfProbeMenuBtnTmp;

            // Panel roots + drag components
            public GameObject       ModesDropdown;     public DraggablePanel ModesPanelDrag;
            public GameObject       ItemsDropdown;     public DraggablePanel ItemsPanelDrag;
            public GameObject       PropsDropdown;     public DraggablePanel PropsPanelDrag;
            public GameObject       InstancesDropdown; public DraggablePanel InstancesPanelDrag;

            // Modes panel — mode buttons + add/remove sub-toolbar
            public Image            SelectBtnImg;
            public Image            SpawnBtnImg;
            public Image            DeleteBtnImg;
            public Image            AddBtnImg;
            public Image            RemoveBtnImg;
            public Image            AddOnSystemBtnImg;

            // Items panel
            public TMP_InputField   SearchBox;
            public RectTransform    PickerContent;
            public TextMeshProUGUI  StatusText;
            public TextMeshProUGUI  GridEmptyState;       // shown when no items match filter
            public TabStrip         GridCategoryTabs;     // category filter atop the grid
            public Button           TableColumnsButton;   // "⚙ Columns" — opens the visibility popup
            public TextMeshProUGUI  TableColumnsCountLabel; // "Columns: N/M" indicator on the bar
            // Table view (second tab)
            public ScrollRect       TableHeaderScroll;
            public RectTransform    TableHeaderContent;
            public ScrollRect       TableBodyScroll;
            public RectTransform    TableBodyContent;

            // Properties panel
            public TextMeshProUGUI  PropsTitle;   // 22px header — shows item name or "(no selection)"
            public TextMeshProUGUI  PropsText;    // body inside the scroll content — shows the full inspector
            public RectTransform    PropsContent;

            // Instances panel
            public RectTransform    InstancesListContent;
            public TextMeshProUGUI  InstancesHint;
        }

        // ── Panel sizes (mirrors TileEditor / Buildings constants) ────────────────

        private const float MODES_W     = TOOLS_DROP_W;          // 60 px
        private const float MODES_H     = 360f + PANEL_HDR_H;    // tall: 3 modes + sep + 3 sub + sep + Undo/Redo
        private const float ITEMS_W     = TILES_DROP_W;          // 256 px
        private const float ITEMS_H     = TILES_DROP_H;          // 564 px
        private const float PROPS_W     = INSPECTOR_DROP_W;      // 250 px
        private const float PROPS_H     = 460f + PANEL_HDR_H;
        private const float INSTANCES_W = 280f;
        private const float INSTANCES_H = 320f + PANEL_HDR_H;

        // ── Menu button widths ────────────────────────────────────────────────────

        private const float TITLE_BTN_W     = 130f;
        private const float MODES_BTN_W     = 70f;
        private const float ITEMS_BTN_W     = 64f;
        private const float PROPS_BTN_W     = 98f;
        private const float INSTANCES_BTN_W = 92f;
        private const float TUTORIAL_BTN_W  = 40f;
        private const float PERF_BTN_W      = 46f;

        private const float BTN_H = 38f;   // tool button height

        // ── BuildAll ──────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action         onUndo,         Action onRedo,
            Action         onModeSelect,   Action onModeSpawn, Action onModeDelete,
            Action         onAdd,          Action onRemove,    Action onAddOnSystem,
            Action         onToggleTutorial,
            Action<string> onSearchChanged,
            Action         onPerfToggle = null)
        {
            // Reserve space below the menu bar so draggable panels cannot occlude it.
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();
            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial, onPerfToggle);
            BuildModesPanel(canvasT, ref refs,
                onModeSelect, onModeSpawn, onModeDelete,
                onAdd, onRemove, onAddOnSystem,
                onUndo, onRedo);
            BuildItemsPanel(canvasT, ref refs, onSearchChanged);
            BuildPropertiesPanel(canvasT, ref refs);
            BuildInstancesPanel(canvasT, ref refs);
            return refs;
        }

        // ── Menu Bar ──────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onTutorial, Action onPerfToggle)
        {
            var go = CreateUI("ItemsMenuBar", canvasT);
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
            var brand = CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_BTN_W;
            var brandTmp              = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text             = "ITEMS EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ModesMenuBtnImg     = AddMenuBtn(t, "Modes v",      MODES_BTN_W,
                () => onToggle?.Invoke("modes"),     out refs.ModesMenuBtnTmp);
            refs.ItemsMenuBtnImg     = AddMenuBtn(t, "Items v",      ITEMS_BTN_W,
                () => onToggle?.Invoke("items"),     out refs.ItemsMenuBtnTmp);
            refs.PropsMenuBtnImg     = AddMenuBtn(t, "Properties v", PROPS_BTN_W,
                () => onToggle?.Invoke("props"),     out refs.PropsMenuBtnTmp);
            refs.InstancesMenuBtnImg = AddMenuBtn(t, "Instances v",  INSTANCES_BTN_W,
                () => onToggle?.Invoke("instances"), out refs.InstancesMenuBtnTmp);

            // Flexible spacer
            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
            AddMenuDivider(t);
            refs.PerfProbeMenuBtnImg = AddMenuBtn(t, "PERF", PERF_BTN_W,
                () => onPerfToggle?.Invoke(), out refs.PerfProbeMenuBtnTmp);
        }

        // ── Public helpers (called from ItemsRuntimeEditor) ───────────────────────

        /// <summary>Mirrors BuildingsEditorUIBuilder.ApplyMenuBtnStyle.</summary>
        public static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null) img.color = isOpen ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? ACCENT      : TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        /// <summary>
        /// Highlights a tool button to reflect active mode. <paramref name="danger"/> uses red tones.
        /// </summary>
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

        // ── Internal helpers (private static) ─────────────────────────────────────

        private static void AddMenuDivider(Transform parent)
        {
            var go = CreateUI("Div", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 1f;
            go.AddComponent<Image>().color = BORDER;
        }

        private static Image AddMenuBtn(Transform parent, string label, float width,
            UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI tmp)
        {
            var go = CreateUI($"MenuBtn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;

            var img   = go.AddComponent<Image>();
            img.color = MENU_BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = MENU_BTN_NORMAL;
            c.highlightedColor = MENU_BTN_HOVER;
            c.pressedColor     = MENU_BTN_OPEN;
            c.selectedColor    = MENU_BTN_NORMAL;
            c.fadeDuration     = 0.08f;
            btn.colors        = c;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            tmp           = AddCenteredText(go.transform, label, 11f, FontStyles.Normal, TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return img;
        }

        // Tool button (icon-style, used inside the Modes panel)
        private static Image AddToolBtn(Transform parent, string label, string sub,
            float height, Action onClick)
        {
            var go = CreateUI($"ToolBtn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment         = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth      = true;
            vl.childControlHeight     = true;
            vl.spacing                = 0f;
            vl.padding                = new RectOffset(2, 2, 4, 4);

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var lblTmp       = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text      = label;
            lblTmp.fontSize  = 10f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color     = TEXT_PRIMARY;

            if (!string.IsNullOrEmpty(sub))
            {
                var subGo = CreateUI("Sub", go.transform);
                subGo.AddComponent<LayoutElement>().preferredHeight = 10f;
                var subTmp       = subGo.AddComponent<TextMeshProUGUI>();
                subTmp.text      = sub;
                subTmp.fontSize  = 8f;
                subTmp.alignment = TextAlignmentOptions.Center;
                subTmp.color     = TEXT_MUTED;
            }
            return img;
        }

        private static Image AddDangerToolBtn(Transform parent, string label, string sub,
            float height, Action onClick)
        {
            var img        = AddToolBtn(parent, label, sub, height, onClick);
            var dangerBase = UITheme.DANGER_IDLE;
            img.color      = dangerBase;
            var btn        = img.GetComponent<Button>();
            var c          = btn.colors;
            c.normalColor      = dangerBase;
            c.highlightedColor = new Color(0.70f, 0.20f, 0.20f, 1f);
            c.pressedColor     = UITheme.DANGER;
            btn.colors         = c;
            return img;
        }

        // Compact full-width action button (used for Undo / Redo at the bottom of Modes)
        private static void AddActionBtn(Transform parent, string label, float height, Action onClick)
        {
            var go = CreateUI($"Act_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var tmp       = AddCenteredText(go.transform, label, 9f, FontStyles.Bold, TEXT_SECONDARY);
            tmp.alignment = TextAlignmentOptions.Center;
        }

        private static void AddInlineSeparator(Transform parent)
        {
            var go = CreateUI("InlineSep", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 6f;
            var img = go.AddComponent<Image>();
            img.color = SEPARATOR;
        }

        private static void AddSectionLabel(Transform parent, string text)
        {
            var go = CreateUI($"SecLbl_{text}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 14f;
            var tmp       = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = 9f;
            tmp.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = TEXT_MUTED;
        }

        // ── MakeDrop (mirrors BuildingsEditorUIBuilder.Widgets.MakeDrop) ──────────

        private static GameObject MakeDrop(
            string name, Transform canvasT,
            PanelDock dock, float xOff, float yOff, float width, float height,
            string title, out Transform contentOut, out DraggablePanel dragOut,
            bool narrowPanel = false)
        {
            var go = CreateUI(name, canvasT);
            var r  = go.GetComponent<RectTransform>();
            ApplyDock(r, dock, xOff, yOff, width, height);

            var img           = go.AddComponent<Image>();
            img.color         = TileEditorTheme.PanelBg;
            var ol            = go.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(TileEditorTheme.OutlinePx, TileEditorTheme.OutlinePx);

            // Header
            var hdrGo          = CreateUI("PanelHeader", go.transform);
            var hdrRt          = hdrGo.GetComponent<RectTransform>();
            hdrRt.anchorMin        = new Vector2(0f, 1f);
            hdrRt.anchorMax        = new Vector2(1f, 1f);
            hdrRt.pivot            = new Vector2(0f, 1f);
            hdrRt.anchoredPosition = Vector2.zero;
            hdrRt.sizeDelta        = new Vector2(0f, PANEL_HDR_H);

            var hdrImg           = hdrGo.AddComponent<Image>();
            hdrImg.color         = TileEditorTheme.HeaderBg;
            hdrImg.raycastTarget = true;

            var hdrHlg = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.spacing                = 0f;
            hdrHlg.childForceExpandWidth  = false;
            hdrHlg.childForceExpandHeight = true;
            hdrHlg.childControlWidth      = true;
            hdrHlg.childControlHeight     = true;
            hdrHlg.childAlignment         = TextAnchor.MiddleLeft;

            TextMeshProUGUI titleTmp = null;
            if (!narrowPanel)
            {
                hdrHlg.padding = new RectOffset(8, 8, 0, 0);
                var titleGo                 = CreateUI("Title", hdrGo.transform);
                titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
                titleTmp                    = titleGo.AddComponent<TextMeshProUGUI>();
                titleTmp.text               = title.ToUpper();
                titleTmp.fontSize           = 10f;
                titleTmp.fontStyle          = FontStyles.Bold;
                titleTmp.color              = TileEditorTheme.HeaderTitle;
                titleTmp.characterSpacing   = 1.5f;
                titleTmp.alignment          = TextAlignmentOptions.Left;
                titleTmp.enableWordWrapping = false;
                titleTmp.overflowMode       = TextOverflowModes.Truncate;
                titleTmp.raycastTarget      = false;
            }

            // Separator between header and content
            var sepGo              = CreateUI("HdrSep", go.transform);
            var sepRt              = sepGo.GetComponent<RectTransform>();
            sepRt.anchorMin        = new Vector2(0f, 1f);
            sepRt.anchorMax        = new Vector2(1f, 1f);
            sepRt.pivot            = new Vector2(0f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -PANEL_HDR_H);
            sepRt.sizeDelta        = new Vector2(0f, 1f);
            var sepImg             = sepGo.AddComponent<Image>();
            sepImg.color           = TileEditorTheme.Separator;

            // Content area
            var contentGo       = CreateUI("Content", go.transform);
            var contentRt       = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.offsetMin = new Vector2(0f, 0f);
            contentRt.offsetMax = new Vector2(0f, -(PANEL_HDR_H + 1f));

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding                = new RectOffset(8, 8, 6, 6);
            layout.spacing                = 4f;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            contentGo.AddComponent<CanvasGroup>();

            // DraggablePanel
            var drag         = go.AddComponent<DraggablePanel>();
            drag.DragHeader  = hdrRt;
            drag.ContentRoot = contentGo;
            go.AddComponent<CanvasGroup>();

            // PanelChrome — participates in TileEditorTheme live-repaint
            var chrome             = go.AddComponent<PanelChrome>();
            chrome.PanelBgImage    = img;
            chrome.PanelOutline    = ol;
            chrome.HeaderBgImage   = hdrImg;
            chrome.HeaderSeparator = sepImg;
            chrome.HeaderTitle     = titleTmp;

            contentOut = contentGo.transform;
            dragOut    = drag;
            return go;
        }

        private static void ApplyDock(RectTransform r, PanelDock dock,
            float xOff, float yOff, float width, float height)
        {
            switch (dock)
            {
                case PanelDock.TopLeft:
                    r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(0f, 1f);
                    r.pivot     = new Vector2(0f, 1f);
                    r.anchoredPosition = new Vector2(xOff, -yOff);
                    break;
                case PanelDock.TopRight:
                    r.anchorMin = new Vector2(1f, 1f); r.anchorMax = new Vector2(1f, 1f);
                    r.pivot     = new Vector2(1f, 1f);
                    r.anchoredPosition = new Vector2(-xOff, -yOff);
                    break;
                case PanelDock.BottomLeft:
                    r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 0f);
                    r.pivot     = new Vector2(0f, 0f);
                    r.anchoredPosition = new Vector2(xOff, yOff);
                    break;
                case PanelDock.BottomRight:
                    r.anchorMin = new Vector2(1f, 0f); r.anchorMax = new Vector2(1f, 0f);
                    r.pivot     = new Vector2(1f, 0f);
                    r.anchoredPosition = new Vector2(-xOff, yOff);
                    break;
            }
            r.sizeDelta = new Vector2(width, height);
        }
    }
}
