using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    public static partial class TileEditorUIBuilder
    {
        // Triangle resize handle pinned to the panel's bottom-right corner
        // (mirrors Spells Editor convention so users have one consistent gesture).
        private const float TILES_RESIZE_HANDLE_PX = 16f;
        // Soft floor on shrinking the Tiles panel. Sized so the layout never
        // collapses below "LEFT column (200) + gap (8) + one CATEGORIES column
        // (min cell 110 + 6 px padding + 12 px scrollbar) + 16 px panel padding".
        private static readonly Vector2 TILES_PANEL_MIN_SIZE = new Vector2(360f, 320f);
        // Soft ceiling on growing the panel — large enough to occupy a 4K canvas
        // half-screen but small enough to avoid runaway drag overshoot.
        private static readonly Vector2 TILES_PANEL_MAX_SIZE = new Vector2(1600f, 1400f);
        // Inner content row width at the panel's initial size (panel width minus
        // the 8px lateral padding applied by the shared MakeDropdownPanel VLG).
        // Chrome rows (selected preview, categories, zoom controls, footer) stay
        // pinned to this width even after the panel is resized — only the
        // TILES picker absorbs the delta. See ApplyTilesPanelResizePolicy.
        private const float TILES_PANEL_ROW_W = TILES_DROP_W - 16f;

        // Top-row layout (SELECTED + RULESET on the left, CATEGORIES on the right).
        // Floor for the auto-derived row height so the column LayoutGroups always
        // have a sane minimum to compute against. The row's actual height is
        // max(LEFT.preferredHeight, RIGHT.preferredHeight) — both columns report
        // content-driven heights, so the row only takes the room it needs.
        // The effective ceiling is bounded by TILES_CAT_SCROLL_MAX_H (the
        // CATEGORIES scroll caps itself before the row gets pushed any taller).
        private const float TILES_TOP_ROW_MIN_H = 56f;
        // Left-column width — sized so the SELECTED tile name fits on a single
        // line ("pandora_r06_c07" ≈ 110 px at fontSize 12) AND the RULESET
        // button ("NO RULESET FOR CATEGORY") fits across without wrapping.
        // The previous 88 px was too tight on both axes.
        private const float TILES_TOP_LEFT_W  = 200f;
        // Gap between left and right columns inside the top row.
        private const float TILES_TOP_GAP     = 8f;

        // ── Responsive CATEGORIES list (driven by GridAutoSize) ──────────────
        // Smallest comfortable width for a single category cell. Sized so the
        // longest existing category name ("castle_pandora", 14 chars at
        // fontSize 10 ≈ 90 px) fits with breathing room. Drops below this and
        // GridAutoSize keeps 1 column and clips instead of growing further.
        private const float TILES_CAT_BTN_MIN_W   = 110f;
        // Largest allowed width per category cell — caps how big the buttons
        // get when the panel is dragged very wide. Keeps text from looking
        // lost in a 400 px-wide cell.
        private const float TILES_CAT_BTN_MAX_W   = 200f;
        // Fixed cell height — categories are short labels, height never grows.
        private const float TILES_CAT_BTN_H       = 22f;
        // Uniform spacing between category cells (both axes).
        private const float TILES_CAT_GRID_SPACING = 3f;

        private static void BuildTilesDropdown(Transform canvasT, ref UIRefs refs)
        {
            refs.TilesDropdown = MakeDropdownPanel("TilesDropdown", canvasT,
                PanelDock.TopLeft, TilesX, TilesY, TILES_DROP_W, TILES_DROP_H,
                "Tiles", out var tilesContent, out refs.TilesPanelDrag);

            var t = tilesContent;

            // Top row — horizontal split:
            //   • LEFT  : SELECTED preview + RULESET button stacked vertically.
            //   • RIGHT : CATEGORIES section label + scroll list.
            // Same height on both sides keeps the panel surface tidy and
            // frees vertical real estate for the TILES grid below.
            BuildTopRow(t, ref refs);
            BuildSeparator(t);

            // Tile grid
            BuildSectionLabel(t, "TILES");
            // Tileset-view chrome (zoom slider + dedup toggle). Hidden until a
            // tilesheet category is selected; wired by TileEditorUI.Builder.cs
            // post-construction so the callbacks can bind to UI methods.
            BuildTilesetControls(t, ref refs, null, null);
            BuildTilePicker(t, ref refs);
            BuildTileCountRow(t, ref refs);

            // Constrain every chrome row to its initial size so resizing the
            // panel only grows the TILES picker — see comment on the constant.
            ApplyTilesPanelResizePolicy(t, refs);

            // Bottom-right drag-to-resize handle (matches Spells Editor pattern).
            BuildTilesResizeHandle(refs.TilesDropdown);

            refs.TilesDropdown.SetActive(false);
        }

        /// <summary>
        /// Builds the horizontal top row of the Tiles panel:
        ///   • left column  → SELECTED preview + CONFIGURE TILESET button.
        ///   • right column → CATEGORIES label + scrollable category list.
        /// Row height is content-driven (<see cref="TILES_TOP_ROW_MIN_H"/>
        /// floor, soft-capped by the inner CATEGORIES scroll's own
        /// <see cref="TILES_CAT_SCROLL_MAX_H"/>) — both columns report
        /// preferredHeight from their content so the row only takes the room
        /// it actually needs and the picker below recovers vertical space.
        /// </summary>
        private static void BuildTopRow(Transform parent, ref UIRefs refs)
        {
            var row = CreateUI("TopRow", parent);
            refs.TilesTopRow = row;
            var le  = row.AddComponent<LayoutElement>();
            // Don't pin a fixed preferredHeight — we want the row to size to
            // max(LEFT.preferredHeight, RIGHT.preferredHeight) so it shrinks
            // when CATEGORIES is short. minHeight gives us a sane floor; the
            // ceiling is enforced separately via LayoutElementFollowsChildHeight
            // wired below.
            le.minHeight       = TILES_TOP_ROW_MIN_H;
            le.preferredHeight = -1f;
            // CRITICAL: pin flexibleHeight = 0 so the panel-content VLG never
            // distributes extra panel space INTO this row. Without this hard
            // pin, any nested element with flex > 0 (e.g. the SELECTED Name TMP
            // before we fixed it) propagates flex upward through both VLGs and
            // the row HLG; the panel VLG then siphons the picker's surplus
            // space into the TopRow, leaving the picker undersized and a fat
            // empty band between CATEGORIES and the TILES section. With
            // flexibleHeight pinned, all surplus goes to the only child that
            // still publishes flex > 0 — the TILES picker (flex=1).
            le.flexibleHeight  = 0f;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = TILES_TOP_GAP;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.padding                = new RectOffset(0, 0, 0, 0);

            BuildTopRowLeftColumn(row.transform, ref refs);
            BuildTopRowRightColumn(row.transform, ref refs);
        }

        private static void BuildTopRowLeftColumn(Transform parent, ref UIRefs refs)
        {
            var col = CreateUI("TopRowLeft", parent);
            var le  = col.AddComponent<LayoutElement>();
            le.preferredWidth = TILES_TOP_LEFT_W;
            le.flexibleWidth  = 0f;

            var vlg = col.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 4f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.padding                = new RectOffset(0, 0, 0, 0);

            BuildSelectedTilePreview(col.transform, ref refs);
            BuildConfigureRow(col.transform, ref refs);
        }

        private static void BuildTopRowRightColumn(Transform parent, ref UIRefs refs)
        {
            var col = CreateUI("TopRowRight", parent);
            var le  = col.AddComponent<LayoutElement>();
            // flexibleWidth=1 → absorb whatever space is left in the row after
            // the fixed-width left column and the inter-column gap.
            le.flexibleWidth = 1f;

            var vlg = col.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 2f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.padding                = new RectOffset(0, 0, 0, 0);

            BuildSectionLabel(col.transform, "CATEGORIES");
            BuildCategoryScroll(col.transform, ref refs);
        }

        /// <summary>
        /// Pin every chrome row in the Tiles panel to a fixed width and height
        /// (no flex), with two horizontally-flexible exceptions: the TILES
        /// picker and the top row (so CATEGORIES can reflow into multi-column
        /// when the panel widens). Disables the shared
        /// <see cref="VerticalLayoutGroup.childForceExpandWidth"/> flag locally
        /// so the chrome rows actually honour their preferredWidth — without
        /// this pass the panel VLG would stretch every row with the panel.
        /// </summary>
        private static void ApplyTilesPanelResizePolicy(Transform tilesContent, UIRefs refs)
        {
            var vlg = tilesContent.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.childForceExpandWidth = false;

            var pickerGo = refs.TileScrollRect != null ? refs.TileScrollRect.gameObject : null;
            var topRowGo = refs.TilesTopRow;

            foreach (Transform child in tilesContent)
            {
                var le = child.GetComponent<LayoutElement>();
                if (le == null) continue;

                bool isPicker = pickerGo != null && child.gameObject == pickerGo;
                bool isTopRow = topRowGo != null && child.gameObject == topRowGo;

                if (isPicker || isTopRow)
                {
                    // Both grow horizontally with the panel:
                    //   • the picker shows more tiles per row,
                    //   • the top row's right column widens → CATEGORIES
                    //     reflows into 2/3/N columns via GridAutoSize.
                    le.preferredWidth = TILES_PANEL_ROW_W;
                    le.flexibleWidth  = 1f;
                    // flexibleHeight on the picker is already 1 from BuildTilePicker;
                    // the top row's preferredHeight stays content-driven (set
                    // to -1 in BuildTopRow) so it shrinks when the CATEGORIES
                    // scroll only needs one row.
                }
                else
                {
                    // Chrome row (separator, "TILES" section label, zoom controls,
                    // footer). Keep its preferredHeight; pin the width and zero
                    // out flex on both axes.
                    le.preferredWidth = TILES_PANEL_ROW_W;
                    le.flexibleWidth  = 0f;
                    le.flexibleHeight = 0f;
                }
            }
        }

        /// <summary>
        /// Adds a small triangular drag handle to the bottom-right corner of the
        /// Tiles panel that lets the user resize it. The panel itself uses a
        /// top-left pivot (set in <see cref="MakeDropdownPanel"/>) which is
        /// what <see cref="PanelResizeHandle"/> expects.
        /// </summary>
        private static void BuildTilesResizeHandle(GameObject panelRoot)
        {
            var panelRt = panelRoot.GetComponent<RectTransform>();
            if (panelRt == null) return;

            var go = CreateUI("ResizeHandle", panelRoot.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 0f);
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(1f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(TILES_RESIZE_HANDLE_PX, TILES_RESIZE_HANDLE_PX);

            // The triangle visual sits just inside the corner — same colour as
            // the panel border so it reads as a chrome element, not content.
            var tri = go.AddComponent<TriangleHandleGraphic>();
            tri.color         = TileEditorTheme.Border;
            tri.raycastTarget = true;

            var handle = go.AddComponent<PanelResizeHandle>();
            handle.Target  = panelRt;
            handle.MinSize = TILES_PANEL_MIN_SIZE;
            handle.MaxSize = TILES_PANEL_MAX_SIZE;
        }

        private static void BuildSelectedTilePreview(Transform parent, ref UIRefs refs)
        {
            var row = CreateUI("SelectedPreview", parent);
            // flexibleHeight = 0 pins the row at preferredHeight even when the
            // parent column has leftover vertical space (e.g. when the
            // CATEGORIES scroll on the right side stretches the TopRow taller).
            // Without this the row inherits flexibleHeight=1 from the "Name" TMP
            // inside Info → VLG of the LEFT column distributes the leftover
            // pixels into the row → the yellow Img outline ends up tall and
            // narrow instead of a 40 × 40 thumbnail.
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 48f;
            rowLe.minHeight       = 48f;
            rowLe.flexibleHeight  = 0f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 4, 4);

            var imgGo = CreateUI("Img", row.transform);
            // Pin the thumbnail to a square 40 × 40 footprint regardless of how
            // tall the parent row ends up — flexibleWidth/Height = 0 stops the
            // HLG from stretching the slot into a non-square rectangle.
            var imgLe = imgGo.AddComponent<LayoutElement>();
            imgLe.preferredWidth  = 40f;
            imgLe.preferredHeight = 40f;
            imgLe.minWidth        = 40f;
            imgLe.minHeight       = 40f;
            imgLe.flexibleWidth   = 0f;
            imgLe.flexibleHeight  = 0f;
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

        // Horizontal padding inside the CONFIGURE button (between bg edge and label).
        // Button width = label preferredWidth + 2 * this.
        private const int CONFIGURE_BTN_INNER_PAD_X = 10;

        private static void BuildConfigureRow(Transform parent, ref UIRefs refs)
        {
            var row = CreateUI("ConfigureRow", parent);
            // flexibleHeight = 0 stops the row from absorbing leftover vertical
            // space in the LEFT column when the parent TopRow grows tall (e.g.
            // because the CATEGORIES scroll on the right is using its full cap).
            // Without this the button would float toward the bottom of the row
            // — visually disconnected from the SELECTED preview above it.
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 26f;
            rowLe.minHeight       = 26f;
            rowLe.flexibleHeight  = 0f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            // childForceExpandWidth = false + childAlignment = MiddleLeft so the
            // button sits at its own preferred width on the left edge of the row
            // instead of being stretched to fill the column. Lets the button text
            // ("CONFIGURE: castle_pandora", "NO RULESET FOR CATEGORY", …) drive
            // the visible button surface area and saves horizontal real estate.
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.padding = new RectOffset(4, 4, 2, 2);

            var btnGo = CreateUI("ConfigBtn", row.transform);
            var img = btnGo.AddComponent<Image>();
            img.color = BTN_NORMAL;
            refs.ConfigureTilesetBtn = btnGo.AddComponent<Button>();
            var bc = refs.ConfigureTilesetBtn.colors;
            bc.normalColor = BTN_NORMAL;
            bc.highlightedColor = BTN_HOVER;
            bc.pressedColor = BTN_ACTIVE;
            refs.ConfigureTilesetBtn.colors = bc;
            refs.ConfigureTilesetBtn.targetGraphic = img;

            // Layout group + ContentSizeFitter so the button's RectTransform
            // grows / shrinks horizontally to match the TMP label's preferred
            // text width on every refresh. Vertical stays fixed (parent HLG
            // forces height = row height = 26 px). The label is added as a
            // layout-controlled child (not stretch-anchored) — TMP implements
            // ILayoutElement so its preferredWidth bubbles up to CSF.
            var btnHlg = btnGo.AddComponent<HorizontalLayoutGroup>();
            btnHlg.padding = new RectOffset(
                CONFIGURE_BTN_INNER_PAD_X, CONFIGURE_BTN_INNER_PAD_X, 2, 2);
            btnHlg.spacing                = 0f;
            btnHlg.childForceExpandWidth  = false;
            btnHlg.childForceExpandHeight = true;
            btnHlg.childControlWidth      = true;
            btnHlg.childControlHeight     = true;
            btnHlg.childAlignment         = TextAnchor.MiddleCenter;

            var btnCsf = btnGo.AddComponent<ContentSizeFitter>();
            btnCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            btnCsf.verticalFit   = ContentSizeFitter.FitMode.Unconstrained;

            var lblGo = CreateUI("Lbl", btnGo.transform);
            refs.ConfigureTilesetBtnLabel = lblGo.AddComponent<TextMeshProUGUI>();
            refs.ConfigureTilesetBtnLabel.text          = "CONFIGURE TILESET";
            refs.ConfigureTilesetBtnLabel.fontSize      = 11f;
            refs.ConfigureTilesetBtnLabel.fontStyle     = FontStyles.Bold;
            refs.ConfigureTilesetBtnLabel.color         = TEXT_SECONDARY;
            refs.ConfigureTilesetBtnLabel.alignment     = TextAlignmentOptions.Center;
            refs.ConfigureTilesetBtnLabel.raycastTarget = false;
            // Single-line; no wrap — button width MUST equal label preferredWidth
            // for CSF to converge. Word-wrap would make TMP report a shrunken
            // preferredWidth and the button would oscillate.
            refs.ConfigureTilesetBtnLabel.enableWordWrapping = false;
            refs.ConfigureTilesetBtnLabel.overflowMode       = TextOverflowModes.Overflow;
        }

        // Hard ceiling on the auto-resized CATEGORIES scroll height. Beyond this
        // the scroll engages and the user pans through the rest. Sized so that
        // even with the minimum cell height (22 px + 3 px spacing) the user can
        // see at least 5 rows of categories before scrolling — enough for the
        // common case of 12-15 categories at 3 columns ≈ 5 rows tall.
        private const float TILES_CAT_SCROLL_MAX_H = 130f;
        // Floor — keep the scrollbar interactable even when only one row exists.
        private const float TILES_CAT_SCROLL_MIN_H = TILES_CAT_BTN_H + 6f;

        private static void BuildCategoryScroll(Transform parent, ref UIRefs refs)
        {
            var scrollGo = CreateUI("CatScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            // Auto-size the scroll's height to fit the grid content (clamped by
            // TILES_CAT_SCROLL_MIN/MAX_H) instead of stretching to fill the
            // top-row column. When the categories list is short the scroll
            // collapses tight against the section label, freeing vertical real
            // estate for the picker below. The grid's GridAutoSize already
            // drives content.rect.height; LayoutElementFollowsChildHeight
            // mirrors that into le.preferredHeight.
            le.flexibleHeight = 0f;
            le.minHeight      = TILES_CAT_SCROLL_MIN_H;
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
            // Initial cellSize / constraintCount serve only as a fallback for
            // the very first frame before GridAutoSize.OnEnable runs. The
            // responsive sizer below overwrites both based on the column's
            // actual width, reflowing the list into 1/2/3+ columns as the
            // panel grows.
            gl.cellSize        = new Vector2(TILES_CAT_BTN_MIN_W, TILES_CAT_BTN_H);
            gl.spacing         = new Vector2(TILES_CAT_GRID_SPACING, TILES_CAT_GRID_SPACING);
            gl.padding         = new RectOffset(3, 3, 2, 2);
            gl.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 1;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Responsive sizer: recomputes cellSize.x + constraintCount whenever
            // the content RectTransform width changes (panel resize → top-row
            // right column widens → more category columns fit). Cells stay at
            // the fixed 22 px row height set via CellHeightOverride.
            var autoSize = content.AddComponent<GridAutoSize>();
            autoSize.MinCellSize        = TILES_CAT_BTN_MIN_W;
            autoSize.MaxCellSize        = TILES_CAT_BTN_MAX_W;
            autoSize.Spacing            = TILES_CAT_GRID_SPACING;
            autoSize.CellHeightOverride = TILES_CAT_BTN_H;
            autoSize.Padding            = new RectOffset(3, 3, 2, 2);

            // Scrollbar (always visible) — same look as the tile picker
            BuildVerticalScrollbar(scrollGo.transform, sr);

            sr.content = cr;
            sr.viewport = vpRt;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // Mirror the grid content's measured height into the scroll's
            // LayoutElement.preferredHeight so the scroll shrinks tight when
            // the grid only has 1-2 rows and grows up to the cap when there
            // are many categories. Without this the scroll would stretch via
            // flexibleHeight=1 and waste vertical space above the picker.
            var follow = scrollGo.AddComponent<LayoutElementFollowsChildHeight>();
            follow.SourceContent = cr;
            follow.MinHeight     = TILES_CAT_SCROLL_MIN_H;
            follow.MaxHeight     = TILES_CAT_SCROLL_MAX_H;
            // 4 px on each side — matches the GridLayoutGroup vertical padding
            // (top 2 + bottom 2) so the scroll surface doesn't clip cell borders.
            follow.ExtraPadding  = 4f;
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
            // Horizontal is enabled by default so tilesheet view (24+ cols at zoom)
            // can pan sideways. Legacy 4-column categories still fit width-wise so
            // the user only sees a vertical scroll there.
            refs.TileScrollRect.horizontal = true;
            refs.TileScrollRect.vertical = true;
            refs.TileScrollRect.scrollSensitivity = 24f;
            refs.TileScrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Viewport: leave room on the right for the vertical scrollbar AND
            // on the bottom for the horizontal scrollbar (added below). Both
            // bars are permanently visible so the picker visually communicates
            // "you can pan in two axes" — important for tilesheet view (24+ cols).
            var vp = CreateUI("VP", scrollGo.transform);
            var vpRt = vp.GetComponent<RectTransform>();
            vpRt.anchorMin = new Vector2(0f, 0f);
            vpRt.anchorMax = new Vector2(1f, 1f);
            vpRt.pivot = new Vector2(0f, 1f);
            vpRt.offsetMin = new Vector2(0f, TILES_SCROLLBAR_W);
            vpRt.offsetMax = new Vector2(-TILES_SCROLLBAR_W, 0f);
            vp.AddComponent<RectMask2D>();

            // Content grid: anchored to top-left so it can grow both rightward (when
            // a tilesheet category exceeds the 4-col width) and downward.
            var content = CreateUI("Content", vp.transform);
            refs.TileGridContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(0f, 1f);
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
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Vertical + horizontal scrollbars (always visible for navigability).
            // The vertical bar reserves bottom space matching the horizontal
            // bar's height so they don't overlap at the corner. The horizontal
            // bar mirrors the same trick on the right edge for the vertical bar.
            BuildVerticalScrollbar(scrollGo.transform, refs.TileScrollRect, TILES_SCROLLBAR_W);
            BuildHorizontalScrollbar(scrollGo.transform, refs.TileScrollRect, TILES_SCROLLBAR_W);

            refs.TileScrollRect.content = cr;
            refs.TileScrollRect.viewport = vpRt;
            refs.TileScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            refs.TileScrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        /// <summary>Builds a thin, always-visible vertical scrollbar pinned to the right edge of the parent
        /// scroll container and wires it into the supplied ScrollRect. Visual style matches the editor accent palette.</summary>
    }
}