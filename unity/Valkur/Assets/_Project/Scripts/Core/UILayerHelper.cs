using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Assigns the built-in "UI" layer (5) to a GameObject hierarchy so that
    /// Screen-Space Overlay canvases can be hidden in the Scene view via
    /// the Layers dropdown without affecting Game view rendering.
    /// </summary>
    public static class UILayerHelper
    {
        private const int UI_LAYER = 5;

        public static void SetUILayerRecursive(GameObject root)
        {
            root.layer = UI_LAYER;
            foreach (Transform child in root.transform)
                SetUILayerRecursive(child.gameObject);
        }
    }
}
