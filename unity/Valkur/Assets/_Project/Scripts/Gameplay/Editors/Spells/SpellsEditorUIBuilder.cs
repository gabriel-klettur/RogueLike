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
    /// Builds the UI for the Spells Runtime Editor (F4) — PHASE 1 (UI/UX only).
    ///
    /// Mirrors the menu-bar + floating-dropdown-panel architecture established by
    /// <see cref="Valkur.Gameplay.Items.ItemsEditorUIBuilder"/> and
    /// <c>BuildingsEditorUIBuilder</c>:
    ///   • 30 px menu bar at top   — brand + Modes / Spells / Properties / Tutorial
    ///                                + flexible spacer + ? + PERF
    ///   • Modes panel       (60 px, top-left)        — Add / Remove / Reload / Undo / Redo / Save
    ///   • Spells panel      (256 px, picker grid)   — search + 4-col grid catalog
    ///   • Properties panel  (320 px, top-right)     — TabStrip [Properties | Assets/Particles]
    ///   • Tutorial panel    (~360x300)              — 6-step guided walkthrough
    ///
    /// All callbacks are wired by <see cref="SpellsRuntimeEditor"/>.
    /// </summary>
    public static class SpellsEditorUIBuilder
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

            // Spells panel
            public TMP_InputField   SearchBox;
            public RectTransform    PickerContent;
            public TextMeshProUGUI  StatusText;

            // Properties panel
            public TabStrip         PropsTabStrip;
            public PropertyForm     PropsForm;
            public RectTransform    PropsAssetsRoot;
            public Image            AssetPreviewImage;
            public TextMeshProUGUI  AssetNameTmp;

            // View panel — live preview surface
            public RawImage         ViewRawImage;
            public RectTransform    ViewPreviewArea;     // RawImage parent (used for hover detection)
            public TextMeshProUGUI  ViewSpellNameTmp;
            public TextMeshProUGUI  ViewStatusTmp;
            public Button           ViewDirNBtn;
            public Button           ViewDirSBtn;
            public Button           ViewDirEBtn;
            public Button           ViewDirWBtn;
            public Button           ViewZoomInBtn;
            public Button           ViewZoomOutBtn;

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
        // Wider than TILES_DROP_W (256) so 4 columns of 64×64 cells fit cleanly:
        // 4*64 + 3*4 spacing + 8 grid pad + 12 scrollbar + 16 outer pad = ~304 → 312 with breathing room.
        private const float SPELLS_W   = 312f;
        private const float SPELLS_H   = TILES_DROP_H;          // 564
        private const float PROPS_W    = 340f;
        private const float PROPS_H    = 560f + PANEL_HDR_H;
        private const float TUT_W      = 360f;
        private const float TUT_H      = 300f + PANEL_HDR_H;
        // View (live preview) panel — square preview surface + direction selector + zoom row + status.
        private const float VIEW_W     = 420f;
        private const float VIEW_H     = 520f + PANEL_HDR_H;

        // ── Menu button widths ────────────────────────────────────────────────────

        private const float TITLE_BTN_W    = 130f;
        private const float MODES_BTN_W    = 70f;
        private const float SPELLS_BTN_W   = 70f;
        private const float PROPS_BTN_W    = 98f;
        private const float VIEW_BTN_W     = 60f;
        private const float TUTORIAL_BTN_W = 84f;
        private const float HELP_BTN_W     = 40f;
        private const float PERF_BTN_W     = 46f;

        private const float BTN_H = 32f;   // tool button height

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
                    ? new Color(0.90f, 0.30f, 0.30f, 1f)
                    : new Color(0.55f, 0.15f, 0.15f, 1f);
            }
            else
            {
                img.color = active ? BTN_ACTIVE : BTN_NORMAL;
            }
        }

        // ── Menu Bar ──────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onPerfToggle)
        {
            var go = CreateUI("SpellsMenuBar", canvasT);
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
            brandTmp.text             = "SPELLS EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ModesMenuBtnImg    = AddMenuBtn(t, "Modes v",      MODES_BTN_W,
                () => onToggle?.Invoke("modes"),    out refs.ModesMenuBtnTmp);
            refs.SpellsMenuBtnImg   = AddMenuBtn(t, "Spells v",     SPELLS_BTN_W,
                () => onToggle?.Invoke("spells"),   out refs.SpellsMenuBtnTmp);
            refs.PropsMenuBtnImg    = AddMenuBtn(t, "Properties v", PROPS_BTN_W,
                () => onToggle?.Invoke("props"),    out refs.PropsMenuBtnTmp);
            refs.ViewMenuBtnImg     = AddMenuBtn(t, "View v",       VIEW_BTN_W,
                () => onToggle?.Invoke("view"),     out refs.ViewMenuBtnTmp);
            refs.TutorialMenuBtnImg = AddMenuBtn(t, "Tutorial v",   TUTORIAL_BTN_W,
                () => onToggle?.Invoke("tutorial"), out refs.TutorialMenuBtnTmp);

            // Flexible spacer
            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", HELP_BTN_W, () => onToggle?.Invoke("tutorial"), out _);
            AddMenuDivider(t);
            refs.PerfProbeMenuBtnImg = AddMenuBtn(t, "PERF", PERF_BTN_W,
                () => onPerfToggle?.Invoke(), out refs.PerfProbeMenuBtnTmp);
        }

        // ── Modes Panel ───────────────────────────────────────────────────────────

        private static void BuildModesPanel(Transform canvasT, ref UIRefs refs,
            Action onAdd, Action onRemove, Action onReload,
            Action onUndo, Action onRedo, Action onSave)
        {
            refs.ModesDropdown = MakeDrop("SpellsModesPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                MODES_W, MODES_H, "Modes",
                out var t, out refs.ModesPanelDrag, narrowPanel: true);

            refs.AddBtnImg    = AddToolBtn(t, "Add",  "+", BTN_H, onAdd);
            refs.RemoveBtnImg = AddDangerToolBtn(t, "Rem", "-", BTN_H, onRemove);

            AddInlineSeparator(t);
            AddSectionLabel(t, "DATA");

            refs.ReloadBtnImg = AddToolBtn(t, "Rld", "json", BTN_H, onReload);

            AddInlineSeparator(t);
            AddSectionLabel(t, "EDIT");

            refs.UndoBtnImg = AddToolBtn(t, "Undo", "Z", BTN_H, onUndo);
            refs.RedoBtnImg = AddToolBtn(t, "Redo", "Y", BTN_H, onRedo);

            AddInlineSeparator(t);
            AddSectionLabel(t, "FILE");

            refs.SaveBtnImg = AddToolBtn(t, "Save", "to disk", BTN_H, onSave);

            refs.ModesDropdown.SetActive(false);
        }

        // ── Spells Panel (picker) ─────────────────────────────────────────────────

        private static void BuildSpellsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            float xOff = PANEL_GAP + MODES_W + PANEL_GAP;
            refs.SpellsDropdown = MakeDrop("SpellsCatalogPanel", canvasT,
                PanelDock.TopLeft, xOff, PANEL_TOP_OFFSET,
                SPELLS_W, SPELLS_H, "Spells",
                out var t, out refs.SpellsPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search spells…", onSearchChanged);

            var (scroll, gridContent) = EditorUIHelpers.MakeGridPicker(
                t, "SpellsGrid", columns: 4, cellSize: 64f, spacing: 4f);
            EnsureFlexibleHeight(scroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.PickerContent = gridContent;

            refs.StatusText      = EditorUIHelpers.MakeStatusText(t);
            refs.StatusText.text = "Phase 1 — UI scaffolding";

            refs.SpellsDropdown.SetActive(false);
        }

        // ── Properties Panel (TabStrip) ───────────────────────────────────────────

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.PropsDropdown = MakeDrop("SpellsPropertiesPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties",
                out var t, out refs.PropsPanelDrag);

            // ── TabStrip (Properties | Assets/Particles) ──
            // Build the two tab-content containers FIRST so AddTab can hide them.
            var tab1 = CreateUI("PropsTab", t);
            var tab1Le = tab1.AddComponent<LayoutElement>();
            tab1Le.flexibleHeight = 1f;
            var tab1Vlg = tab1.AddComponent<VerticalLayoutGroup>();
            tab1Vlg.childForceExpandWidth = true;
            tab1Vlg.childForceExpandHeight = false;
            tab1Vlg.childControlWidth = true; tab1Vlg.childControlHeight = true;
            tab1Vlg.spacing = 2f; tab1Vlg.padding = new RectOffset(0, 0, 0, 0);

            var (pScroll, pContent) = EditorUIHelpers.MakeScrollView(tab1.transform, "PropsScroll");
            EnsureFlexibleHeight(pScroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(pScroll);
            refs.PropsForm = PropertyForm.Create(pContent, "PropsForm");

            var tab2 = CreateUI("AssetsTab", t);
            var tab2Le = tab2.AddComponent<LayoutElement>();
            tab2Le.flexibleHeight = 1f;
            var tab2Vlg = tab2.AddComponent<VerticalLayoutGroup>();
            tab2Vlg.childForceExpandWidth = true;
            tab2Vlg.childForceExpandHeight = false;
            tab2Vlg.childControlWidth = true; tab2Vlg.childControlHeight = true;
            tab2Vlg.spacing = 6f; tab2Vlg.padding = new RectOffset(8, 8, 8, 8);

            // Move tabs to their proper sibling order: TabStrip first, content after.
            tab1.transform.SetAsLastSibling();
            tab2.transform.SetAsLastSibling();

            refs.PropsAssetsRoot = (RectTransform)tab2.transform;

            // Asset preview (square)
            var previewWrap = CreateUI("PreviewWrap", tab2.transform);
            previewWrap.AddComponent<LayoutElement>().preferredHeight = 180f;
            var previewLayout = previewWrap.AddComponent<HorizontalLayoutGroup>();
            previewLayout.childAlignment = TextAnchor.MiddleCenter;
            previewLayout.childForceExpandWidth = false;
            previewLayout.childForceExpandHeight = false;
            previewLayout.childControlWidth = true; previewLayout.childControlHeight = true;

            var previewGo = CreateUI("Preview", previewWrap.transform);
            var previewLe = previewGo.AddComponent<LayoutElement>();
            previewLe.preferredWidth = 180f; previewLe.preferredHeight = 180f;
            var previewBg = previewGo.AddComponent<Image>();
            previewBg.color = EditorUIHelpers.BG_SURFACE;
            var iconGo = CreateUI("Icon", previewGo.transform);
            EditorUIHelpers.StretchFill(iconGo);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.color = Color.white;
            iconImg.enabled = false;
            refs.AssetPreviewImage = iconImg;

            // Asset name label
            var nameGo = CreateUI("AssetName", tab2.transform);
            nameGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text      = "(no spell selected)";
            nameTmp.fontSize  = 13f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color     = ACCENT;
            refs.AssetNameTmp = nameTmp;

            // Phase 2 placeholder
            var hintGo = CreateUI("AssetHint", tab2.transform);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 40f;
            var hintTmp = hintGo.AddComponent<TextMeshProUGUI>();
            hintTmp.text      = "Asset picker — phase 2";
            hintTmp.fontSize  = 11f;
            hintTmp.fontStyle = FontStyles.Italic;
            hintTmp.alignment = TextAlignmentOptions.Center;
            hintTmp.color     = TEXT_MUTED;
            hintTmp.enableWordWrapping = true;

            // Now build the TabStrip last and reorder it to the top.
            var tabs = TabStrip.Create(t, "PropsTabs");
            tabs.transform.SetSiblingIndex(0);
            tabs.AddTab("props",  "Properties",        tab1);
            tabs.AddTab("assets", "Assets / Particles", tab2);
            refs.PropsTabStrip = tabs;

            refs.PropsDropdown.SetActive(false);
        }

        // ── View Panel (live preview) ─────────────────────────────────────────────
        // Floating, draggable panel anchored at top-right initially but re-centered
        // on first open by SpellsRuntimeEditor.Preview. Hosts a square RawImage that
        // displays the off-screen RenderTexture rendered by SpellPreviewService, plus
        // a 4-direction selector (N/W/E/S) and a status line.

        private static void BuildViewPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.ViewDropdown = MakeDrop("SpellsViewPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                VIEW_W, VIEW_H, "View",
                out var t, out refs.ViewPanelDrag);

            // Re-anchor to canvas center (the panel is a free-floating, draggable
            // window per the editor design). DraggablePanel offsets in anchor-space
            // so a center anchor is just as draggable as the original TopLeft one.
            var rt = (RectTransform)refs.ViewDropdown.transform;
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(VIEW_W, VIEW_H);

            // Spell name header (matches Properties Asset Name styling).
            var nameGo = CreateUI("SpellName", t);
            nameGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text      = "(no spell selected)";
            nameTmp.fontSize  = 13f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color     = ACCENT;
            refs.ViewSpellNameTmp = nameTmp;

            // Square preview surface — RawImage bound to the SpellPreviewService RT.
            var previewWrap = CreateUI("PreviewWrap", t);
            previewWrap.AddComponent<LayoutElement>().preferredHeight = 384f;
            var previewLayout = previewWrap.AddComponent<HorizontalLayoutGroup>();
            previewLayout.childAlignment        = TextAnchor.MiddleCenter;
            previewLayout.childForceExpandWidth  = false;
            previewLayout.childForceExpandHeight = false;
            previewLayout.childControlWidth      = true;
            previewLayout.childControlHeight     = true;

            var previewGo = CreateUI("Preview", previewWrap.transform);
            var previewLe = previewGo.AddComponent<LayoutElement>();
            previewLe.preferredWidth  = 384f;
            previewLe.preferredHeight = 384f;
            refs.ViewPreviewArea = (RectTransform)previewGo.transform;

            // Background + raycast target ON so the area receives pointer-enter / exit
            // events (the runtime editor adds a hover-probe component to it that drives
            // mouse-wheel zoom).
            var bg           = previewGo.AddComponent<Image>();
            bg.color         = EditorUIHelpers.BG_SURFACE;
            bg.raycastTarget = true;

            var rawGo = CreateUI("RT", previewGo.transform);
            EditorUIHelpers.StretchFill(rawGo);
            var raw           = rawGo.AddComponent<RawImage>();
            raw.color         = Color.white;
            raw.raycastTarget = false;
            refs.ViewRawImage = raw;

            // Direction selector — 4 buttons in a single row [N | W | E | S].
            var dirRow = CreateUI("DirRow", t);
            dirRow.AddComponent<LayoutElement>().preferredHeight = 32f;
            var dirHlg = dirRow.AddComponent<HorizontalLayoutGroup>();
            dirHlg.spacing                = 6f;
            dirHlg.childForceExpandWidth  = true;
            dirHlg.childForceExpandHeight = true;
            dirHlg.childControlWidth      = true;
            dirHlg.childControlHeight     = true;

            refs.ViewDirNBtn = EditorUIHelpers.MakeButton(dirRow.transform, "N", null, 28f, 11f);
            refs.ViewDirWBtn = EditorUIHelpers.MakeButton(dirRow.transform, "W", null, 28f, 11f);
            refs.ViewDirEBtn = EditorUIHelpers.MakeButton(dirRow.transform, "E", null, 28f, 11f);
            refs.ViewDirSBtn = EditorUIHelpers.MakeButton(dirRow.transform, "S", null, 28f, 11f);

            // Zoom row — [-]  [+]  + tooltip-ish label between them.
            var zoomRow = CreateUI("ZoomRow", t);
            zoomRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var zoomHlg = zoomRow.AddComponent<HorizontalLayoutGroup>();
            zoomHlg.spacing                = 6f;
            zoomHlg.childForceExpandWidth  = true;
            zoomHlg.childForceExpandHeight = true;
            zoomHlg.childControlWidth      = true;
            zoomHlg.childControlHeight     = true;

            refs.ViewZoomOutBtn = EditorUIHelpers.MakeButton(zoomRow.transform, "-",   null, 26f, 14f);
            // Inert "Zoom" label between the two buttons so the row reads as a control,
            // not just two random glyphs.
            var zoomLblGo = CreateUI("ZoomLbl", zoomRow.transform);
            zoomLblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var zoomLbl       = zoomLblGo.AddComponent<TextMeshProUGUI>();
            zoomLbl.text      = "ZOOM  (mouse wheel over preview)";
            zoomLbl.fontSize  = 10f;
            zoomLbl.alignment = TextAlignmentOptions.Center;
            zoomLbl.color     = TEXT_MUTED;
            refs.ViewZoomInBtn  = EditorUIHelpers.MakeButton(zoomRow.transform, "+",   null, 26f, 14f);

            // Status line.
            var statusGo = CreateUI("ViewStatus", t);
            statusGo.AddComponent<LayoutElement>().preferredHeight = 20f;
            var statusTmp = statusGo.AddComponent<TextMeshProUGUI>();
            statusTmp.text      = "idle";
            statusTmp.fontSize  = 11f;
            statusTmp.fontStyle = FontStyles.Italic;
            statusTmp.alignment = TextAlignmentOptions.Center;
            statusTmp.color     = TEXT_MUTED;
            refs.ViewStatusTmp = statusTmp;

            refs.ViewDropdown.SetActive(false);
        }

        // ── Tutorial Panel ────────────────────────────────────────────────────────

        private static void BuildTutorialPanel(Transform canvasT, ref UIRefs refs,
            Action onPrev, Action onNext, Action onClose)
        {
            // Offset away from the spells-picker column so it doesn't overlap.
            float xOff = PANEL_GAP + MODES_W + PANEL_GAP + SPELLS_W + PANEL_GAP;
            refs.TutorialDropdown = MakeDrop("SpellsTutorialPanel", canvasT,
                PanelDock.TopLeft, xOff, PANEL_TOP_OFFSET + 80f,
                TUT_W, TUT_H, "Tutorial",
                out var t, out refs.TutorialPanelDrag);

            // Step header
            var stepGo = CreateUI("Step", t);
            stepGo.AddComponent<LayoutElement>().preferredHeight = 24f;
            var stepTmp = stepGo.AddComponent<TextMeshProUGUI>();
            stepTmp.fontSize  = 13f;
            stepTmp.fontStyle = FontStyles.Bold;
            stepTmp.alignment = TextAlignmentOptions.Left;
            stepTmp.color     = ACCENT;
            refs.TutorialStepLabel = stepTmp;

            // Body (flexible)
            var bodyGo = CreateUI("Body", t);
            bodyGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var bodyTmp = bodyGo.AddComponent<TextMeshProUGUI>();
            bodyTmp.fontSize           = 12f;
            bodyTmp.color              = TEXT_PRIMARY;
            bodyTmp.alignment          = TextAlignmentOptions.TopLeft;
            bodyTmp.enableWordWrapping = true;
            refs.TutorialBodyTmp = bodyTmp;

            // Nav row
            var nav = CreateUI("Nav", t);
            nav.AddComponent<LayoutElement>().preferredHeight = 30f;
            var navHlg = nav.AddComponent<HorizontalLayoutGroup>();
            navHlg.spacing = 6f;
            navHlg.childForceExpandWidth = true;
            navHlg.childControlWidth = true; navHlg.childControlHeight = true;

            refs.TutorialPrevBtn  = EditorUIHelpers.MakeButton(nav.transform, "<= Prev", () => onPrev?.Invoke(), 28f, 11f);
            refs.TutorialNextBtn  = EditorUIHelpers.MakeButton(nav.transform, "Next =>", () => onNext?.Invoke(), 28f, 11f);
            refs.TutorialCloseBtn = EditorUIHelpers.MakeButton(nav.transform, "Close",  () => onClose?.Invoke(), 28f, 11f);

            refs.TutorialDropdown.SetActive(false);
        }

        // ── Internal helpers ──────────────────────────────────────────────────────

        private static void EnsureFlexibleHeight(GameObject go, float flex = 1f)
        {
            if (go == null) return;
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleHeight = flex;
        }

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
            var dangerBase = new Color(0.55f, 0.15f, 0.15f, 1f);
            img.color      = dangerBase;
            var btn        = img.GetComponent<Button>();
            var c          = btn.colors;
            c.normalColor      = dangerBase;
            c.highlightedColor = new Color(0.70f, 0.20f, 0.20f, 1f);
            c.pressedColor     = new Color(0.90f, 0.30f, 0.30f, 1f);
            btn.colors         = c;
            return img;
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

        // ── MakeDrop (mirrors ItemsEditorUIBuilder.MakeDrop) ──────────────────────

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

            // Header / content separator
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

            var drag         = go.AddComponent<DraggablePanel>();
            drag.DragHeader  = hdrRt;
            drag.ContentRoot = contentGo;
            go.AddComponent<CanvasGroup>();

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
