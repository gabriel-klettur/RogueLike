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
            _canvas.sortingOrder = 200;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Root panel
            _root = CreatePanel(canvasGo.transform, new Vector2(panelWidth * 2f + 24f, panelHeight + 80f),
                Vector2.zero, bgColor, "VendorShopRoot");

            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;

            // Title bar
            var titleBar = CreatePanel(_root.transform, new Vector2(panelWidth * 2f + 24f, 36f),
                new Vector2(0f, (panelHeight + 80f) * 0.5f - 18f), new Color(0.04f, 0.04f, 0.06f, 1f), "TitleBar");
            titleBar.GetComponent<RectTransform>().anchorMin = titleBar.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1f);

            _vendorTitleText = CreateLabel(titleBar.transform, "Shop", 14, titleColor, true);
            var titleRect = _vendorTitleText.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;

            // Gold display row
            var goldBar = CreatePanel(_root.transform, new Vector2(panelWidth * 2f + 24f, 30f),
                new Vector2(0f, -(panelHeight + 80f) * 0.5f + 15f), new Color(0.06f, 0.05f, 0.02f, 1f), "GoldBar");
            goldBar.GetComponent<RectTransform>().anchorMin = goldBar.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0f);

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

        private static GameObject CreateScrollView(Transform parent, Vector2 size,
            Vector2 anchoredPos, string goName)
        {
            var scrollGo = new GameObject(goName, typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            var sRect = scrollGo.GetComponent<RectTransform>();
            sRect.anchorMin = new Vector2(0.5f, 0.5f);
            sRect.anchorMax = new Vector2(0.5f, 0.5f);
            sRect.pivot = new Vector2(0.5f, 1f);
            sRect.sizeDelta = size;
            sRect.anchoredPosition = anchoredPos;

            scrollGo.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            scrollGo.AddComponent<Mask>().showMaskGraphic = false;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var vpRect = viewportGo.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = vpRect.offsetMax = Vector2.zero;

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

            return scrollGo;
        }
    }
}
