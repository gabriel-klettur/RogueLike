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

            TextMeshProUGUI titleTmp = null;
            if (!narrowPanel)
            {
                // Title text fills the full header width
                hdrHlg.padding = new RectOffset(8, 8, 0, 0);
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
            // else (narrowPanel): no title — header acts as drag handle only

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