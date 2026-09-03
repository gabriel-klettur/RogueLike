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
    /// Items Editor panel builders.
    /// Each panel mirrors a Python items_editor sub-panel:
    ///   - Modes      <- Toolbar + AddRemove panels (combined into one narrow column)
    ///   - Items      <- Picker panel (search + Grid/Table tab)
    ///   - Properties <- Properties panel (selected item inspector)
    ///   - Instances  <- InstancesPanel + ParamsPanel (drops list + params editor)
    /// </summary>
    public static partial class ItemsEditorUIBuilder
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Ensures the GameObject has a LayoutElement with flexibleHeight=1.
        /// EditorUIHelpers.MakeScrollView only adds a LayoutElement when called
        /// with an explicit height; without it we have to add one ourselves so the
        /// scroll view fills the remaining vertical space inside its parent VLG.
        /// </summary>
        private static void EnsureFlexibleHeight(GameObject go, float flex = 1f)
        {
            if (go == null) return;
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleHeight = flex;
        }

        // ── Modes Panel (60 px narrow, top-left) ──────────────────────────────────
        // Modes: Select / Spawn / Delete   +   Add / Remove / Add-on-System
        // Plus Undo / Redo as inline action buttons.

        private static void BuildModesPanel(Transform canvasT, ref UIRefs refs,
            Action onModeSelect, Action onModeSpawn, Action onModeDelete,
            Action onAdd,        Action onRemove,   Action onAddOnSystem,
            Action onUndo,       Action onRedo)
        {
            refs.ModesDropdown = MakeDrop("ItemsModesPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                MODES_W, MODES_H, "Modes",
                out var t, out refs.ModesPanelDrag, narrowPanel: true);

            // Mode buttons (highlight reflects active mode)
            refs.SelectBtnImg = AddToolBtn(t, "Sel", "ect",   BTN_H, onModeSelect);
            refs.SpawnBtnImg  = AddToolBtn(t, "Spwn", "+",    BTN_H, onModeSpawn);
            refs.DeleteBtnImg = AddDangerToolBtn(t, "Del", "X", BTN_H, onModeDelete);

            AddInlineSeparator(t);
            AddSectionLabel(t, "ADD");

            refs.AddBtnImg         = AddToolBtn(t, "Add",  "to map", BTN_H, onAdd);
            refs.RemoveBtnImg      = AddDangerToolBtn(t, "Rem", "from map", BTN_H, onRemove);
            refs.AddOnSystemBtnImg = AddToolBtn(t, "+Sys", "system", BTN_H, onAddOnSystem);

            AddInlineSeparator(t);
            AddSectionLabel(t, "EDIT");

            AddActionBtn(t, "Undo", 24f, onUndo);
            AddActionBtn(t, "Redo", 24f, onRedo);

            refs.ModesDropdown.SetActive(false);
        }

        // ── Items Panel ───────────────────────────────────────────────────────────
        // Layout top-to-bottom inside the panel content VLG:
        //   1. Tab strip (26 px) -- "Grid" | "Table"
        //   2. Search box (26 px)
        //   3a. Grid container (flex) -- 3-col icon grid (Grid tab, default)
        //   3b. Table container (flex) -- sticky header + scrollable rows (Table tab)
        //   4. Status label (20 px)
        //
        // The panel uses ITEMS_W (256 px) in both modes; the table view adds
        // horizontal scrolling so all columns are accessible within that width.
        // The tab strip is what hides/shows containers 3a and 3b.

        private const float TABLE_HEADER_H = 24f;    // sticky header strip height
        private const float TABLE_SB_W     = 12f;    // scrollbar width (both axes)

        private static void BuildItemsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            float xOff = PANEL_GAP + MODES_W + PANEL_GAP;
            refs.ItemsDropdown = MakeDrop("ItemsCatalogPanel", canvasT,
                PanelDock.TopLeft, xOff, PANEL_TOP_OFFSET,
                ITEMS_W, ITEMS_H, "Items",
                out var t, out refs.ItemsPanelDrag);

            // ── 1. Tab strip ──────────────────────────────────────────────────
            // TabStrip.Create adds to end of 't'; we move it to sibling index 0
            // below after all children exist so the VLG order is correct.
            var tabStrip = TabStrip.Create(t, "ViewTabStrip", height: 26f);

            // ── 2. Search box ─────────────────────────────────────────────────
            refs.SearchBox = SearchBox.Create(t, "Search items...", onSearchChanged);

            // ── 3a. Grid container ────────────────────────────────────────────
            var gridContainerGo = CreateUI("GridContainer", t);
            EnsureFlexibleHeight(gridContainerGo);
            // VLG so the grid scroll view fills it vertically.
            var gridVlg = gridContainerGo.AddComponent<VerticalLayoutGroup>();
            gridVlg.spacing                = 2f;
            gridVlg.childForceExpandWidth  = true;
            gridVlg.childForceExpandHeight = false;
            gridVlg.childControlWidth      = true;
            gridVlg.childControlHeight     = true;

            // Category filter tabs — one tab per ItemCategory + "All". Lets
            // the user narrow the picker to Equipment / Consumable / Material
            // / Quest / Other (mirrors the inventory tab layout). Tabs reuse
            // TabStrip with content = null so we listen to TabChanged for the
            // filter event instead of letting it toggle hidden GameObjects.
            var catTabs = TabStrip.Create(gridContainerGo.transform,
                "GridCategoryTabs", height: 22f);
            catTabs.AddTab("all",        "All",    null);
            catTabs.AddTab("equipment",  "Equip",  null);
            catTabs.AddTab("consumable", "Consum", null);
            catTabs.AddTab("material",   "Mat",    null);
            catTabs.AddTab("quest",      "Quest",  null);
            catTabs.AddTab("other",      "Other",  null);
            refs.GridCategoryTabs = catTabs;

            // Responsive grid: cell size + column count adapt to the panel
            // width as the user drags the resize handle. minCellSize=64
            // matches the legacy slot footprint; maxCellSize=96 keeps cells
            // usable when only a couple of items match a narrow filter.
            var (gridScroll, gridContent, _) = EditorUIHelpers.MakeResponsiveGridPicker(
                gridContainerGo.transform, "ItemsGrid",
                minCellSize: 64f, maxCellSize: 96f, spacing: 4f);
            EnsureFlexibleHeight(gridScroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(gridScroll);
            refs.PickerContent = gridContent;

            // Empty-state hint sibling — overlays the scroll viewport when no
            // items match the filter. Toggled on/off by the picker rebuild.
            var gridEmptyGo = CreateUI("GridEmptyState", gridContainerGo.transform);
            var gridEmptyRt = gridEmptyGo.GetComponent<RectTransform>();
            // Pull it out of the VLG so it can overlay the scroll view.
            gridEmptyGo.AddComponent<LayoutElement>().ignoreLayout = true;
            gridEmptyRt.anchorMin = Vector2.zero;
            gridEmptyRt.anchorMax = Vector2.one;
            gridEmptyRt.offsetMin = Vector2.zero;
            gridEmptyRt.offsetMax = Vector2.zero;
            var gridEmptyTmp        = gridEmptyGo.AddComponent<TextMeshProUGUI>();
            gridEmptyTmp.text       = "No items match the current filter.";
            gridEmptyTmp.fontSize   = 11f;
            gridEmptyTmp.fontStyle  = FontStyles.Italic;
            gridEmptyTmp.alignment  = TextAlignmentOptions.Center;
            gridEmptyTmp.color      = TEXT_MUTED;
            gridEmptyTmp.enableWordWrapping = true;
            gridEmptyTmp.raycastTarget      = false;
            refs.GridEmptyState = gridEmptyTmp;
            gridEmptyGo.SetActive(false);

            // ── 3b. Table container ───────────────────────────────────────────
            var tableContainerGo = CreateUI("TableContainer", t);
            EnsureFlexibleHeight(tableContainerGo);
            var tableVlg = tableContainerGo.AddComponent<VerticalLayoutGroup>();
            tableVlg.spacing                = 0f;
            tableVlg.childForceExpandWidth  = true;
            tableVlg.childForceExpandHeight = false;
            tableVlg.childControlWidth      = true;
            tableVlg.childControlHeight     = true;

            // ── Table config bar (above the header) ──────────────────────────
            // Hosts the "⚙ Columns" button that opens the visibility popup,
            // plus a live "Columns: N/M" indicator. Layout is a HLG so the
            // counter can flex while the button stays a fixed width.
            var configBarGo = CreateUI("TableConfigBar", tableContainerGo.transform);
            configBarGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            configBarGo.AddComponent<Image>().color = TileEditorTheme.HeaderBg;

            var configHlg = configBarGo.AddComponent<HorizontalLayoutGroup>();
            configHlg.spacing                = 6f;
            configHlg.padding                = new RectOffset(6, 6, 0, 0);
            configHlg.childForceExpandWidth  = false;
            configHlg.childForceExpandHeight = true;
            configHlg.childControlWidth      = true;
            configHlg.childControlHeight     = true;
            configHlg.childAlignment         = TextAnchor.MiddleLeft;

            var counterGo = CreateUI("Counter", configBarGo.transform);
            counterGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var counterTmp        = counterGo.AddComponent<TextMeshProUGUI>();
            counterTmp.fontSize   = 10f;
            counterTmp.fontStyle  = FontStyles.Bold;
            counterTmp.alignment  = TextAlignmentOptions.MidlineLeft;
            counterTmp.color      = TEXT_SECONDARY;
            counterTmp.text       = ""; // populated by RefreshColumnsCountLabel
            refs.TableColumnsCountLabel = counterTmp;

            // Button click is wired in ItemsRuntimeEditor.cs after BuildAll
            // returns (the editor instance owns the popup state).
            var colsBtn = UIButton.Make(configBarGo.transform, "Columns",
                onClick: null, height: 18f, fontSize: 10f);
            var colsBtnLE = colsBtn.GetComponent<LayoutElement>();
            colsBtnLE.preferredWidth = 80f;
            colsBtnLE.flexibleWidth  = 0f;
            refs.TableColumnsButton = colsBtn;

            // Sticky header: horizontal-only ScrollRect (no scrollbar; body drives it).
            // Holds the header strip; the body's scroll position is mirrored onto
            // the header content via absolute pixel offset (see Table.cs).
            var hdrScrollGo = CreateUI("TableHeaderScroll", tableContainerGo.transform);
            hdrScrollGo.AddComponent<LayoutElement>().preferredHeight = TABLE_HEADER_H;
            hdrScrollGo.AddComponent<RectMask2D>();
            hdrScrollGo.AddComponent<Image>().color = TileEditorTheme.HeaderBg;

            // Viewport's right edge is inset by TABLE_SB_W to mirror the body
            // viewport (which leaves room for the vertical scrollbar). Without
            // this inset the header would be 12 px wider than the body and
            // columns would visually misalign by 12 px when scrolled.
            var hdrViewport   = CreateUI("Viewport", hdrScrollGo.transform);
            var hdrViewportRt = hdrViewport.GetComponent<RectTransform>();
            hdrViewportRt.anchorMin = Vector2.zero;
            hdrViewportRt.anchorMax = Vector2.one;
            hdrViewportRt.offsetMin = new Vector2(0f, 0f);
            hdrViewportRt.offsetMax = new Vector2(-TABLE_SB_W, 0f);

            var hdrContent   = CreateUI("Content", hdrViewport.transform);
            var hdrContentRt = hdrContent.GetComponent<RectTransform>();
            hdrContentRt.anchorMin        = new Vector2(0f, 0f);
            hdrContentRt.anchorMax        = new Vector2(0f, 1f);
            hdrContentRt.pivot            = new Vector2(0f, 0.5f);
            hdrContentRt.anchoredPosition = Vector2.zero;
            hdrContentRt.sizeDelta        = Vector2.zero;   // sized by BuildTableHeader()

            // Visual filler for the 12 px gutter where the body's vertical
            // scrollbar lives. Same colour as the scrollbar track so the chrome
            // looks continuous from header through body.
            var hdrGutterGo = CreateUI("HeaderGutter", hdrScrollGo.transform);
            var hdrGutterRt = hdrGutterGo.GetComponent<RectTransform>();
            hdrGutterRt.anchorMin        = new Vector2(1f, 0f);
            hdrGutterRt.anchorMax        = new Vector2(1f, 1f);
            hdrGutterRt.pivot            = new Vector2(1f, 0.5f);
            hdrGutterRt.anchoredPosition = Vector2.zero;
            hdrGutterRt.sizeDelta        = new Vector2(TABLE_SB_W, 0f);
            hdrGutterGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;

            // ScrollRect on the header is configured purely for content clipping
            // and programmatic positioning — horizontal=false locks out user
            // drag so the header can't desync from the body.
            var hdrSR = hdrScrollGo.AddComponent<ScrollRect>();
            hdrSR.content           = hdrContentRt;
            hdrSR.viewport          = hdrViewportRt;
            hdrSR.horizontal        = false;
            hdrSR.vertical          = false;
            hdrSR.scrollSensitivity = 0f;
            hdrSR.movementType      = ScrollRect.MovementType.Clamped;

            refs.TableHeaderScroll  = hdrSR;
            refs.TableHeaderContent = hdrContentRt;

            // Body: horizontal + vertical ScrollRect.
            var bodyScrollGo = CreateUI("TableBodyScroll", tableContainerGo.transform);
            EnsureFlexibleHeight(bodyScrollGo);
            bodyScrollGo.AddComponent<RectMask2D>();
            bodyScrollGo.AddComponent<Image>().color = UITheme.BG_SURFACE;

            const float hSbH = TABLE_SB_W;
            var bodyViewport   = CreateUI("Viewport", bodyScrollGo.transform);
            UIFactory.StretchFill(bodyViewport);
            var bodyViewportRt = bodyViewport.GetComponent<RectTransform>();
            // Leave room for both scrollbars.
            bodyViewportRt.offsetMin = new Vector2(0f,          hSbH);
            bodyViewportRt.offsetMax = new Vector2(-TABLE_SB_W, 0f);

            var bodyContent   = CreateUI("Content", bodyViewport.transform);
            var bodyContentRt = bodyContent.GetComponent<RectTransform>();
            bodyContentRt.anchorMin        = new Vector2(0f, 1f);
            bodyContentRt.anchorMax        = new Vector2(0f, 1f);
            bodyContentRt.pivot            = new Vector2(0f, 1f);
            bodyContentRt.anchoredPosition = Vector2.zero;
            bodyContentRt.sizeDelta        = Vector2.zero;

            // Rows are stacked by a VLG; they set their own explicit width via sizeDelta.
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
            bodySR.content          = bodyContentRt;
            bodySR.viewport         = bodyViewportRt;
            bodySR.horizontal       = true;
            bodySR.vertical         = true;
            bodySR.scrollSensitivity = 20f;
            bodySR.movementType     = ScrollRect.MovementType.Clamped;

            // Vertical scrollbar (right edge).
            var vSbGo = CreateUI("VScrollbar", bodyScrollGo.transform);
            var vSbRt = vSbGo.GetComponent<RectTransform>();
            vSbRt.anchorMin        = new Vector2(1f, 0f);
            vSbRt.anchorMax        = new Vector2(1f, 1f);
            vSbRt.pivot            = new Vector2(1f, 1f);
            vSbRt.anchoredPosition = new Vector2(0f, hSbH);
            vSbRt.sizeDelta        = new Vector2(TABLE_SB_W, -hSbH);
            vSbGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;
            var vSb = BuildScrollbarHandle(vSbGo.transform, Scrollbar.Direction.BottomToTop);
            bodySR.verticalScrollbar = vSb;
            bodySR.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // Horizontal scrollbar (bottom edge).
            var hSbGo = CreateUI("HScrollbar", bodyScrollGo.transform);
            var hSbRt = hSbGo.GetComponent<RectTransform>();
            hSbRt.anchorMin        = new Vector2(0f, 0f);
            hSbRt.anchorMax        = new Vector2(1f, 0f);
            hSbRt.pivot            = new Vector2(0f, 0f);
            hSbRt.anchoredPosition = Vector2.zero;
            hSbRt.sizeDelta        = new Vector2(-TABLE_SB_W, hSbH);
            hSbGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;
            var hSb = BuildScrollbarHandle(hSbGo.transform, Scrollbar.Direction.LeftToRight);
            bodySR.horizontalScrollbar = hSb;
            bodySR.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            refs.TableBodyScroll  = bodySR;
            refs.TableBodyContent = bodyContentRt;

            // ── 4. Status label ───────────────────────────────────────────────
            refs.StatusText      = EditorUIHelpers.MakeStatusText(t);
            refs.StatusText.text = "0 items";

            // ── Wire TabStrip tabs ────────────────────────────────────────────
            // TabStrip.AddTab activates the first registered tab and deactivates
            // all content GameObjects, so the initial state is Grid visible.
            tabStrip.AddTab("grid",  "Grid",  gridContainerGo);
            tabStrip.AddTab("table", "Table", tableContainerGo);

            // Move tab strip to sibling index 0 (above search box) so the
            // VLG renders it at the top of the panel.
            tabStrip.transform.SetSiblingIndex(0);

            // ── Resize handle (bottom-right triangle) ─────────────────────────
            BuildResizeHandle(refs.ItemsDropdown);

            refs.ItemsDropdown.SetActive(false);
        }

        // Triangle resize handle anchored to the panel's bottom-right corner.
        // Sibling of header / content (not inside the VLG) so layout never
        // pushes it around. Drag it to grow / shrink the panel bidirectionally.
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
        /// Builds a styled Scrollbar's sliding area + handle as children of
        /// <paramref name="parent"/> and returns the <see cref="Scrollbar"/>
        /// component already attached to <paramref name="parent"/>'s GameObject.
        /// </summary>
        private static Scrollbar BuildScrollbarHandle(Transform parent, Scrollbar.Direction dir)
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

        // ── Properties Panel (250 px, top-right) ──────────────────────────────────
        // Inspector for the selected ItemDefinition (Phase 2: real property editor).

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.PropsDropdown = MakeDrop("ItemsPropertiesPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties",
                out var t, out refs.PropsPanelDrag);

            // Title strip -- bold, single line, fixed height. Shows the active item's
            // displayName so the inspector body below stays free for the full table.
            var titleGo = CreateUI("PropsTitle", t);
            titleGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var titleTmp        = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text       = "(no item selected)";
            titleTmp.fontSize   = 12f;
            titleTmp.fontStyle  = FontStyles.Bold;
            titleTmp.alignment  = TextAlignmentOptions.Center;
            titleTmp.color      = ACCENT;
            titleTmp.enableWordWrapping = false;
            titleTmp.overflowMode = TextOverflowModes.Ellipsis;
            refs.PropsTitle     = titleTmp;

            AddInlineSeparator(t);

            // Scrollable content area for the full property table.
            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "PropsScroll");
            EnsureFlexibleHeight(scroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.PropsContent = content;

            // Body TMP -- lives inside the scroll content so long inspectors scroll
            // instead of overlapping the title strip.
            var bodyGo = CreateUI("PropsBody", content);
            var bodyLE = bodyGo.AddComponent<LayoutElement>();
            bodyLE.flexibleHeight = 1f;
            bodyLE.minHeight = 60f;
            var bodyTmp        = bodyGo.AddComponent<TextMeshProUGUI>();
            bodyTmp.text       = "Select an item from the grid to view its properties.";
            bodyTmp.fontSize   = 11f;
            bodyTmp.alignment  = TextAlignmentOptions.TopLeft;
            bodyTmp.color      = TEXT_PRIMARY;
            bodyTmp.enableWordWrapping = true;
            bodyTmp.richText   = true;
            bodyTmp.margin     = new Vector4(6f, 4f, 6f, 4f);
            refs.PropsText     = bodyTmp;

            refs.PropsDropdown.SetActive(false);
        }

        // ── Instances Panel (280 px, bottom-right) ────────────────────────────────
        // Lists items currently dropped on the map + per-instance params.

        private static void BuildInstancesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.InstancesDropdown = MakeDrop("ItemsInstancesPanel", canvasT,
                PanelDock.BottomRight, PANEL_GAP, PANEL_GAP,
                INSTANCES_W, INSTANCES_H, "Instances",
                out var t, out refs.InstancesPanelDrag);

            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "InstancesScroll");
            EnsureFlexibleHeight(scroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.InstancesListContent = content;

            // Empty-state hint
            var hintGo = CreateUI("InstancesHint", content);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 80f;
            var hintTmp        = hintGo.AddComponent<TextMeshProUGUI>();
            hintTmp.text       = "(no item drops on the map)\n\nUse Add mode to drop items.\nClick a drop in this list to inspect or edit its params.";
            hintTmp.fontSize   = 10f;
            hintTmp.fontStyle  = FontStyles.Italic;
            hintTmp.alignment  = TextAlignmentOptions.Center;
            hintTmp.color      = TEXT_MUTED;
            hintTmp.enableWordWrapping = true;
            refs.InstancesHint = hintTmp;

            refs.InstancesDropdown.SetActive(false);
        }
    }
}
