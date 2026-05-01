using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Low-level GameObject + RectTransform builders shared across every
    /// editor and HUD widget. Anything that creates an empty UI node, a
    /// stretch fill, or a vertical scroll view goes through here.
    /// </summary>
    public static class UIFactory
    {
        public static GameObject CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static void StretchFill(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.sizeDelta = Vector2.zero;
        }

        /// <summary>Creates a ScrollView with a VerticalLayoutGroup content.</summary>
        public static (ScrollRect scroll, RectTransform content) MakeScrollView(
            Transform parent, string name, float height = 0f)
        {
            var scrollGo = CreateUI(name, parent);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero; scrollRt.anchorMax = Vector2.one;
            scrollRt.sizeDelta = Vector2.zero;
            if (height > 0f) scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1f;

            scrollGo.AddComponent<RectMask2D>();
            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = UITheme.BG_SURFACE;

            var viewport = CreateUI("Viewport", scrollGo.transform);
            StretchFill(viewport);

            var content = CreateUI("Content", viewport.transform);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot     = new Vector2(0.5f, 1);
            contentRt.sizeDelta = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.content = contentRt;
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 20f;

            return (sr, contentRt);
        }

        /// <summary>
        /// Adds a thin vertical scrollbar styled with the kit's accent palette
        /// to an existing ScrollRect. Offsets the viewport so it does not
        /// overlap the scrollbar and pins visibility to Permanent.
        /// </summary>
        public static Scrollbar AddVerticalScrollbar(ScrollRect scrollRect, float sbWidth = 12f)
        {
            var vpRt = scrollRect.viewport;
            vpRt.offsetMax = new Vector2(-sbWidth, vpRt.offsetMax.y);

            var sbGo = CreateUI("VScrollbar", scrollRect.transform);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin        = new Vector2(1f, 0f);
            sbRt.anchorMax        = new Vector2(1f, 1f);
            sbRt.pivot            = new Vector2(1f, 1f);
            sbRt.sizeDelta        = new Vector2(sbWidth, 0f);
            sbRt.anchoredPosition = Vector2.zero;
            sbGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.85f);

            var scrollbar       = sbGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slidingArea = CreateUI("SlidingArea", sbGo.transform);
            var saRt        = slidingArea.GetComponent<RectTransform>();
            saRt.anchorMin  = Vector2.zero;
            saRt.anchorMax  = Vector2.one;
            saRt.offsetMin  = new Vector2(2f,  2f);
            saRt.offsetMax  = new Vector2(-2f, -2f);

            var handleGo  = CreateUI("Handle", slidingArea.transform);
            var hRt       = handleGo.GetComponent<RectTransform>();
            hRt.anchorMin = Vector2.zero;
            hRt.anchorMax = Vector2.one;
            hRt.offsetMin = Vector2.zero;
            hRt.offsetMax = Vector2.zero;
            var hImg      = handleGo.AddComponent<Image>();
            hImg.color    = new Color(0.55f, 0.45f, 0.22f, 0.85f);
            scrollbar.targetGraphic = hImg;
            scrollbar.handleRect    = hRt;

            var sbColors              = scrollbar.colors;
            sbColors.normalColor      = new Color(0.55f, 0.45f, 0.22f, 0.85f);
            sbColors.highlightedColor = new Color(0.75f, 0.62f, 0.30f, 0.95f);
            sbColors.pressedColor     = new Color(0.90f, 0.76f, 0.38f, 1f);
            scrollbar.colors          = sbColors;

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            return scrollbar;
        }
    }
}
