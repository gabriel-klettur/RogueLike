using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.VFX
{
    public static partial class ParticlesEditorUIBuilder
    {
        /// <summary>Key of the "All" tab — the one category that filters nothing.</summary>
        internal const string CATEGORY_ALL_KEY = "__all";

        // ── Presets Panel (Grid / Table) ──────────────────────────────────────────
        // Mirrors SpellsEditorUIBuilder.SpellsPanel.cs 1:1 in structure.
        //
        // Layout top-to-bottom inside the panel content VLG:
        //   1. Tab strip (26 px) — "Grid" | "Table"
        //   2. Search box (26 px)
        //   3a. Grid container (flex): responsive grid only
        //   3b. Table container (flex): sticky header + scrollable rows
        //   4. Status label (20 px)

        private const float PARTICLE_TABLE_HEADER_STRIP_H = 24f;
        private const float PARTICLE_TABLE_SB_W           = 12f;

        private static void BuildPresetsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged,
            Action<string> onCategoryChanged = null)
        {
            float x = PANEL_GAP + TOOLS_W + PANEL_GAP;
            refs.PresetsDropdown = MakeDrop("ParticlesPresetsPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                PRESETS_W, PRESETS_H, "Presets",
                out var t, out refs.PresetsPanelDrag);

            // ── 1. Tab strip ──────────────────────────────────────────────────
            var tabStrip = TabStrip.Create(t, "PresetsViewTabStrip", height: 26f);
            refs.PresetsTabStrip = tabStrip;

            // ── 2. Category tab strip ─────────────────────────────────────────
            // Two thirds of the catalog is spell internals — four projectile stacks and
            // twenty portal variants — which is a lot to scroll past when placing a torch.
            // These tabs carry no content of their own; they only narrow the list, so both
            // the Grid and the Table honour them.
            var catStrip = TabStrip.Create(t, "PresetsCategoryTabStrip", height: 24f);
            // Eight tabs share one panel width. At the default 11 pt the longer labels wrap
            // mid-word — "Portals" became "Portal / s" — which is worse than small text.
            catStrip.LabelFontSize = 9f;
            refs.PresetsCategoryTabStrip = catStrip;
            catStrip.AddTab(CATEGORY_ALL_KEY, "All", null);
            foreach (var cat in ParticlePresetCategory.TabOrder)
                catStrip.AddTab(cat.ToString(), ParticlePresetCategory.Label(cat), null);
            catStrip.TabChanged += (_, key) => onCategoryChanged?.Invoke(key);

            // ── 3. Search box ─────────────────────────────────────────────────
            refs.SearchBox = SearchBox.Create(t, "Search presets…",
                v => onSearchChanged?.Invoke(v ?? ""));

            // ── 3a. Grid container ────────────────────────────────────────────
            var gridContainerGo = CreateUI("GridContainer", t);
            EnsureFlexibleHeightParticles(gridContainerGo);
            var gridVlg = gridContainerGo.AddComponent<VerticalLayoutGroup>();
            gridVlg.spacing                = 2f;
            gridVlg.childForceExpandWidth  = true;
            gridVlg.childForceExpandHeight = false;
            gridVlg.childControlWidth      = true;
            gridVlg.childControlHeight     = true;

            // Responsive grid picker (auto-reflow as panel is resized).
            var (gridScroll, gridContent, _) = EditorUIHelpers.MakeResponsiveGridPicker(
                gridContainerGo.transform, "PresetGrid",
                minCellSize: 64f, maxCellSize: 96f, spacing: 4f);
            EnsureFlexibleHeightParticles(gridScroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(gridScroll);
            refs.PickerContent = gridContent;

            // ── 3b. Table container ───────────────────────────────────────────
            var tableContainerGo = CreateUI("TableContainer", t);
            EnsureFlexibleHeightParticles(tableContainerGo);
            var tableVlg = tableContainerGo.AddComponent<VerticalLayoutGroup>();
            tableVlg.spacing                = 0f;
            tableVlg.childForceExpandWidth  = true;
            tableVlg.childForceExpandHeight = false;
            tableVlg.childControlWidth      = true;
            tableVlg.childControlHeight     = true;

            // Table toolbar: "Columns ▾" button aligned left (mirrors Spells Editor).
            BuildPresetsTableToolbar(tableContainerGo.transform, ref refs);
            BuildPresetsTableHeader(tableContainerGo.transform, ref refs);
            BuildPresetsTableBody(tableContainerGo.transform, ref refs);

            // ── 4. Status label ───────────────────────────────────────────────
            refs.StatusText      = EditorUIHelpers.MakeStatusText(t);
            refs.StatusText.text = "0 presets";

            // AddTab activates the first tab and deactivates others — Grid is default.
            tabStrip.AddTab("grid",  "Grid",  gridContainerGo);
            tabStrip.AddTab("table", "Table", tableContainerGo);
            tabStrip.transform.SetSiblingIndex(0);

            // Triangular resize handle in the bottom-right corner (16 px).
            BuildPresetsResizeHandle(refs.PresetsDropdown);

            refs.PresetsDropdown.SetActive(false);
        }

        // ── Table toolbar (Columns button) ───────────────────────────────────────

        private static void BuildPresetsTableToolbar(Transform parent, ref UIRefs refs)
        {
            const float TOOLBAR_H = 26f;
            var toolbarGo = CreateUI("TableToolbar", parent);
            toolbarGo.AddComponent<LayoutElement>().preferredHeight = TOOLBAR_H;
            toolbarGo.AddComponent<Image>().color =
                new Color(0.09f, 0.09f, 0.12f, 0.90f);

            var hlg = toolbarGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.padding                = new RectOffset(4, 4, 2, 2);
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            // "Columns ▾" button — label is updated by TableColumnsConfig.cs
            var colsBtnGo = CreateUI("ColumnsCfgBtn", toolbarGo.transform);
            colsBtnGo.AddComponent<LayoutElement>().preferredWidth = 120f;
            var colsBtnImg = colsBtnGo.AddComponent<Image>();
            colsBtnImg.color = UITheme.BTN_NORMAL;
            var colsBtn = colsBtnGo.AddComponent<Button>();
            colsBtn.targetGraphic = colsBtnImg;
            var bc = colsBtn.colors;
            bc.normalColor      = UITheme.BTN_NORMAL;
            bc.highlightedColor = UITheme.BTN_HOVER;
            bc.pressedColor     = UITheme.BTN_ACTIVE;
            bc.fadeDuration     = 0.08f;
            colsBtn.colors      = bc;

            var colsLblGo = CreateUI("Lbl", colsBtnGo.transform);
            UIFactory.StretchFill(colsLblGo);
            var colsLbl = colsLblGo.AddComponent<TextMeshProUGUI>();
            colsLbl.text      = "Columns";
            colsLbl.fontSize  = 10f;
            colsLbl.alignment = TextAlignmentOptions.Center;
            colsLbl.color     = TEXT_PRIMARY;
            colsLbl.enableWordWrapping = false;
            colsLbl.overflowMode       = TextOverflowModes.Truncate;

            refs.PresetsColumnsCfgBtn   = colsBtn;
            refs.PresetsColumnsCfgLabel = colsLbl;

            // Flexible spacer so future toolbar items anchor to the right.
            CreateUI("Spacer", toolbarGo.transform).AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        // ── Table header ──────────────────────────────────────────────────────────

        private static void BuildPresetsTableHeader(Transform parent, ref UIRefs refs)
        {
            var hdrScrollGo = CreateUI("PresetsTableHeaderScroll", parent);
            hdrScrollGo.AddComponent<LayoutElement>().preferredHeight = PARTICLE_TABLE_HEADER_STRIP_H;
            hdrScrollGo.AddComponent<RectMask2D>();
            hdrScrollGo.AddComponent<Image>().color = TileEditorTheme.HeaderBg;

            var hdrViewport   = CreateUI("Viewport", hdrScrollGo.transform);
            var hdrViewportRt = hdrViewport.GetComponent<RectTransform>();
            hdrViewportRt.anchorMin = Vector2.zero;
            hdrViewportRt.anchorMax = Vector2.one;
            hdrViewportRt.offsetMin = new Vector2(0f, 0f);
            hdrViewportRt.offsetMax = new Vector2(-PARTICLE_TABLE_SB_W, 0f);

            var hdrContent   = CreateUI("Content", hdrViewport.transform);
            var hdrContentRt = hdrContent.GetComponent<RectTransform>();
            hdrContentRt.anchorMin        = new Vector2(0f, 0f);
            hdrContentRt.anchorMax        = new Vector2(0f, 1f);
            hdrContentRt.pivot            = new Vector2(0f, 0.5f);
            hdrContentRt.anchoredPosition = Vector2.zero;
            hdrContentRt.sizeDelta        = Vector2.zero;

            // Gutter filler for the vertical-scrollbar column.
            var hdrGutterGo = CreateUI("HeaderGutter", hdrScrollGo.transform);
            var hdrGutterRt = hdrGutterGo.GetComponent<RectTransform>();
            hdrGutterRt.anchorMin        = new Vector2(1f, 0f);
            hdrGutterRt.anchorMax        = new Vector2(1f, 1f);
            hdrGutterRt.pivot            = new Vector2(1f, 0.5f);
            hdrGutterRt.anchoredPosition = Vector2.zero;
            hdrGutterRt.sizeDelta        = new Vector2(PARTICLE_TABLE_SB_W, 0f);
            hdrGutterGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;

            var hdrSR = hdrScrollGo.AddComponent<ScrollRect>();
            hdrSR.content           = hdrContentRt;
            hdrSR.viewport          = hdrViewportRt;
            hdrSR.horizontal        = false;  // programmatic-only; user drag disabled
            hdrSR.vertical          = false;
            hdrSR.scrollSensitivity = 0f;
            hdrSR.movementType      = ScrollRect.MovementType.Clamped;

            refs.PresetsTableHeaderScroll  = hdrSR;
            refs.PresetsTableHeaderContent = hdrContentRt;
        }

        // ── Table body ────────────────────────────────────────────────────────────

        private static void BuildPresetsTableBody(Transform parent, ref UIRefs refs)
        {
            const float hSbH = PARTICLE_TABLE_SB_W;

            var bodyScrollGo = CreateUI("PresetsTableBodyScroll", parent);
            EnsureFlexibleHeightParticles(bodyScrollGo);
            bodyScrollGo.AddComponent<RectMask2D>();
            bodyScrollGo.AddComponent<Image>().color = UITheme.BG_SURFACE;

            var bodyViewport   = CreateUI("Viewport", bodyScrollGo.transform);
            UIFactory.StretchFill(bodyViewport);
            var bodyViewportRt = bodyViewport.GetComponent<RectTransform>();
            bodyViewportRt.offsetMin = new Vector2(0f,                    hSbH);
            bodyViewportRt.offsetMax = new Vector2(-PARTICLE_TABLE_SB_W,  0f);

            var bodyContent   = CreateUI("Content", bodyViewport.transform);
            var bodyContentRt = bodyContent.GetComponent<RectTransform>();
            bodyContentRt.anchorMin        = new Vector2(0f, 1f);
            bodyContentRt.anchorMax        = new Vector2(0f, 1f);
            bodyContentRt.pivot            = new Vector2(0f, 1f);
            bodyContentRt.anchoredPosition = Vector2.zero;
            bodyContentRt.sizeDelta        = Vector2.zero;

            var bodyVlg = bodyContent.AddComponent<VerticalLayoutGroup>();
            bodyVlg.spacing                = 0f;
            bodyVlg.padding                = new RectOffset(0, 0, 0, 0);
            bodyVlg.childForceExpandWidth  = false;
            bodyVlg.childForceExpandHeight = false;
            bodyVlg.childControlWidth      = false;
            bodyVlg.childControlHeight     = false;
            bodyContent.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var bodySR = bodyScrollGo.AddComponent<ScrollRect>();
            bodySR.content           = bodyContentRt;
            bodySR.viewport          = bodyViewportRt;
            bodySR.horizontal        = true;
            bodySR.vertical          = true;
            bodySR.scrollSensitivity = 20f;
            bodySR.movementType      = ScrollRect.MovementType.Clamped;

            // Vertical scrollbar
            var vSbGo = CreateUI("VScrollbar", bodyScrollGo.transform);
            var vSbRt = vSbGo.GetComponent<RectTransform>();
            vSbRt.anchorMin        = new Vector2(1f, 0f);
            vSbRt.anchorMax        = new Vector2(1f, 1f);
            vSbRt.pivot            = new Vector2(1f, 1f);
            vSbRt.anchoredPosition = new Vector2(0f, hSbH);
            vSbRt.sizeDelta        = new Vector2(PARTICLE_TABLE_SB_W, -hSbH);
            vSbGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;
            var vSb = BuildEditorScrollbarHandle(vSbGo.transform, Scrollbar.Direction.BottomToTop);
            bodySR.verticalScrollbar = vSb;
            bodySR.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // Horizontal scrollbar
            var hSbGo = CreateUI("HScrollbar", bodyScrollGo.transform);
            var hSbRt = hSbGo.GetComponent<RectTransform>();
            hSbRt.anchorMin        = new Vector2(0f, 0f);
            hSbRt.anchorMax        = new Vector2(1f, 0f);
            hSbRt.pivot            = new Vector2(0f, 0f);
            hSbRt.anchoredPosition = Vector2.zero;
            hSbRt.sizeDelta        = new Vector2(-PARTICLE_TABLE_SB_W, hSbH);
            hSbGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;
            var hSb = BuildEditorScrollbarHandle(hSbGo.transform, Scrollbar.Direction.LeftToRight);
            bodySR.horizontalScrollbar = hSb;
            bodySR.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            refs.PresetsTableBodyScroll  = bodySR;
            refs.PresetsTableBodyContent = bodyContentRt;
        }

        // ── Resize handle (triangular, anchored bottom-right, 16 px) ─────────────

        private const float PRESET_RESIZE_HANDLE_PX = 16f;

        private static void BuildPresetsResizeHandle(GameObject panelRoot)
        {
            var panelRt = panelRoot.GetComponent<RectTransform>();
            if (panelRt == null) return;

            var go  = CreateUI("ResizeHandle", panelRoot.transform);
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 0f);
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(1f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(PRESET_RESIZE_HANDLE_PX, PRESET_RESIZE_HANDLE_PX);

            var tri    = go.AddComponent<TriangleHandleGraphic>();
            tri.color  = TileEditorTheme.Border;
            tri.raycastTarget = true;

            var handle    = go.AddComponent<PanelResizeHandle>();
            handle.Target = panelRt;
        }

        // ── Amber scrollbar handle (shared style for both table scrollbars) ───────
        /// <summary>
        /// Builds a styled Scrollbar's sliding area + handle inside <paramref name="parent"/>
        /// using the amber-colour scheme shared by the Spells editor table.
        /// Generic name so it can be called from VFX namespace without cross-assembly deps.
        /// </summary>
        private static Scrollbar BuildEditorScrollbarHandle(
            Transform parent, Scrollbar.Direction dir)
        {
            var sb       = parent.gameObject.AddComponent<Scrollbar>();
            sb.direction = dir;

            var sliding = CreateUI("SlidingArea", parent);
            var sRt     = sliding.GetComponent<RectTransform>();
            sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
            sRt.offsetMin = new Vector2(2f, 2f); sRt.offsetMax = new Vector2(-2f, -2f);

            var handle = CreateUI("Handle", sliding.transform);
            var hRt    = handle.GetComponent<RectTransform>();
            hRt.anchorMin = Vector2.zero; hRt.anchorMax = Vector2.one;
            hRt.offsetMin = Vector2.zero; hRt.offsetMax = Vector2.zero;
            var hImg   = handle.AddComponent<Image>();
            hImg.color = UITheme.SCROLL_HANDLE;

            sb.targetGraphic = hImg;
            sb.handleRect    = hRt;

            var cols = sb.colors;
            cols.normalColor      = UITheme.SCROLL_HANDLE;
            cols.highlightedColor = new Color(0.75f, 0.62f, 0.30f, 0.95f);
            cols.pressedColor     = UITheme.ACCENT;
            sb.colors = cols;
            return sb;
        }

        // ── Local helper ─────────────────────────────────────────────────────────

        private static void EnsureFlexibleHeightParticles(GameObject go, float flex = 1f)
        {
            if (go == null) return;
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleHeight = flex;
        }
    }
}
