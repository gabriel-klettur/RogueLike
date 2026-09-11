using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Places a RectTransform on whole texels, anchored and pivoted bottom-left, so every child
    /// of the panel's pixel space has integer corners. One helper rather than a copy of the four
    /// anchor lines at every call site, which is how one of them ends up with a centre pivot and
    /// every label under it lands half a texel off.
    /// </summary>
    public static class HudRect
    {
        public static void Place(RectTransform rt, int x, int y, int w, int h)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one;
        }

        /// <summary>A child that stretches over its parent, inset by whole texels.</summary>
        public static void Fill(RectTransform rt, int inset = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.zero;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            rt.localScale = Vector3.one;
        }

        /// <summary>A new UI object with a RectTransform, placed on whole texels.</summary>
        public static RectTransform Make(string name, Transform parent, int x, int y, int w, int h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Place(rt, x, y, w, h);
            return rt;
        }

        /// <summary>A new Image, placed on whole texels, never a raycast target.</summary>
        public static Image MakeImage(string name, Transform parent, Sprite sprite, int x, int y, int w, int h,
                                      Image.Type type = Image.Type.Simple)
        {
            var rt = Make(name, parent, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            img.raycastTarget = false;
            if (type == Image.Type.Sliced) img.fillCenter = true;
            img.pixelsPerUnitMultiplier = 1f;
            return img;
        }
    }
}
