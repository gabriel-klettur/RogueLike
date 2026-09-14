using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// The primitives every pre-game surface is built from, as vertex-coloured geometry on the
    /// canvas's white texture: a flat quad, a quad with a vertical gradient, a slanted band, a
    /// diamond and a triangle.
    ///
    /// <para><b>Geometry instead of sprites, on purpose.</b> The loading bar is a long thin
    /// rectangle whose width is 60 % of the screen, and a menu panel is a rectangle the kit sizes
    /// to its content; any sprite drawn at those aspects is either a 9-slice whose centre
    /// stretches or a texture filtered across hundreds of pixels. A quad whose corners are the
    /// colours is exact at every resolution, and every edge lands where the canvas scaler puts it
    /// rather than where a texel happened to be.</para>
    ///
    /// <para>Born as <c>LoadingBarMesh</c>; moved here unchanged when the menus adopted the bar's
    /// language, so there is one set of primitives and not two that drift.</para>
    /// </summary>
    public static class FrontendMesh
    {
        public static void Quad(VertexHelper vh, float x, float y, float w, float h, Color32 c)
            => QuadV(vh, x, y, w, h, c, c);

        /// <summary>A rectangle coloured <paramref name="bottom"/> at its foot and <paramref name="top"/> at its head.</summary>
        public static void QuadV(VertexHelper vh, float x, float y, float w, float h, Color32 bottom, Color32 top)
        {
            if (w <= 0f || h <= 0f) return;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), bottom, Vector2.zero);
            vh.AddVert(new Vector3(x, y + h), top, Vector2.zero);
            vh.AddVert(new Vector3(x + w, y + h), top, Vector2.zero);
            vh.AddVert(new Vector3(x + w, y), bottom, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        /// <summary>A rectangle coloured <paramref name="left"/> at its left edge and <paramref name="right"/> at its right.</summary>
        public static void QuadH(VertexHelper vh, float x, float y, float w, float h, Color32 left, Color32 right)
        {
            if (w <= 0f || h <= 0f) return;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), left, Vector2.zero);
            vh.AddVert(new Vector3(x, y + h), left, Vector2.zero);
            vh.AddVert(new Vector3(x + w, y + h), right, Vector2.zero);
            vh.AddVert(new Vector3(x + w, y), right, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        /// <summary>A slanted band: bottom edge from x to x+w, top edge shifted right by <paramref name="slant"/>.</summary>
        public static void Parallelogram(VertexHelper vh, float x, float y, float w, float h, float slant, Color32 c)
        {
            if (w <= 0f || h <= 0f) return;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), c, Vector2.zero);
            vh.AddVert(new Vector3(x + slant, y + h), c, Vector2.zero);
            vh.AddVert(new Vector3(x + slant + w, y + h), c, Vector2.zero);
            vh.AddVert(new Vector3(x + w, y), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        /// <summary>A diamond lit from above: the top vertex takes <paramref name="light"/>, the foot <paramref name="dark"/>.</summary>
        public static void Diamond(VertexHelper vh, float cx, float cy, float rx, float ry, Color32 light, Color32 dark)
        {
            if (rx <= 0f || ry <= 0f) return;
            Color32 mid = Color32.Lerp(light, dark, 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(cx, cy + ry), light, Vector2.zero);
            vh.AddVert(new Vector3(cx + rx, cy), mid, Vector2.zero);
            vh.AddVert(new Vector3(cx, cy - ry), dark, Vector2.zero);
            vh.AddVert(new Vector3(cx - rx, cy), mid, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        /// <summary>A triangle with one colour per corner.</summary>
        public static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 ca, Color32 cb, Color32 cc)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, ca, Vector2.zero);
            vh.AddVert(b, cb, Vector2.zero);
            vh.AddVert(c, cc, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        /// <summary>
        /// Multiplies every vertex added so far by <paramref name="tint"/> — what an
        /// <c>Image</c> does with its <c>color</c> and a hand-built mesh does NOT do on its own.
        /// Without it <c>Graphic.color</c> is silently inert on these graphics: a load-panel slot
        /// "hidden" with <c>Color.clear</c> went on drawing, and so did every other one.
        /// </summary>
        public static void ApplyGraphicColor(VertexHelper vh, Color tint)
        {
            if (tint == Color.white) return;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Color c = v.color;
                v.color = c * tint;
                vh.SetUIVertex(v, i);
            }
        }

        public static Color32 WithAlpha(Color c, float a)
        {
            c.a = Mathf.Clamp01(a);
            return c;
        }
    }
}
