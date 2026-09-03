using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;
using Valkur.UIKit;

namespace Valkur.Gameplay.TileEditor
{
    public static partial class TileEditorUIBuilder
    {
        /// <summary>
        /// Builds a vertical scrollbar pinned to the right edge of <paramref name="scrollContainer"/>.
        /// Pass <paramref name="bottomReservedPx"/> > 0 when a horizontal scrollbar will sit
        /// underneath, so the vertical bar doesn't overlap it.
        /// </summary>
        private static void BuildVerticalScrollbar(Transform scrollContainer, ScrollRect targetScrollRect,
            float bottomReservedPx = 0f)
        {
            var sbGo = CreateUI("VScrollbar", scrollContainer);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot     = new Vector2(1f, 1f);
            sbRt.offsetMin = new Vector2(-TILES_SCROLLBAR_W, bottomReservedPx);
            sbRt.offsetMax = new Vector2(0f, 0f);
            var sbBg = sbGo.AddComponent<Image>();
            sbBg.color = UITheme.SCROLL_TRACK;
            var scrollbar = sbGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            BuildScrollbarHandle(sbGo.transform, scrollbar);

            targetScrollRect.verticalScrollbar = scrollbar;
        }

        /// <summary>
        /// Builds a horizontal scrollbar pinned to the bottom edge of <paramref name="scrollContainer"/>.
        /// Pass <paramref name="rightReservedPx"/> > 0 when a vertical scrollbar sits to the right,
        /// so the two bars don't overlap at the corner.
        /// </summary>
        private static void BuildHorizontalScrollbar(Transform scrollContainer, ScrollRect targetScrollRect,
            float rightReservedPx = 0f)
        {
            var sbGo = CreateUI("HScrollbar", scrollContainer);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(0f, 0f);
            sbRt.anchorMax = new Vector2(1f, 0f);
            sbRt.pivot     = new Vector2(0f, 0f);
            sbRt.offsetMin = new Vector2(0f, 0f);
            sbRt.offsetMax = new Vector2(-rightReservedPx, TILES_SCROLLBAR_W);
            var sbBg = sbGo.AddComponent<Image>();
            sbBg.color = UITheme.SCROLL_TRACK;
            var scrollbar = sbGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.LeftToRight;

            BuildScrollbarHandle(sbGo.transform, scrollbar);

            targetScrollRect.horizontalScrollbar = scrollbar;
        }

        /// <summary>
        /// Shared sliding-area + golden handle + colour palette used by both
        /// vertical and horizontal scrollbars in the Tile Editor.
        /// </summary>
        private static void BuildScrollbarHandle(Transform scrollbarRoot, Scrollbar scrollbar)
        {
            var slidingArea = CreateUI("SlidingArea", scrollbarRoot);
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
            hImg.color = UITheme.SCROLL_HANDLE;
            scrollbar.targetGraphic = hImg;
            scrollbar.handleRect = hRt;
            var sbColors = scrollbar.colors;
            sbColors.normalColor      = UITheme.SCROLL_HANDLE;
            sbColors.highlightedColor = new Color(0.75f, 0.62f, 0.30f, 0.95f);
            sbColors.pressedColor     = UITheme.ACCENT;
            scrollbar.colors = sbColors;
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
    }
}