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
    }
}