using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Screen-space border overlay that provides visual feedback when the tile editor is active.
    /// Draws a gold/amber border around the screen edges + a top-center mode label.
    /// Maps to Python's tile editor outline + indicator rendering.
    /// </summary>
    public class TileEditorBorderOverlay : MonoBehaviour
    {
        private static readonly Color BorderColor = new Color(0.90f, 0.76f, 0.38f, 0.50f);
        private static readonly Color LabelBg = new Color(0.09f, 0.09f, 0.12f, 0.92f);
        private static readonly Color LabelBorder = new Color(0.90f, 0.76f, 0.38f, 0.35f);
        private static readonly Color AccentText = new Color(0.90f, 0.76f, 0.38f, 1f);
        private static readonly Color MutedText = new Color(0.55f, 0.57f, 0.62f, 1f);

        private Canvas _canvas;
        private TextMeshProUGUI _modeLabel;
        private TextMeshProUGUI _hintLabel;

        public void Initialize()
        {
            var canvasGo = new GameObject("BorderOverlayCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 310;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            float borderThickness = 2f;

            CreateBorderStrip(canvasGo.transform, "TopBorder",
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0, borderThickness));
            CreateBorderStrip(canvasGo.transform, "BottomBorder",
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0, borderThickness));
            CreateBorderStrip(canvasGo.transform, "LeftBorder",
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(borderThickness, 0));
            CreateBorderStrip(canvasGo.transform, "RightBorder",
                new Vector2(1, 0), new Vector2(1, 1), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(borderThickness, 0));

            // Top-center label panel
            var labelPanel = CreateUIObj("ModeLabelPanel", canvasGo.transform);
            var panelRect = labelPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -4f);
            panelRect.sizeDelta = new Vector2(320f, 28f);

            var panelImg = labelPanel.AddComponent<Image>();
            panelImg.color = LabelBg;
            panelImg.raycastTarget = false;

            var outline = labelPanel.AddComponent<Outline>();
            outline.effectColor = LabelBorder;
            outline.effectDistance = new Vector2(1f, 1f);

            // Horizontal layout: mode label + hint
            var hl = labelPanel.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.padding = new RectOffset(12, 12, 0, 0);
            hl.spacing = 8f;

            var modeGo = CreateUIObj("ModeText", labelPanel.transform);
            modeGo.AddComponent<LayoutElement>().flexibleWidth = 0f;
            _modeLabel = modeGo.AddComponent<TextMeshProUGUI>();
            _modeLabel.text = "TILE EDITOR";
            _modeLabel.fontSize = 13f;
            _modeLabel.fontStyle = FontStyles.Bold;
            _modeLabel.alignment = TextAlignmentOptions.Center;
            _modeLabel.color = AccentText;
            _modeLabel.raycastTarget = false;
            _modeLabel.enableAutoSizing = false;

            var hintGo = CreateUIObj("HintText", labelPanel.transform);
            hintGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _hintLabel = hintGo.AddComponent<TextMeshProUGUI>();
            _hintLabel.text = "F6 close";
            _hintLabel.fontSize = 10f;
            _hintLabel.alignment = TextAlignmentOptions.Right;
            _hintLabel.color = MutedText;
            _hintLabel.raycastTarget = false;
        }

        public void SetToolLabel(string toolName)
        {
            if (_modeLabel != null)
                _modeLabel.text = $"TILE EDITOR \u2014 {toolName}";
        }

        private void CreateBorderStrip(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = CreateUIObj(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            var img = go.AddComponent<Image>();
            img.color = BorderColor;
            img.raycastTarget = false;
        }

        private static GameObject CreateUIObj(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
