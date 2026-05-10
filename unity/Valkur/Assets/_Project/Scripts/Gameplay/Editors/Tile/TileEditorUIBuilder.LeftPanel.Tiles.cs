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
        // Single fixed height for the whole row so the two columns visually align
        // and the panel surface above the picker stays compact (no dead space).
        private const float TILES_TOP_ROW_H   = 80f;
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
        /// Both columns share the same height (<see cref="TILES_TOP_ROW_H"/>)
        /// so the panel surface above the TILES grid has no dead space.
        /// </summary>
        private static void BuildTopRow(Transform parent, ref UIRefs refs)
        {
            var row = CreateUI("TopRow", parent);
            refs.TilesTopRow = row;
            var le  = row.AddComponent<LayoutElement>();
            le.preferredHeight = TILES_TOP_ROW_H;

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
                    // the top row keeps its fixed preferredHeight (TILES_TOP_ROW_H).
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

        private static void BuildConfigureRow(Transform parent, ref UIRefs refs)
        {
            var row = CreateUI("ConfigureRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
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
            refs.ConfigureTilesetBtnLabel = TileEditorUIHelpers.AddCenteredText(
                btnGo.transform, "CONFIGURE TILESET", 11f, FontStyles.Bold, TEXT_SECONDARY);
        }

        private static void BuildCategoryScroll(Transform parent, ref UIRefs refs)
        {
            var scrollGo = CreateUI("CatScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            // The scroll lives inside the top-row's right column whose height
            // is set by the parent HorizontalLayoutGroup. flexibleHeight=1 +
            // a small minHeight lets the scroll absorb whatever vertical space
            // is left after the section label, with a sane lower bound so the
            // scrollbar handle stays interactable when the panel is shrunk.
            le.flexibleHeight = 1f;
            le.minHeight      = 40f;
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