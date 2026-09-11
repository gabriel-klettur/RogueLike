using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// A uGUI graphic that draws a list of icon quads in one mesh — every glyph on the map, or
    /// every glow and particle, depending on the material it is given.
    ///
    /// <para><b>Why a mesh and not one Image per glyph.</b> The map redraws its glyphs every
    /// frame (the minimap this replaced updated at 12 Hz, which is why its dots visibly
    /// stepped). Sixty Images would be sixty RectTransforms to move and sixty graphics to
    /// rebuild; one <see cref="MaskableGraphic"/> rebuilds one small mesh on the layout pass
    /// that happens anyway — the same shape <c>SparklineGraphic</c> and
    /// <c>TriangleHandleGraphic</c> already use.</para>
    ///
    /// <para><b>Positions are local to this graphic's rect CENTRE, in canvas units.</b> The
    /// caller projects world positions through <see cref="MinimapProjection"/> and never has
    /// to know the rect's pivot.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MinimapQuadGraphic : MaskableGraphic
    {
        private struct Quad
        {
            public Vector2 Pos;
            public Vector2 Size;
            public float CosR, SinR;
            public Rect Uv;
            public Color32 Color;
        }

        private readonly List<Quad> _quads = new List<Quad>(96);

        /// <summary>Quads queued since the last <see cref="Clear"/>.</summary>
        public int Count => _quads.Count;

        public override Texture mainTexture => MinimapIconAtlas.Texture;

        /// <summary>Forget every queued quad. Call once per frame before adding.</summary>
        public void Clear() => _quads.Clear();

        /// <summary>Queue one icon. <paramref name="rotationDeg"/> is counter-clockwise.</summary>
        public void Add(Vector2 centre, float size, MinimapIcon icon, Color color, float rotationDeg = 0f)
        {
            Add(centre, new Vector2(size, size), icon, color, rotationDeg);
        }

        /// <summary>Queue one icon with independent width and height.</summary>
        public void Add(Vector2 centre, Vector2 size, MinimapIcon icon, Color color, float rotationDeg = 0f)
        {
            if (color.a <= 0.002f || size.x <= 0.01f || size.y <= 0.01f) return;
            float rad = rotationDeg * Mathf.Deg2Rad;
            _quads.Add(new Quad
            {
                Pos = centre,
                Size = size,
                CosR = Mathf.Cos(rad),
                SinR = Mathf.Sin(rad),
                Uv = MinimapIconAtlas.UvOf(icon),
                Color = color,
            });
        }

        /// <summary>Push the queued quads to the mesh.</summary>
        public void Commit() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 origin = r.center;
            var v = UIVertex.simpleVert;
            for (int i = 0; i < _quads.Count; i++)
            {
                var q = _quads[i];
                float hx = q.Size.x * 0.5f, hy = q.Size.y * 0.5f;
                Vector2 ax = new Vector2(q.CosR, q.SinR) * hx;
                Vector2 ay = new Vector2(-q.SinR, q.CosR) * hy;
                Vector2 c = origin + q.Pos;
                int b = vh.currentVertCount;

                v.color = q.Color;
                v.position = c - ax - ay; v.uv0 = new Vector2(q.Uv.xMin, q.Uv.yMin); vh.AddVert(v);
                v.position = c - ax + ay; v.uv0 = new Vector2(q.Uv.xMin, q.Uv.yMax); vh.AddVert(v);
                v.position = c + ax + ay; v.uv0 = new Vector2(q.Uv.xMax, q.Uv.yMax); vh.AddVert(v);
                v.position = c + ax - ay; v.uv0 = new Vector2(q.Uv.xMax, q.Uv.yMin); vh.AddVert(v);
                vh.AddTriangle(b, b + 1, b + 2);
                vh.AddTriangle(b + 2, b + 3, b);
            }
        }
    }
}
