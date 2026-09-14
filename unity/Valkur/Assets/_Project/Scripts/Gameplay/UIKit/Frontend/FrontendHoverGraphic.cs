using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// "This is what you would pick": the channel's outline and a thin border in the tint, over a
    /// faint recess — never a fill.
    ///
    /// <para>Hover and selection must not look alike, or a mouse looks like it has already chosen.
    /// The selection is the bar FILLED; the hover is the bar's groove, empty.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FrontendHoverGraphic : MaskableGraphic
    {
        private Color _tint = new Color(0.95f, 0.75f, 0.35f, 1f);

        public Color Tint { get => _tint; set { if (_tint != value) { _tint = value; SetVerticesDirty(); } } }

        public static FrontendHoverGraphic Create(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var g = go.AddComponent<FrontendHoverGraphic>();
            g.raycastTarget = false;
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
            float x = r.xMin, y = r.yMin, w = r.width, h = r.height;
            if (w <= 4f || h <= 4f) return;
            FrontendMesh.QuadV(vh, x, y, w, h, FrontendMesh.WithAlpha(FrontendPalette.RecessBottom, 0.35f),
                               FrontendMesh.WithAlpha(FrontendPalette.RecessTop, 0.5f));
            Color edge = FrontendMesh.WithAlpha(_tint, 0.55f);
            Color faint = FrontendMesh.WithAlpha(_tint, 0.28f);
            FrontendMesh.Quad(vh, x, y + h - 1f, w, 1f, edge);
            FrontendMesh.Quad(vh, x, y, w, 1f, faint);
            FrontendMesh.QuadV(vh, x, y, 1f, h, faint, edge);
            FrontendMesh.QuadV(vh, x + w - 1f, y, 1f, h, faint, edge);
            FrontendMesh.Quad(vh, x + 1f, y, w - 2f, 1f, FrontendPalette.LipLight);
        }
    }
}
