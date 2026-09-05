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
    public static partial class SpellsEditorUIBuilder
    {
        // ── Spells Panel (picker — Grid / Table) ──────────────────────────────────
        // Layout top-to-bottom inside the panel content VLG:
        //   1. Tab strip (26 px) — "Grid" | "Table" | "Tree" | "Graph"
        //   2. Search box (26 px)
        //   3a. Grid container (flex) — 4-col icon grid (Grid tab, default)
        //   3b. Table container (flex) — sticky header + scrollable rows (Table tab)
        //   3c. Tree container (flex) — indented grimoire outline (Tree tab)
        //   (Graph tab owns no container — it opens SpellGraphView full-screen)
        //   4. Status label (20 px)

        private const float SPELL_TABLE_HEADER_STRIP_H = 24f;
        private const float SPELL_TABLE_SB_W           = 12f;

        private static void BuildSpellsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            float xOff = PANEL_GAP + MODES_W + PANEL_GAP;
            refs.SpellsDropdown = MakeDrop("SpellsCatalogPanel", canvasT,
                PanelDock.TopLeft, xOff, PANEL_TOP_OFFSET,
                SPELLS_W, SPELLS_H, "Spells",
                out var t, out refs.SpellsPanelDrag);

            // ── 1. Tab strip ──────────────────────────────────────────────────
            var tabStrip = TabStrip.Create(t, "SpellsViewTabStrip", height: 26f);

            refs.SpellAudienceTabs = TabStrip.Create(t, "SpellAudienceTabStrip", height: 24f);
            refs.SpellAudienceTabs.AddTab("all",        "All",        null);
            refs.SpellAudienceTabs.AddTab("player",     "Player",     null);
            refs.SpellAudienceTabs.AddTab("npc",        "NPC",        null);
            refs.SpellAudienceTabs.AddTab("boss",       "Boss",       null);
            refs.SpellAudienceTabs.AddTab("unassigned", "Unassigned", null);

            // ── 2. Search box ─────────────────────────────────────────────────
            refs.SearchBox = SearchBox.Create(t, "Search spells...", onSearchChanged);

            // ── 3a. Grid container ────────────────────────────────────────────
            var gridContainerGo = CreateUI("GridContainer", t);
            EnsureFlexibleHeight(gridContainerGo);
            var gridVlg = gridContainerGo.AddComponent<VerticalLayoutGroup>();
            gridVlg.spacing                = 2f;
            gridVlg.childForceExpandWidth  = true;
            gridVlg.childForceExpandHeight = false;
            gridVlg.childControlWidth      = true;
            gridVlg.childControlHeight     = true;

            var (gridScroll, gridContent, _) = EditorUIHelpers.MakeResponsiveGridPicker(
                gridContainerGo.transform, "SpellsGrid",
                minCellSize: 64f, maxCellSize: 96f, spacing: 4f);
            EnsureFlexibleHeight(gridScroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(gridScroll);
            refs.PickerContent = gridContent;
            MakeNestedCanvas(gridScroll.gameObject);

            // ── 3b. Table container ───────────────────────────────────────────
            var tableContainerGo = CreateUI("TableContainer", t);
            EnsureFlexibleHeight(tableContainerGo);
            var tableVlg = tableContainerGo.AddComponent<VerticalLayoutGroup>();
            tableVlg.spacing                = 0f;
            tableVlg.childForceExpandWidth  = true;
            tableVlg.childForceExpandHeight = false;
            tableVlg.childControlWidth      = true;
            tableVlg.childControlHeight     = true;

            BuildTableHeader(tableContainerGo.transform, ref refs);
            BuildTableBody(tableContainerGo.transform, ref refs);

            // ── 3c. Tree container ────────────────────────────────────────────
            var treeContainerGo = CreateUI("TreeContainer", t);
            EnsureFlexibleHeight(treeContainerGo);
            // The layout group is what SIZES the scroll surface inside. Without it the
            // container never lays its child out and the view renders at the RectTransform
            // default — measured live at 88 x 100 px inside a 312 px panel, which put the
            // cost column off the right-hand edge of every row. Grid and Table each carry
            // the same three lines for the same reason.
            var treeVlg = treeContainerGo.AddComponent<VerticalLayoutGroup>();
            treeVlg.spacing                = 2f;
            treeVlg.childForceExpandWidth  = true;
            treeVlg.childForceExpandHeight = false;
            treeVlg.childControlWidth      = true;
            treeVlg.childControlHeight     = true;

            // School sub-tabs. Empty here: the schools come from the ProgressionCatalog,
            // which this static builder has no access to, so SpellsRuntimeEditor fills the
            // strip once it has resolved the catalogue. Wrapped at four columns because a
            // single row would divide 300 px between eleven tabs.
            refs.SpellsTreeSchoolTabs = TabStrip.CreateWrapped(
                treeContainerGo.transform, "SpellsTreeSchoolTabs", columns: 4, rowHeight: 22f);

            BuildTreeBody(treeContainerGo.transform, ref refs);

            // ── 4. Status label ───────────────────────────────────────────────
            refs.StatusText      = EditorUIHelpers.MakeStatusText(t);
            refs.StatusText.text = "0 spells";

            // AddTab activates the first tab and deactivates all content GameObjects,
            // so the initial state is Grid visible and the other two hidden.
            tabStrip.AddTab("grid",  "Grid",  gridContainerGo);
            tabStrip.AddTab("table", "Table", tableContainerGo);
            tabStrip.AddTab("tree",  "Tree",  treeContainerGo);
            // No content of its own: the constellation is a full-screen slab, so selecting
            // this tab opens that and leaves the panel body empty BEHIND it. A tab with a
            // container would have to be the graph drawn at 312 px, which is the thing the
            // outline exists instead of.
            tabStrip.AddTab("graph", "Graph", null);
            tabStrip.transform.SetSiblingIndex(0);
            refs.SpellsViewTabs = tabStrip;

            BuildResizeHandle(refs.SpellsDropdown);

            refs.SpellsDropdown.SetActive(false);
        }

        /// <summary>
        /// The Tree view's scroll surface: one vertical list, no horizontal axis.
        ///
        /// <para>Unlike the table body next door this needs no horizontal scrollbar — the
        /// outline is exactly as wide as the panel and gets its structure from indentation, so
        /// a horizontal axis would only ever be a way to lose the labels off-screen.</para>
        /// </summary>
        private static void BuildTreeBody(Transform parent, ref UIRefs refs)
        {
            var scrollGo = CreateUI("SpellsTreeScroll", parent);
            EnsureFlexibleHeight(scrollGo);
            scrollGo.AddComponent<RectMask2D>();
            scrollGo.AddComponent<Image>().color = UITheme.BG_SURFACE;

            var viewport = CreateUI("Viewport", scrollGo.transform);
            UIFactory.StretchFill(viewport);
            var viewportRt = viewport.GetComponent<RectTransform>();
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = new Vector2(-SPELL_TABLE_SB_W, 0f);

            var content   = CreateUI("Content", viewport.transform);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin        = new Vector2(0f, 1f);
            contentRt.anchorMax        = new Vector2(1f, 1f);
            contentRt.pivot            = new Vector2(0f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta        = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 0f;
            vlg.padding                = new RectOffset(0, 0, 0, 0);
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            content.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.content           = contentRt;
            sr.viewport          = viewportRt;
            sr.horizontal        = false;
            sr.vertical          = true;
            sr.scrollSensitivity = 20f;
            sr.movementType      = ScrollRect.MovementType.Clamped;

            var sbGo = CreateUI("VScrollbar", scrollGo.transform);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin        = new Vector2(1f, 0f);
            sbRt.anchorMax        = new Vector2(1f, 1f);
            sbRt.pivot            = new Vector2(1f, 1f);
            sbRt.anchoredPosition = Vector2.zero;
            sbRt.sizeDelta        = new Vector2(SPELL_TABLE_SB_W, 0f);
            sbGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;
            sr.verticalScrollbar = BuildSpellScrollbarHandle(sbGo.transform,
                Scrollbar.Direction.BottomToTop);
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            refs.SpellsTreeScroll  = sr;
            refs.SpellsTreeContent = contentRt;
            MakeNestedCanvas(scrollGo);
        }


        /// <summary>
        /// Give a scrolling list its own canvas.
        ///
        /// <para>A uGUI canvas rebuilds and re-batches as ONE unit, so before this a single
        /// row repaint — a hover, a selection moving, a zebra stripe changing — dirtied all
        /// ~1,000 active Graphics of the editor at once. A nested Canvas draws the same
        /// pixels in the same order (it inherits sorting; overrideSorting stays false) while
        /// confining that rebuild to the list, which is exactly the part that changes.</para>
        ///
        /// <para>The GraphicRaycaster is not optional: a nested Canvas ends the parent
        /// raycaster's traversal, so without one the rows stop taking clicks entirely.</para>
        /// </summary>
        private static void MakeNestedCanvas(GameObject go)
        {
            if (go == null || go.GetComponent<Canvas>() != null) return;
            go.AddComponent<Canvas>();
            go.AddComponent<GraphicRaycaster>();
        }

        private static void BuildTableHeader(Transform parent, ref UIRefs refs)
        {
            // Sticky header: horizontal-only ScrollRect.
            // The body's onValueChanged mirrors its x-position onto the header
            // content via absolute pixel offset (see SpellsRuntimeEditor.Table.cs).
            var hdrScrollGo = CreateUI("SpellsTableHeaderScroll", parent);
            hdrScrollGo.AddComponent<LayoutElement>().preferredHeight = SPELL_TABLE_HEADER_STRIP_H;
            hdrScrollGo.AddComponent<RectMask2D>();
            hdrScrollGo.AddComponent<Image>().color = TileEditorTheme.HeaderBg;

            var hdrViewport   = CreateUI("Viewport", hdrScrollGo.transform);
            var hdrViewportRt = hdrViewport.GetComponent<RectTransform>();
            hdrViewportRt.anchorMin = Vector2.zero;
            hdrViewportRt.anchorMax = Vector2.one;
            hdrViewportRt.offsetMin = new Vector2(0f, 0f);
            hdrViewportRt.offsetMax = new Vector2(-SPELL_TABLE_SB_W, 0f);

            var hdrContent   = CreateUI("Content", hdrViewport.transform);
            var hdrContentRt = hdrContent.GetComponent<RectTransform>();
            hdrContentRt.anchorMin        = new Vector2(0f, 0f);
            hdrContentRt.anchorMax        = new Vector2(0f, 1f);
            hdrContentRt.pivot            = new Vector2(0f, 0.5f);
            hdrContentRt.anchoredPosition = Vector2.zero;
            hdrContentRt.sizeDelta        = Vector2.zero;

            // Gutter filler for the 12 px vertical-scrollbar column.
            var hdrGutterGo = CreateUI("HeaderGutter", hdrScrollGo.transform);
            var hdrGutterRt = hdrGutterGo.GetComponent<RectTransform>();
            hdrGutterRt.anchorMin        = new Vector2(1f, 0f);
            hdrGutterRt.anchorMax        = new Vector2(1f, 1f);
            hdrGutterRt.pivot            = new Vector2(1f, 0.5f);
            hdrGutterRt.anchoredPosition = Vector2.zero;
            hdrGutterRt.sizeDelta        = new Vector2(SPELL_TABLE_SB_W, 0f);
            hdrGutterGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;

            var hdrSR = hdrScrollGo.AddComponent<ScrollRect>();
            hdrSR.content           = hdrContentRt;
            hdrSR.viewport          = hdrViewportRt;
            hdrSR.horizontal        = false;   // programmatic only — user drag disabled
            hdrSR.vertical          = false;
            hdrSR.scrollSensitivity = 0f;
            hdrSR.movementType      = ScrollRect.MovementType.Clamped;

            refs.SpellsTableHeaderScroll  = hdrSR;
            refs.SpellsTableHeaderContent = hdrContentRt;
        }

        private static void BuildTableBody(Transform parent, ref UIRefs refs)
        {
            const float hSbH = SPELL_TABLE_SB_W;

            var bodyScrollGo = CreateUI("SpellsTableBodyScroll", parent);
            EnsureFlexibleHeight(bodyScrollGo);
            bodyScrollGo.AddComponent<RectMask2D>();
            bodyScrollGo.AddComponent<Image>().color = UITheme.BG_SURFACE;

            var bodyViewport   = CreateUI("Viewport", bodyScrollGo.transform);
            UIFactory.StretchFill(bodyViewport);
            var bodyViewportRt = bodyViewport.GetComponent<RectTransform>();
            bodyViewportRt.offsetMin = new Vector2(0f,                  hSbH);
            bodyViewportRt.offsetMax = new Vector2(-SPELL_TABLE_SB_W,   0f);

            var bodyContent   = CreateUI("Content", bodyViewport.transform);
            var bodyContentRt = bodyContent.GetComponent<RectTransform>();
            bodyContentRt.anchorMin        = new Vector2(0f, 1f);
            bodyContentRt.anchorMax        = new Vector2(0f, 1f);
            bodyContentRt.pivot            = new Vector2(0f, 1f);
            bodyContentRt.anchoredPosition = Vector2.zero;
            bodyContentRt.sizeDelta        = Vector2.zero;

            // NO layout group and NO ContentSizeFitter on the body, on purpose. The table
            // is VIRTUALISED: the rows that exist are an arbitrary window of the list, so a
            // layout group would stack whatever happens to be there from the top and put row
            // 90 where row 0 belongs, and the fitter would shrink the content to that window
            // and make the rest unreachable. RefreshTable sizes the content itself.
            // ItemsRuntimeEditor.Table.cs records the same two constraints.

            var bodySR = bodyScrollGo.AddComponent<ScrollRect>();
            bodySR.content           = bodyContentRt;
            bodySR.viewport          = bodyViewportRt;
            bodySR.horizontal        = true;
            bodySR.vertical          = true;
            bodySR.scrollSensitivity = 20f;
            bodySR.movementType      = ScrollRect.MovementType.Clamped;

            var vSbGo = CreateUI("VScrollbar", bodyScrollGo.transform);
            var vSbRt = vSbGo.GetComponent<RectTransform>();
            vSbRt.anchorMin        = new Vector2(1f, 0f);
            vSbRt.anchorMax        = new Vector2(1f, 1f);
            vSbRt.pivot            = new Vector2(1f, 1f);
            vSbRt.anchoredPosition = new Vector2(0f, hSbH);
            vSbRt.sizeDelta        = new Vector2(SPELL_TABLE_SB_W, -hSbH);
            vSbGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;
            var vSb = BuildSpellScrollbarHandle(vSbGo.transform, Scrollbar.Direction.BottomToTop);
            bodySR.verticalScrollbar = vSb;
            bodySR.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            var hSbGo = CreateUI("HScrollbar", bodyScrollGo.transform);
            var hSbRt = hSbGo.GetComponent<RectTransform>();
            hSbRt.anchorMin        = new Vector2(0f, 0f);
            hSbRt.anchorMax        = new Vector2(1f, 0f);
            hSbRt.pivot            = new Vector2(0f, 0f);
            hSbRt.anchoredPosition = Vector2.zero;
            hSbRt.sizeDelta        = new Vector2(-SPELL_TABLE_SB_W, hSbH);
            hSbGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;
            var hSb = BuildSpellScrollbarHandle(hSbGo.transform, Scrollbar.Direction.LeftToRight);
            bodySR.horizontalScrollbar = hSb;
            bodySR.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            refs.SpellsTableBodyScroll  = bodySR;
            refs.SpellsTableBodyContent = bodyContentRt;
            MakeNestedCanvas(bodyScrollGo);
        }

        // Triangle resize handle anchored to the panel's bottom-right corner.
        private const float RESIZE_HANDLE_PX = 16f;

        private static void BuildResizeHandle(GameObject panelRoot)
        {
            var panelRt = panelRoot.GetComponent<RectTransform>();
            if (panelRt == null) return;

            var go  = CreateUI("ResizeHandle", panelRoot.transform);
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 0f);
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(1f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(RESIZE_HANDLE_PX, RESIZE_HANDLE_PX);

            var tri    = go.AddComponent<TriangleHandleGraphic>();
            tri.color  = TileEditorTheme.Border;
            tri.raycastTarget = true;

            var handle    = go.AddComponent<PanelResizeHandle>();
            handle.Target = panelRt;
        }

        /// <summary>
        /// Builds a styled Scrollbar's sliding area + handle as children of parent
        /// and returns the Scrollbar component attached to parent's GameObject.
        /// </summary>
        private static Scrollbar BuildSpellScrollbarHandle(Transform parent, Scrollbar.Direction dir)
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
    }
}
