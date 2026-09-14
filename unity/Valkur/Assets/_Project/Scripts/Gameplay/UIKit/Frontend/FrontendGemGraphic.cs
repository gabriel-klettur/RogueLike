using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// A single gem: the loading bar's end gem on its own. It is a toggle that is ON when lit, a
    /// slider's handle, a card's corner. The radius follows the smaller side of the rect.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FrontendGemGraphic : MaskableGraphic
    {
        private float _lit;
        private Color _tint = new Color(0.95f, 0.75f, 0.35f, 1f);

        /// <summary>0..1: dim stone to the tint's hot core.</summary>
        public float Lit { get => _lit; set { value = Mathf.Clamp01(value); if (Mathf.Abs(_lit - value) > 0.002f) { _lit = value; SetVerticesDirty(); } } }

        public Color Tint { get => _tint; set { if (_tint != value) { _tint = value; SetVerticesDirty(); } } }

        public static FrontendGemGraphic Create(Transform parent, string name, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<FrontendGemGraphic>();
            g.raycastTarget = false;
            g._tint = tint;
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
            float radius = Mathf.Min(r.width, r.height) * 0.5f - 2.5f;
            if (radius <= 1f) return;
            FrontendDraw.Gem(vh, r.center.x, r.center.y, radius, _lit, _tint);
        }
    }
}
