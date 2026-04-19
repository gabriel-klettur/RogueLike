using UnityEngine;
using UnityEngine.UI;

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

        private Canvas _canvas;

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
        }

        /// <summary>
        /// Kept for binary compatibility with <see cref="TileEditorManager.UpdateBorderToolLabel"/>.
        /// The center mode-label panel was removed (the tool name is already shown via the
        /// "Tools" dropdown in the menu bar), so this is now a no-op.
        /// </summary>
        public void SetToolLabel(string toolName) { }

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
