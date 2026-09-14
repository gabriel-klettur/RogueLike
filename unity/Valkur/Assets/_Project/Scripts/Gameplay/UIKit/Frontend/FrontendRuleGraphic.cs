using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// A divider: a dark groove with a warm light line beside it, ending in a diamond notch at
    /// each end — the loading bar's etapa divider laid flat across a panel.
    ///
    /// <para>Horizontal rules sit on the rect's vertical centre; vertical ones on its horizontal
    /// centre. The rect only has to be as long as the rule; its thickness is ignored.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FrontendRuleGraphic : MaskableGraphic
    {
        private bool _vertical;
        private Color _tint = new Color(0.95f, 0.75f, 0.35f, 1f);

        public bool Vertical { get => _vertical; set { if (_vertical != value) { _vertical = value; SetVerticesDirty(); } } }
        public Color Tint { get => _tint; set { if (_tint != value) { _tint = value; SetVerticesDirty(); } } }

        public static FrontendRuleGraphic Create(Transform parent, string name, bool vertical, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<FrontendRuleGraphic>();
            g.raycastTarget = false;
            g._vertical = vertical;
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
            if (_vertical) FrontendDraw.Rule(vh, r.center.x - 1f, r.yMin + 3f, r.height - 6f, _tint, vertical: true);
            else FrontendDraw.Rule(vh, r.xMin + 3f, r.center.y, r.width - 6f, _tint);
        }
    }
}
