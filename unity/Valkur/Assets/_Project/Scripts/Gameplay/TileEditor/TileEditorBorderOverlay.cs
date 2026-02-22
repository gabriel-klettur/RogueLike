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
        private static readonly Color BorderColor = new Color(0.85f, 0.75f, 0.45f, 0.7f);
        private static readonly Color LabelBg = new Color(0.1f, 0.1f, 0.14f, 0.88f);

        private Canvas _canvas;
        private TextMeshProUGUI _modeLabel;

        public void Initialize()
        {
            var canvasGo = new GameObject("BorderOverlayCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 310;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            float borderThickness = 3f;

            // Top border
            CreateBorderStrip(canvasGo.transform, "TopBorder",
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0, borderThickness));

            // Bottom border
            CreateBorderStrip(canvasGo.transform, "BottomBorder",
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0, borderThickness));

            // Left border
            CreateBorderStrip(canvasGo.transform, "LeftBorder",
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(borderThickness, 0));

            // Right border
            CreateBorderStrip(canvasGo.transform, "RightBorder",
                new Vector2(1, 0), new Vector2(1, 1), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(borderThickness, 0));

            // Top-center mode label: "TILE EDITOR MODE"
            var labelPanel = CreateUIObj("ModeLabelPanel", canvasGo.transform);
            var panelRect = labelPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -6f);
            panelRect.sizeDelta = new Vector2(260f, 32f);

            var panelImg = labelPanel.AddComponent<Image>();
            panelImg.color = LabelBg;
            panelImg.raycastTarget = false;

            var outline = labelPanel.AddComponent<Outline>();
            outline.effectColor = BorderColor;
            outline.effectDistance = new Vector2(1f, 1f);

            var textGo = CreateUIObj("ModeText", labelPanel.transform);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            _modeLabel = textGo.AddComponent<TextMeshProUGUI>();
            _modeLabel.text = "TILE EDITOR MODE";
            _modeLabel.fontSize = 16f;
            _modeLabel.fontStyle = FontStyles.Bold;
            _modeLabel.alignment = TextAlignmentOptions.Center;
            _modeLabel.color = BorderColor;
            _modeLabel.raycastTarget = false;
        }

        public void SetToolLabel(string toolName)
        {
            if (_modeLabel != null)
                _modeLabel.text = $"TILE EDITOR — {toolName}";
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
