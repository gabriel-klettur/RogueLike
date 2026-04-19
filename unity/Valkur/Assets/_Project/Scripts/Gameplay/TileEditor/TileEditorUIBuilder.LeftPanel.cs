using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    public static partial class TileEditorUIBuilder
    {
        // ═════════════════════════════════════════════════════════════════
        //  Panel dock layout (top-row Tools + Tiles, top-right Inspector, bottom-right Layers)
        // ═════════════════════════════════════════════════════════════════
        // Tools sits at top-left, just below the menu bar.
        // Tiles sits immediately to the right of Tools (same vertical row).
        // Inspector sits at top-right, same vertical row as Tools/Tiles.
        // Layers sits at the bottom-right corner.
        private static float ToolsX     => PANEL_GAP;
        private static float ToolsY     => PANEL_TOP_OFFSET;
        private static float TilesX     => PANEL_GAP + TOOLS_DROP_W + PANEL_GAP;
        private static float TilesY     => PANEL_TOP_OFFSET;
        private static float InspectorX => PANEL_GAP;   // from right edge
        private static float InspectorY => PANEL_TOP_OFFSET;
        private static float LayersX    => PANEL_GAP;   // from right edge
        private static float LayersY    => PANEL_GAP;   // from bottom edge

        // ═════════════════════════════════════════════════════════════════
        //  TOOLS DROPDOWN
        // ═════════════════════════════════════════════════════════════════

        private static void BuildToolsDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<int> onBrushSizeChanged,
            System.Action onUndo = null,
            System.Action onRedo = null,
            System.Action onSave = null)
        {
            refs.ToolsDropdown = MakeDropdownPanel("ToolsDropdown", canvasT,
                PanelDock.TopLeft, ToolsX, ToolsY, TOOLS_DROP_W, TOOLS_DROP_H,
                "Tools", out var toolsContent, out refs.ToolsPanelDrag,
                narrowPanel: true);   // 60px wide — header shows only control buttons; title goes in content

            var t = toolsContent;
            BuildSectionLabel(t, "TOOLS");  // panel title as first content row (below control buttons)

            // Single-column icon toolbar — inner width (60-8-8=44) = BTN_H → square
            const float BTN_H = 44f;
            CreateToolBtn(t, "Select",  "S",      TileEditorState.Tool.Select,      state, ref refs, onToolChanged, BTN_H);
            CreateToolBtn(t, "Brush",   "B",      TileEditorState.Tool.Brush,       state, ref refs, onToolChanged, BTN_H);
            CreateToolBtn(t, "Erase",   "E",      TileEditorState.Tool.Eraser,      state, ref refs, onToolChanged, BTN_H);
            CreateToolBtn(t, "Fill",    "F",      TileEditorState.Tool.Fill,        state, ref refs, onToolChanged, BTN_H);
            CreateToolBtn(t, "Pick",    "I",      TileEditorState.Tool.Eyedropper,  state, ref refs, onToolChanged, BTN_H);

            BuildSeparator(t);

            CreateActionBtn(t, "Undo", "Ctrl+Z",       BTN_H, onUndo);
            CreateActionBtn(t, "Redo", "Ctrl+Shift+Z", BTN_H, onRedo);

            BuildSeparator(t);

            // Save button (writes dirty zones to disk)
            CreateSaveBtn(t, BTN_H, onSave, ref refs);

            refs.ToolsDropdown.SetActive(false);
        }

        /// <summary>Compact Save button + dirty-zone counter shown beneath it.</summary>
        private static void CreateSaveBtn(Transform parent, float height, System.Action onClick, ref UIRefs refs)
        {
            var go = CreateUI("Action_Save", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            refs.SaveButtonImg = img;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.spacing = 1f;
            vl.padding = new RectOffset(2, 2, 4, 4);

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            refs.SaveButtonLabel = lblGo.AddComponent<TextMeshProUGUI>();
            refs.SaveButtonLabel.text = "Save";
            refs.SaveButtonLabel.fontSize = 9f;
            refs.SaveButtonLabel.fontStyle = FontStyles.Bold;
            refs.SaveButtonLabel.alignment = TextAlignmentOptions.Center;
            refs.SaveButtonLabel.color = TEXT_SECONDARY;

            var keyGo = CreateUI("Key", go.transform);
            keyGo.AddComponent<LayoutElement>().preferredHeight = 11f;
            var keyTmp = keyGo.AddComponent<TextMeshProUGUI>();
            keyTmp.text = "Ctrl+S";
            keyTmp.fontSize = 7f;
            keyTmp.alignment = TextAlignmentOptions.Center;
            keyTmp.color = TEXT_MUTED;

            // Dirty zone counter beneath the button
            var dirtyGo = CreateUI("DirtyIndicator", parent);
            dirtyGo.AddComponent<LayoutElement>().preferredHeight = 12f;
            refs.DirtyIndicatorText = dirtyGo.AddComponent<TextMeshProUGUI>();
            refs.DirtyIndicatorText.text = string.Empty;
            refs.DirtyIndicatorText.fontSize = 7f;
            refs.DirtyIndicatorText.alignment = TextAlignmentOptions.Center;
            refs.DirtyIndicatorText.color = TEXT_MUTED;
        }

        private static void CreateActionBtn(Transform parent, string label, string shortcut,
            float height, System.Action onClick)
        {
            var go = CreateUI($"Action_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.spacing = 1f;
            vl.padding = new RectOffset(2, 2, 4, 4);

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label;
            lblTmp.fontSize = 9f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color = TEXT_SECONDARY;

            var keyGo = CreateUI("Key", go.transform);
            keyGo.AddComponent<LayoutElement>().preferredHeight = 11f;
            var keyTmp = keyGo.AddComponent<TextMeshProUGUI>();
            keyTmp.text = shortcut;
            keyTmp.fontSize = 7f;
            keyTmp.alignment = TextAlignmentOptions.Center;
            keyTmp.color = TEXT_MUTED;
        }

        private static void CreateToolBtn(Transform parent, string label, string shortcut,
            TileEditorState.Tool tool, TileEditorState state, ref UIRefs refs,
            System.Action<TileEditorState.Tool> onToolChanged, float height = 44f)
        {
            var go = CreateUI($"Tool_{tool}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            bool active = tool == state.CurrentTool;
            img.color = active ? BTN_ACTIVE : BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            var cap = tool;
            btn.onClick.AddListener(() => onToolChanged?.Invoke(cap));

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.spacing = 1f;
            vl.padding = new RectOffset(2, 2, 4, 4);

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label;
            lblTmp.fontSize = 9f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color = active ? ACCENT : TEXT_SECONDARY;

            var keyGo = CreateUI("Key", go.transform);
            keyGo.AddComponent<LayoutElement>().preferredHeight = 11f;
            var keyTmp = keyGo.AddComponent<TextMeshProUGUI>();
            keyTmp.text = shortcut;
            keyTmp.fontSize = 7f;
            keyTmp.alignment = TextAlignmentOptions.Center;
            keyTmp.color = TEXT_MUTED;

            refs.ToolButtonImages[tool] = img;
            refs.ToolButtonTexts[tool] = lblTmp;
        }

        // ═════════════════════════════════════════════════════════════════
        //  TILES DROPDOWN (categories + tile grid + selected preview)
        // ═════════════════════════════════════════════════════════════════

        private static void BuildTilesDropdown(Transform canvasT, ref UIRefs refs)
        {
            refs.TilesDropdown = MakeDropdownPanel("TilesDropdown", canvasT,
                PanelDock.TopLeft, TilesX, TilesY, TILES_DROP_W, TILES_DROP_H,
                "Tiles", out var tilesContent, out refs.TilesPanelDrag);

            var t = tilesContent;

            // Selected tile preview row
            BuildSelectedTilePreview(t, ref refs);
            BuildSeparator(t);

            // Categories
            BuildSectionLabel(t, "CATEGORIES");
            BuildCategoryScroll(t, ref refs);
            BuildSeparator(t);

            // Tile grid
            BuildSectionLabel(t, "TILES");
            BuildTilePicker(t, ref refs);
            BuildTileCountRow(t, ref refs);

            refs.TilesDropdown.SetActive(false);
        }

        private static void BuildSelectedTilePreview(Transform parent, ref UIRefs refs)
        {
            var row = CreateUI("SelectedPreview", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 48f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 4, 4);

            var imgGo = CreateUI("Img", row.transform);
            imgGo.AddComponent<LayoutElement>().preferredWidth = 40f;
            refs.SelectedTilePreviewImg = imgGo.AddComponent<Image>();
            refs.SelectedTilePreviewImg.color = SLOT_BG;
            refs.SelectedTilePreviewImg.preserveAspect = true;
            var outline = imgGo.AddComponent<Outline>();
            outline.effectColor = ACCENT;
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            var infoGo = CreateUI("Info", row.transform);
            infoGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vl = infoGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 1f;
            vl.childForceExpandHeight = false;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childControlWidth = true;

            var labelGo = CreateUI("Lbl", infoGo.transform);
            labelGo.AddComponent<LayoutElement>().preferredHeight = 12f;
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = "SELECTED";
            labelTmp.fontSize = 8f;
            labelTmp.color = TEXT_MUTED;
            labelTmp.characterSpacing = 2f;

            var nameGo = CreateUI("Name", infoGo.transform);
            nameGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            refs.SelectedTileNameText = nameGo.AddComponent<TextMeshProUGUI>();
            refs.SelectedTileNameText.text = "(none)";
            refs.SelectedTileNameText.fontSize = 12f;
            refs.SelectedTileNameText.alignment = TextAlignmentOptions.Left;
            refs.SelectedTileNameText.color = TEXT_PRIMARY;
            refs.SelectedTileNameText.enableWordWrapping = true;
        }

        private static void BuildCategoryScroll(Transform parent, ref UIRefs refs)
        {
            var scrollGo = CreateUI("CatScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.preferredHeight = 110f;
            le.minHeight = 60f;
            // Background panel
            var bg = scrollGo.AddComponent<Image>();
            bg.color = BG_SURFACE;
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 18f;
            sr.movementType = ScrollRect.MovementType.Clamped;

            // Viewport reserves space on the right for the scrollbar
            var vp = CreateUI("VP", scrollGo.transform);
            var vpRt = vp.GetComponent<RectTransform>();
            vpRt.anchorMin = new Vector2(0f, 0f);
            vpRt.anchorMax = new Vector2(1f, 1f);
            vpRt.pivot = new Vector2(0f, 1f);
            vpRt.offsetMin = new Vector2(0f, 0f);
            vpRt.offsetMax = new Vector2(-TILES_SCROLLBAR_W, 0f);
            vp.AddComponent<RectMask2D>();

            var content = CreateUI("Content", vp.transform);
            refs.CategoryTabsContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0f, 1f);
            cr.sizeDelta = Vector2.zero;

            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(TILES_ROW_WIDTH, 22f);
            gl.spacing = new Vector2(3f, 2f);
            gl.padding = new RectOffset(3, 3, 2, 2);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 1;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Scrollbar (always visible) — same look as the tile picker
            BuildVerticalScrollbar(scrollGo.transform, sr);

            sr.content = cr;
            sr.viewport = vpRt;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private static void BuildTilePicker(Transform parent, ref UIRefs refs)
        {
            var scrollGo = CreateUI("TileScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 200f;
            // Background panel so the empty area reads as a defined picker surface
            var bg = scrollGo.AddComponent<Image>();
            bg.color = BG_SURFACE;
            refs.TileScrollRect = scrollGo.AddComponent<ScrollRect>();
            refs.TileScrollRect.horizontal = false;
            refs.TileScrollRect.vertical = true;
            refs.TileScrollRect.scrollSensitivity = 24f;
            refs.TileScrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Viewport: leave room on the right for the vertical scrollbar
            var vp = CreateUI("VP", scrollGo.transform);
            var vpRt = vp.GetComponent<RectTransform>();
            vpRt.anchorMin = new Vector2(0f, 0f);
            vpRt.anchorMax = new Vector2(1f, 1f);
            vpRt.pivot = new Vector2(0f, 1f);
            vpRt.offsetMin = new Vector2(0f, 0f);
            vpRt.offsetMax = new Vector2(-TILES_SCROLLBAR_W, 0f);
            vp.AddComponent<RectMask2D>();

            // Content grid: 4 columns of square cells
            var content = CreateUI("Content", vp.transform);
            refs.TileGridContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0f, 1f);
            cr.sizeDelta = Vector2.zero;
            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(TILES_CELL_SIZE, TILES_CELL_SIZE);
            gl.spacing = new Vector2(TILES_GRID_SPACING, TILES_GRID_SPACING);
            gl.padding = new RectOffset(4, 4, 4, 4);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = TILES_GRID_COLS;
            gl.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gl.startAxis = GridLayoutGroup.Axis.Horizontal;
            gl.childAlignment = TextAnchor.UpperLeft;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Vertical scrollbar (always visible for navigability)
            BuildVerticalScrollbar(scrollGo.transform, refs.TileScrollRect);

            refs.TileScrollRect.content = cr;
            refs.TileScrollRect.viewport = vpRt;
            refs.TileScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        /// <summary>Builds a thin, always-visible vertical scrollbar pinned to the right edge of the parent
        /// scroll container and wires it into the supplied ScrollRect. Visual style matches the editor accent palette.</summary>
        private static void BuildVerticalScrollbar(Transform scrollContainer, ScrollRect targetScrollRect)
        {
            var sbGo = CreateUI("VScrollbar", scrollContainer);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 1f);
            sbRt.sizeDelta = new Vector2(TILES_SCROLLBAR_W, 0f);
            sbRt.anchoredPosition = Vector2.zero;
            var sbBg = sbGo.AddComponent<Image>();
            sbBg.color = new Color(0.08f, 0.08f, 0.10f, 0.85f);
            var scrollbar = sbGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slidingArea = CreateUI("SlidingArea", sbGo.transform);
            var saRt = slidingArea.GetComponent<RectTransform>();
            saRt.anchorMin = Vector2.zero;
            saRt.anchorMax = Vector2.one;
            saRt.offsetMin = new Vector2(2f, 2f);
            saRt.offsetMax = new Vector2(-2f, -2f);

            var handleGo = CreateUI("Handle", slidingArea.transform);
            var hRt = handleGo.GetComponent<RectTransform>();
            hRt.anchorMin = Vector2.zero;
            hRt.anchorMax = Vector2.one;
            hRt.offsetMin = Vector2.zero;
            hRt.offsetMax = Vector2.zero;
            var hImg = handleGo.AddComponent<Image>();
            hImg.color = new Color(0.55f, 0.45f, 0.22f, 0.85f);
            scrollbar.targetGraphic = hImg;
            scrollbar.handleRect = hRt;
            var sbColors = scrollbar.colors;
            sbColors.normalColor = new Color(0.55f, 0.45f, 0.22f, 0.85f);
            sbColors.highlightedColor = new Color(0.75f, 0.62f, 0.30f, 0.95f);
            sbColors.pressedColor = new Color(0.90f, 0.76f, 0.38f, 1f);
            scrollbar.colors = sbColors;

            targetScrollRect.verticalScrollbar = scrollbar;
        }

        private static void BuildTileCountRow(Transform parent, ref UIRefs refs)
        {
            var go = CreateUI("TileCount", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 14f;
            refs.TileCountText = go.AddComponent<TextMeshProUGUI>();
            refs.TileCountText.text = "";
            refs.TileCountText.fontSize = 9f;
            refs.TileCountText.alignment = TextAlignmentOptions.Right;
            refs.TileCountText.color = TEXT_MUTED;
        }

        // ═════════════════════════════════════════════════════════════════
        //  SHARED: Dropdown panel factory
        // ═════════════════════════════════════════════════════════════════

        private static GameObject MakeDropdownPanel(string name, Transform canvasT,
            PanelDock dock, float xOffset, float yOffset, float width, float height,
            string title, out Transform contentTransform, out DraggablePanel draggable,
            bool narrowPanel = false)
        {
            // ── Root ─────────────────────────────────────────────────────────
            var go = CreateUI(name, canvasT);
            var r  = go.GetComponent<RectTransform>();
            ApplyDock(r, dock, xOffset, yOffset, width, height);

            var img = go.AddComponent<Image>();
            img.color = TileEditorTheme.PanelBg;          // semi-transparent dark — matches PERF PROBE
            var ol = go.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(TileEditorTheme.OutlinePx, TileEditorTheme.OutlinePx);

            // ── Panel header (drag handle + title + controls) ─────────────────
            var hdrGo  = CreateUI("PanelHeader", go.transform);
            var hdrRt  = hdrGo.GetComponent<RectTransform>();
            hdrRt.anchorMin        = new Vector2(0f, 1f);
            hdrRt.anchorMax        = new Vector2(1f, 1f);
            hdrRt.pivot            = new Vector2(0f, 1f);
            hdrRt.anchoredPosition = Vector2.zero;
            hdrRt.sizeDelta        = new Vector2(0f, PANEL_HDR_H);

            var hdrImg = hdrGo.AddComponent<Image>();
            hdrImg.color         = TileEditorTheme.HeaderBg;
            hdrImg.raycastTarget = true;

            var hdrHlg = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.spacing            = 0f;
            hdrHlg.childForceExpandWidth  = false;
            hdrHlg.childForceExpandHeight = true;
            hdrHlg.childControlWidth      = true;
            hdrHlg.childControlHeight     = true;
            hdrHlg.childAlignment         = TextAnchor.MiddleLeft;

            // Header button width: narrow panels (e.g. TOOLS 60px) use 14px buttons to fit
            float hdrBtnW = narrowPanel ? 14f : PANEL_HDR_BTN_W;

            TextMeshProUGUI titleTmp = null;
            if (!narrowPanel)
            {
                // Title text on the left, buttons on the right
                hdrHlg.padding = new RectOffset(8, 2, 0, 0);
                var titleGo  = CreateUI("Title", hdrGo.transform);
                titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
                titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
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
            else
            {
                // Narrow panel: flexible spacer pushes the 3 buttons to the right
                hdrHlg.padding = new RectOffset(2, 2, 0, 0);
                var spacer = CreateUI("Spacer", hdrGo.transform);
                spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }

            // Separator line between header and content
            var sepGo = CreateUI("HdrSep", go.transform);
            var sepRt = sepGo.GetComponent<RectTransform>();
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot     = new Vector2(0f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -PANEL_HDR_H);
            sepRt.sizeDelta = new Vector2(0f, 1f);
            var sepImg = sepGo.AddComponent<Image>();
            sepImg.color = TileEditorTheme.Separator;

            // ── Content area ──────────────────────────────────────────────────
            var contentGo = CreateUI("Content", go.transform);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.offsetMin = new Vector2(0f, 0f);
            contentRt.offsetMax = new Vector2(0f, -(PANEL_HDR_H + 1f));

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding            = new RectOffset(8, 8, 6, 6);
            layout.spacing            = 4f;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;

            contentGo.AddComponent<CanvasGroup>();

            // ── DraggablePanel + header control buttons ───────────────────────
            var drag = go.AddComponent<DraggablePanel>();
            drag.DragHeader  = hdrRt;
            drag.ContentRoot = contentGo;

            // Normal bg: subtle dark chip so buttons are visible even un-hovered
            var normalBg  = new Color(0.18f, 0.18f, 0.24f, 0.75f);
            var closeBgN  = new Color(0.28f, 0.07f, 0.07f, 0.60f);
            BuildHdrBtn(hdrGo.transform, "MinBtn",   "—", normalBg,  PANEL_HDR_BTN_HOVER,    () => drag.Minimize(),   hdrBtnW);
            BuildHdrBtn(hdrGo.transform, "MaxBtn",   "□", normalBg,  PANEL_HDR_BTN_HOVER,    () => drag.Maximize(),   hdrBtnW);
            BuildHdrBtn(hdrGo.transform, "CloseBtn", "×", closeBgN,  PANEL_HDR_CLOSE_HOVER,  () => drag.ClosePanel(), hdrBtnW);

            go.AddComponent<CanvasGroup>();

            // ── Theme tracker ─ lets the UX panel repaint this panel live ──────────
            var chrome = go.AddComponent<PanelChrome>();
            chrome.PanelBgImage    = img;
            chrome.PanelOutline    = ol;
            chrome.HeaderBgImage   = hdrImg;
            chrome.HeaderSeparator = sepImg;
            chrome.HeaderTitle     = titleTmp;

            contentTransform = contentGo.transform;
            draggable        = drag;
            return go;
        }

        /// <summary>Builds a small icon button inside the panel header.</summary>
        private static void BuildHdrBtn(Transform parent, string goName,
            string icon, Color normalBg, Color hoverColor, System.Action onClick,
            float btnWidth = PANEL_HDR_BTN_W)
        {
            var go = CreateUI(goName, parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = btnWidth;
            le.minWidth       = btnWidth;

            var bgImg = go.AddComponent<Image>();
            bgImg.color         = normalBg;
            bgImg.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            var bc  = btn.colors;
            bc.normalColor      = normalBg;
            bc.highlightedColor = hoverColor;
            bc.pressedColor     = hoverColor;
            bc.fadeDuration     = 0.08f;
            btn.colors          = bc;
            btn.targetGraphic   = bgImg;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txtGo = CreateUI("Txt", go.transform);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text          = icon;
            tmp.fontSize      = btnWidth <= 16f ? 9f : 11f;
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.color         = TEXT_SECONDARY;   // clearly visible, not just TEXT_MUTED
            tmp.raycastTarget = false;
        }

        /// <summary>
        /// Applies anchor/pivot/position for a docked panel based on the chosen corner.
        /// xOffset and yOffset are always positive pixel distances from the anchor corner
        /// (e.g. for TopRight, xOffset is pixels left from the right edge, yOffset is pixels down from the top).
        /// </summary>
        private static void ApplyDock(RectTransform r, PanelDock dock,
            float xOffset, float yOffset, float width, float height)
        {
            switch (dock)
            {
                case PanelDock.TopLeft:
                    r.anchorMin = new Vector2(0f, 1f);
                    r.anchorMax = new Vector2(0f, 1f);
                    r.pivot     = new Vector2(0f, 1f);
                    r.anchoredPosition = new Vector2(xOffset, -yOffset);
                    break;
                case PanelDock.TopRight:
                    r.anchorMin = new Vector2(1f, 1f);
                    r.anchorMax = new Vector2(1f, 1f);
                    r.pivot     = new Vector2(1f, 1f);
                    r.anchoredPosition = new Vector2(-xOffset, -yOffset);
                    break;
                case PanelDock.BottomLeft:
                    r.anchorMin = new Vector2(0f, 0f);
                    r.anchorMax = new Vector2(0f, 0f);
                    r.pivot     = new Vector2(0f, 0f);
                    r.anchoredPosition = new Vector2(xOffset, yOffset);
                    break;
                case PanelDock.BottomRight:
                    r.anchorMin = new Vector2(1f, 0f);
                    r.anchorMax = new Vector2(1f, 0f);
                    r.pivot     = new Vector2(1f, 0f);
                    r.anchoredPosition = new Vector2(-xOffset, yOffset);
                    break;
            }
            r.sizeDelta = new Vector2(width, height);
        }
    }
}
