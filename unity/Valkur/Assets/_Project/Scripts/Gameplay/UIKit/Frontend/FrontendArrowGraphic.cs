using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// A stepper arrow in the bevel's metal: an outlined triangle lit from above, gold at its
    /// upper edge and bronze below, pointing left or right. It is the loading bar's gem cut in
    /// half — the same outline, the same two metals — so the Video rows' arrows read as part of
    /// the same object as the panel around them.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FrontendArrowGraphic : MaskableGraphic
    {
        private bool _left;

        public bool PointsLeft { get => _left; set { if (_left != value) { _left = value; SetVerticesDirty(); } } }

        public static FrontendArrowGraphic Create(Transform parent, string name, bool left)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<FrontendArrowGraphic>();
            g._left = left;
            return g;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Build(vh);
            FrontendMesh.ApplyGraphicColor(vh, color);
        }

        private void Build(VertexHelper vh)
        {
            var r = rectTransform.rect;
            float cx = r.center.x, cy = r.center.y;
            float hw = r.width * 0.5f - 2f, hh = r.height * 0.5f - 2f;
            if (hw <= 1f || hh <= 1f) return;
            float dir = _left ? -1f : 1f;
            // Outline triangle first, then the metal one inset, then a dark notch toward the base.
            Triangle(vh, cx, cy, dir, hw + 2f, hh + 2.5f, FrontendPalette.Outline, FrontendPalette.Outline, FrontendPalette.Outline);
            Triangle(vh, cx, cy, dir, hw, hh, FrontendPalette.GoldLight, FrontendPalette.BronzeDark,
                     Color.Lerp(FrontendPalette.GoldLight, FrontendPalette.BronzeDark, 0.35f));
        }

        private static void Triangle(VertexHelper vh, float cx, float cy, float dir, float hw, float hh,
                                     Color top, Color bottom, Color tip)
        {
            var baseTop = new Vector2(cx - dir * hw, cy + hh);
            var baseBottom = new Vector2(cx - dir * hw, cy - hh);
            var point = new Vector2(cx + dir * hw, cy);
            FrontendMesh.Triangle(vh, baseTop, point, baseBottom, top, tip, bottom);
        }
    }
}
