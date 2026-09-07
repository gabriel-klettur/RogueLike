using System;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// A line chart drawn straight into a uGUI mesh: a filled area under the curve plus the
    /// stroke over it, from an array of values already normalised to 0..1.
    ///
    /// <para><b>No texture, no line renderer, no plotting library.</b> Everything the project
    /// would otherwise reach for is the wrong tool at this size — a <c>LineRenderer</c> is a
    /// world-space object that does not belong on a Canvas, and a generated texture would have
    /// to be regenerated and re-uploaded on every repaint. A <see cref="MaskableGraphic"/>
    /// rebuilds its mesh on the layout pass that was going to happen anyway, and it is the
    /// same shape <see cref="TriangleHandleGraphic"/> already uses for the resize grip.</para>
    ///
    /// <para><b>It takes values already in 0..1 and does no scaling of its own.</b> That is
    /// deliberate: the normalisation has to happen where the DOMAIN is understood — a price
    /// chart scales to the wicks rather than the closes, which this widget has no way to know.
    /// Keeping the maths in <c>BitcoinSeries</c> also keeps it testable without a Canvas, and
    /// this file has nothing in it that can be wrong about Bitcoin.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SparklineGraphic : MaskableGraphic
    {
        /// <summary>
        /// Stroke thickness in pixels. Two is the thinnest that survives a non-integer canvas
        /// scale — at one pixel the line drops out on alternate rows and reads as a dashed
        /// chart, which looks like missing data rather than like a thin line.
        /// </summary>
        [SerializeField, Tooltip("Stroke thickness in pixels.")]
        private float thickness = 2f;

        [SerializeField, Tooltip("Alpha of the filled area under the curve, as a fraction of the stroke's.")]
        [Range(0f, 1f)] private float fillAlpha = 0.18f;

        [SerializeField, Tooltip("Draw the filled area under the line at all.")]
        private bool showFill = true;

        private float[] _values = Array.Empty<float>();

        /// <summary>How many points are plotted. Zero draws nothing at all.</summary>
        public int PointCount => _values.Length;

        /// <summary>
        /// Replace the plotted series. Values are CLAMPED rather than rescaled — a caller
        /// handing over something outside 0..1 has a bug in its normalisation, and silently
        /// rescaling here would hide it while drawing a chart with a different vertical scale
        /// from the axis labels beside it.
        /// </summary>
        public void SetValues(float[] values)
        {
            if (values == null || values.Length == 0)
            {
                _values = Array.Empty<float>();
                SetVerticesDirty();
                return;
            }

            var copy = new float[values.Length];
            for (int i = 0; i < values.Length; i++) copy[i] = Mathf.Clamp01(values[i]);
            _values = copy;
            SetVerticesDirty();
        }

        /// <summary>Stroke thickness, in pixels.</summary>
        public float Thickness
        {
            get => thickness;
            set { thickness = Mathf.Max(0.5f, value); SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_values.Length == 0) return;

            var r = rectTransform.rect;

            // Inset by half the stroke so the top and bottom of the curve are not clipped by
            // the rect. Without it a run that touches its own maximum draws a flat-topped line
            // that reads as the data being capped.
            float half = thickness * 0.5f;
            float y0 = r.yMin + half;
            float y1 = r.yMax - half;
            if (y1 <= y0) { y0 = r.yMin; y1 = r.yMax; }

            // A single point has no segment to draw, so it becomes a flat line across the
            // rect — one sample is still a price, and drawing nothing would read as an error.
            float step = _values.Length > 1 ? r.width / (_values.Length - 1) : 0f;

            Func<int, Vector2> pointAt = i => new Vector2(
                _values.Length > 1 ? r.xMin + step * i : r.center.x,
                Mathf.Lerp(y0, y1, _values[i]));

            if (_values.Length == 1)
            {
                var only = pointAt(0);
                AddSegment(vh, new Vector2(r.xMin, only.y), new Vector2(r.xMax, only.y), color, half);
                return;
            }

            if (showFill && fillAlpha > 0f)
            {
                var fill = color;
                fill.a *= fillAlpha;
                for (int i = 0; i < _values.Length - 1; i++)
                {
                    var a = pointAt(i);
                    var b = pointAt(i + 1);
                    AddQuad(vh,
                        new Vector2(a.x, r.yMin), new Vector2(b.x, r.yMin),
                        new Vector2(b.x, b.y), new Vector2(a.x, a.y), fill);
                }
            }

            for (int i = 0; i < _values.Length - 1; i++)
                AddSegment(vh, pointAt(i), pointAt(i + 1), color, half);
        }

        /// <summary>
        /// One stroke segment as a quad perpendicular to its own direction.
        ///
        /// <para>Joints are deliberately NOT mitred. At two pixels on a chart of a hundred
        /// points the gap at a joint is sub-pixel, and mitring costs a normal-averaging pass
        /// plus a degenerate case at every reversal — complexity nobody can see paying for a
        /// defect nobody can see.</para>
        /// </summary>
        private static void AddSegment(VertexHelper vh, Vector2 a, Vector2 b, Color32 tint,
                                       float halfWidth)
        {
            Vector2 dir = b - a;
            float len = dir.magnitude;
            if (len <= Mathf.Epsilon) return;

            // A perfectly vertical or horizontal run still needs a normal; magnitude is
            // guaranteed non-zero by the check above, so this cannot divide by zero.
            Vector2 normal = new Vector2(-dir.y, dir.x) / len;
            Vector2 offset = normal * halfWidth;

            AddQuad(vh, a - offset, b - offset, b + offset, a + offset, tint);
        }

        private static void AddQuad(VertexHelper vh, Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3,
                                    Color32 tint)
        {
            int start = vh.currentVertCount;
            vh.AddVert(v0, tint, Vector2.zero);
            vh.AddVert(v1, tint, Vector2.zero);
            vh.AddVert(v2, tint, Vector2.zero);
            vh.AddVert(v3, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
