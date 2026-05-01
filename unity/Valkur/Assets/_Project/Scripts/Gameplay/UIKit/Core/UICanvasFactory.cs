using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;

namespace Valkur.UIKit
{
    /// <summary>
    /// Builds the screen-space overlay Canvas used by every editor and HUD.
    /// Centralises the standard reference resolution and the Layer=UI(5)
    /// recursive set so future canvases automatically pick the convention.
    /// </summary>
    public static class UICanvasFactory
    {
        /// <summary>
        /// Creates a Screen-Space Overlay canvas with the standard editor
        /// scaler (1600x800 reference resolution) and a `GraphicRaycaster`.
        /// All children are placed on the built-in UI layer so the Scene
        /// view's Layers dropdown can hide them.
        /// </summary>
        public static Canvas CreateOverlayCanvas(string name, int sortOrder = 100)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            go.AddComponent<GraphicRaycaster>();
            UILayerHelper.SetUILayerRecursive(go);
            return canvas;
        }
    }
}
