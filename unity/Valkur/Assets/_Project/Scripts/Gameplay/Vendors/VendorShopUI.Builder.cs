using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.Gameplay.NPC
{
    public partial class VendorShopUI
    {
        // ------------------------------------------------------------------
        // UI Construction
        // ------------------------------------------------------------------

        private partial void BuildUI()
        {
            // Canvas
            var canvasGo = new GameObject("VendorShopCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the chat panel (200), because the shop is opened FROM a conversation and
            // has to draw over it. Both sat at 200, which left the winner to be decided by
            // hierarchy order — i.e. by whichever bootstrap step happened to run first.
            _canvas.sortingOrder = 220;
            // ScaleWithScreenSize, matching ChatUI. The default (ConstantPixelSize) pins the
            // shop to physical pixels, so on a small window a 664-wide panel does not fit and
            // on a large one it is a postage stamp — and the two panels, opened one from the
            // other, would scale differently.
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 800f);

            canvasGo.AddComponent<GraphicRaycaster>();

            // Root panel
            _root = CreatePanel(canvasGo.transform, new Vector2(panelWidth * 2f + 24f, panelHeight + 80f),
                Vector2.zero, bgColor, "VendorShopRoot");

            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;

            // Title bar — pinned to the top edge of the root.
            //
            // The anchors are set BEFORE the position, and that ordering is the whole fix.
            // anchoredPosition is measured FROM the anchor, so the old code — which placed
            // the bar at +262 against the default centre anchor and only then moved the
            // anchor to the top edge — left it 262 px ABOVE the panel, off screen. The gold
            // bar below had the same bug mirrored, which is why neither the NPC's name nor
            // the player's coin count was ever visible in this window.
            var titleBar = CreatePanel(_root.transform, new Vector2(panelWidth * 2f + 24f, 36f),
                Vector2.zero, new Color(0.04f, 0.04f, 0.06f, 1f), "TitleBar");
            var titleBarRect = titleBar.GetComponent<RectTransform>();
            titleBarRect.anchorMin = titleBarRect.anchorMax = new Vector2(0.5f, 1f);
            titleBarRect.pivot = new Vector2(0.5f, 1f);
            titleBarRect.anchoredPosition = Vector2.zero;

            _vendorTitleText = CreateLabel(titleBar.transform, "Shop", 14, titleColor, true);
            var titleRect = _vendorTitleText.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            // Inset on the right so a long vendor name cannot run under the close button.
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = new Vector2(-36f, 0f);

            // Close button. Escape already closes the shop, but a modal with no visible way
            // out reads as stuck — and this one is opened from inside a conversation, so the
            // player arrives here without having pressed anything they can undo.
            var closeButton = CreateButton(titleBar.transform, "X", 14,
                new Color(0.45f, 0.14f, 0.14f, 1f),
                new Vector2(1f, 0f), new Vector2(1f, 1f));
            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(30f, 0f);
            closeRect.anchoredPosition = new Vector2(-3f, 0f);
            closeButton.onClick.AddListener(() => SetVisible(false));

            // Gold display row — pinned to the bottom edge of the root. See the note above
            // on why the anchor comes first.
            var goldBar = CreatePanel(_root.transform, new Vector2(panelWidth * 2f + 24f, 30f),
                Vector2.zero, new Color(0.06f, 0.05f, 0.02f, 1f), "GoldBar");
            var goldBarRect = goldBar.GetComponent<RectTransform>();
            goldBarRect.anchorMin = goldBarRect.anchorMax = new Vector2(0.5f, 0f);
            goldBarRect.pivot = new Vector2(0.5f, 0f);
            goldBarRect.anchoredPosition = Vector2.zero;

            _goldText = CreateLabel(goldBar.transform, "Gold: 0", 12, goldColor, false);
            var gRect = _goldText.GetComponent<RectTransform>();
            gRect.anchorMin = Vector2.zero;
            gRect.anchorMax = Vector2.one;
            gRect.offsetMin = gRect.offsetMax = Vector2.zero;

            // Vendor stock panel (left)
            float colY = -(36f * 0.5f);
            var vendorPanel = CreatePanel(_root.transform, new Vector2(panelWidth, panelHeight - 20f),
                new Vector2(-(panelWidth * 0.5f + 6f), colY), new Color(0.1f, 0.1f, 0.14f, 1f), "VendorPanel");
            vendorPanel.GetComponent<RectTransform>().anchorMin = vendorPanel.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);

            var vendorLabel = CreateLabel(vendorPanel.transform, "Vendor Stock", 11, titleColor, true);
            var vlRect = vendorLabel.GetComponent<RectTransform>();
            vlRect.anchorMin = new Vector2(0f, 1f);
            vlRect.anchorMax = new Vector2(1f, 1f);
            vlRect.pivot = new Vector2(0.5f, 1f);
            vlRect.offsetMin = new Vector2(0f, -22f);
            vlRect.offsetMax = new Vector2(0f, 0f);
            vlRect.sizeDelta = new Vector2(0f, 22f);

            var vendorScroll = CreateScrollView(vendorPanel.transform, new Vector2(panelWidth - 8f, panelHeight - 50f),
                new Vector2(0f, -26f), "VendorScroll");
            _vendorRowsParent = vendorScroll.transform.Find("Viewport/Content");
            _vendorEmptyText = CreateEmptyState(vendorScroll.transform, "Hoy no tiene nada a la venta.");

            // Player inventory panel (right)
            var playerPanel = CreatePanel(_root.transform, new Vector2(panelWidth, panelHeight - 20f),
                new Vector2(panelWidth * 0.5f + 6f, colY), new Color(0.1f, 0.1f, 0.14f, 1f), "PlayerPanel");
            playerPanel.GetComponent<RectTransform>().anchorMin = playerPanel.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);

            var playerLabel = CreateLabel(playerPanel.transform, "Your Items", 11, titleColor, true);
            var plRect = playerLabel.GetComponent<RectTransform>();
            plRect.anchorMin = new Vector2(0f, 1f);
            plRect.anchorMax = new Vector2(1f, 1f);
            plRect.pivot = new Vector2(0.5f, 1f);
            plRect.offsetMin = new Vector2(0f, -22f);
            plRect.offsetMax = new Vector2(0f, 0f);
            plRect.sizeDelta = new Vector2(0f, 22f);

            var playerScroll = CreateScrollView(playerPanel.transform, new Vector2(panelWidth - 8f, panelHeight - 50f),
                new Vector2(0f, -26f), "PlayerScroll");
            _playerRowsParent = playerScroll.transform.Find("Viewport/Content");
            _playerEmptyText = CreateEmptyState(playerScroll.transform, "No llevas nada que vender.");
        }

        // ------------------------------------------------------------------
        // UI Helpers
        // ------------------------------------------------------------------

        private static GameObject CreatePanel(Transform parent, Vector2 size, Vector2 anchoredPos,
            Color color, string goName)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, int fontSize,
            Color color, bool centered)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = centered ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        private static Button CreateButton(Transform parent, string label, int fontSize,
            Color bgColor, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(bgColor.r + 0.1f, bgColor.g + 0.1f, bgColor.b + 0.1f, bgColor.a);
            colors.pressedColor = new Color(bgColor.r - 0.1f, bgColor.g - 0.1f, bgColor.b - 0.1f, bgColor.a);
            btn.colors = colors;

            var tmp = CreateLabel(go.transform, label, fontSize, Color.white, true);
            var tRect = tmp.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = tRect.offsetMax = Vector2.zero;

            return btn;
        }

        /// <summary>
        /// The line a column shows when it has no rows.
        ///
        /// An empty list and a broken one look identical when both are a black rectangle —
        /// which is exactly how this window read while the rows were being clipped away, and
        /// how the player column reads to anyone carrying nothing. A sentence costs one
        /// label and removes the ambiguity.
        /// </summary>
        private static TextMeshProUGUI CreateEmptyState(Transform scrollView, string message)
        {
            var label = CreateLabel(scrollView, message, 11, new Color(0.45f, 0.45f, 0.52f), true);
            var rect = label.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(8f, -46f);
            rect.offsetMax = new Vector2(-8f, -18f);
            label.raycastTarget = false;   // never intercept a click meant for a row
            return label;
        }

        /// <summary>Width of a list's scrollbar strip, and the inset the viewport gives it.</summary>
        private const float SCROLLBAR_WIDTH = 10f;

        private static GameObject CreateScrollView(Transform parent, Vector2 size,
            Vector2 anchoredPos, string goName)
        {
            var scrollGo = new GameObject(goName, typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            var sRect = scrollGo.GetComponent<RectTransform>();

            // Anchored to the TOP of the column, not its middle. With a (0.5, 0.5) anchor and
            // a top pivot the list hung from the panel's CENTRE, so on a 460-tall column the
            // rows began 230 px down and the last four fell straight out of the bottom of the
            // window onto the game world. Nobody had seen it: until the mask was fixed the
            // rows were clipped away entirely, so the column read as empty rather than as
            // misplaced.
            sRect.anchorMin = new Vector2(0.5f, 1f);
            sRect.anchorMax = new Vector2(0.5f, 1f);
            sRect.pivot = new Vector2(0.5f, 1f);
            sRect.sizeDelta = size;
            sRect.anchoredPosition = anchoredPos;

            // The transparent Image stays: it is the raycast target that lets the ScrollRect
            // receive the mouse wheel. An alpha of 0 does not stop a raycast — Graphic tests
            // the rect, not the pixel, unless alphaHitTestMinimumThreshold is raised.
            var raycastSurface = scrollGo.AddComponent<Image>();
            raycastSurface.color = new Color(0, 0, 0, 0);
            raycastSurface.raycastTarget = true;

            // RectMask2D, NOT Mask. A stencil Mask takes its SHAPE from its graphic's alpha,
            // and this one was paired with an Image at alpha 0 — the UI shader alpha-clips
            // those pixels away, the stencil is therefore never written, and every row
            // inside was clipped out of existence. The shop opened with its two column
            // headers and nothing under them, while the rows were all present, active and
            // correctly priced in the hierarchy: the one failure mode that looks like "the
            // vendor has no stock" and is invisible to any test that counts objects.
            //
            // A rect is the shape actually wanted here, and RectMask2D needs no graphic, no
            // stencil buffer and no extra draw call to describe one.
            scrollGo.AddComponent<RectMask2D>();

            // Viewport, inset on the right so the scrollbar has a strip of its own. Without
            // the inset the bar would sit on top of the Buy buttons and steal their clicks.
            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var vpRect = viewportGo.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = new Vector2(-SCROLLBAR_WIDTH, 0f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var cRect = contentGo.GetComponent<RectTransform>();
            cRect.anchorMin = new Vector2(0f, 1f);
            cRect.anchorMax = new Vector2(1f, 1f);
            cRect.pivot = new Vector2(0f, 1f);
            cRect.offsetMin = cRect.offsetMax = Vector2.zero;
            cRect.sizeDelta = new Vector2(0f, 0f);

            scrollRect.content = cRect;
            scrollRect.viewport = vpRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.verticalScrollbar = CreateVerticalScrollbar(scrollGo.transform);

            // AutoHide, so the bar is present exactly when there is more list than window.
            // That is the question it answers — "is there more below?" — and a permanently
            // drawn bar on a list of three items answers it wrongly. Not
            // AutoHideAndExpandViewport: that resizes the viewport as the bar appears, which
            // would reflow every row's width the moment an item is bought.
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            return scrollGo;
        }

        /// <summary>
        /// The bar down the right-hand edge of a list: a track, a sliding area and a handle,
        /// which is the minimum Unity's Scrollbar needs to size its handle from the content.
        /// </summary>
        private static Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            var barGo = new GameObject("Scrollbar", typeof(RectTransform));
            barGo.transform.SetParent(parent, false);
            var barRect = barGo.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 1f);
            barRect.offsetMin = new Vector2(-SCROLLBAR_WIDTH, 0f);
            barRect.offsetMax = Vector2.zero;

            var track = barGo.AddComponent<Image>();
            track.color = new Color(0.05f, 0.05f, 0.07f, 0.9f);

            var slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
            slidingArea.transform.SetParent(barGo.transform, false);
            var slideRect = slidingArea.GetComponent<RectTransform>();
            slideRect.anchorMin = Vector2.zero;
            slideRect.anchorMax = Vector2.one;
            slideRect.offsetMin = new Vector2(1f, 1f);
            slideRect.offsetMax = new Vector2(-1f, -1f);

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(slidingArea.transform, false);
            var handleRect = handleGo.GetComponent<RectTransform>();
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            var handleImage = handleGo.AddComponent<Image>();
            handleImage.color = new Color(0.42f, 0.42f, 0.50f, 1f);

            var scrollbar = barGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;

            var colors = scrollbar.colors;
            colors.highlightedColor = new Color(0.58f, 0.58f, 0.68f, 1f);
            colors.pressedColor = new Color(0.70f, 0.70f, 0.82f, 1f);
            scrollbar.colors = colors;

            return scrollbar;
        }
    }
}
