using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Panel builders shared by every editor sidebar and HUD floating window.
    /// Outlines and background fills are pulled from <see cref="UITheme"/>.
    /// </summary>
    public static class UIPanel
    {
        public static GameObject Make(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = UIFactory.CreateUI(name, parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = pivot;
            r.anchoredPosition = anchoredPos; r.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = UITheme.BG_PANEL;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = UITheme.BORDER; ol.effectDistance = new Vector2(1f, 1f);
            return go;
        }

        /// <summary>Left-anchored sidebar panel.</summary>
        public static GameObject MakeSidebar(string name, Transform parent, float width = 300f)
        {
            return Make(name, parent,
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(width, 0));
        }

        /// <summary>Right-anchored sidebar panel.</summary>
        public static GameObject MakeRightPanel(string name, Transform parent, float width = 300f)
        {
            return Make(name, parent,
                new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(0, 0), new Vector2(width, 0));
        }

        /// <summary>Adds a VLG with padding to a panel.</summary>
        public static VerticalLayoutGroup AddVLG(GameObject panel, int pad = 8, float spacing = 6f)
        {
            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(pad, pad, pad, pad);
            vlg.spacing = spacing;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return vlg;
        }
    }
}
