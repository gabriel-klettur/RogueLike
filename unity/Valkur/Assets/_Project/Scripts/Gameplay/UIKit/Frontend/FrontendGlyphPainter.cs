using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>What a glyph part is made of. The tone decides the vertical light ramp, never a flat fill.</summary>
    public enum FrontendGlyphTone
    {
        /// <summary>The bevel's metal: bronze at the foot, gold at the head.</summary>
        Metal = 0,

        /// <summary>The icon's own colour, shaded from deep to lit.</summary>
        Accent = 1,

        /// <summary>The accent close to white — a core, a flame's heart, a lit gem.</summary>
        AccentHot = 2,

        /// <summary>The recessed channel: a hole, a slot, an eye socket.</summary>
        Dark = 3,

        /// <summary>Warm paper / bone / steel light.</summary>
        Light = 4,
    }

    /// <summary>
    /// Draws an icon as vector geometry in the loading bar's language: every part gets a
    /// near-black outline, then a fill lit from above.
    ///
    /// <para><b>All outlines first, then all fills.</b> Parts overlap — a flame is a disc and a
    /// triangle, a sword is three strokes — and outlining each part as it is drawn would cut a
    /// dark seam through the middle of the silhouette wherever two parts meet. Two passes give
    /// the union a single clean contour, which is what makes 31 small icons read at 46 px.</para>
    ///
    /// <para>Coordinates are a unit box, -1..1 on both axes, y UP. The painter maps them into the
    /// rect it is given, so an icon is authored once and drawn at any size.</para>
    /// </summary>
    public sealed class FrontendGlyphPainter
    {
        private enum Kind { Rect, Disc, Ring, Tri, Line, Diamond }

        private struct Shape
        {
            public Kind Kind;
            public Vector2 A, B, C;
            public float R, W;
            public FrontendGlyphTone Tone;
        }

        private const int CircleSegments = 22;

        private readonly List<Shape> _shapes = new List<Shape>(24);
        private Vector2 _centre;
        private float _half;

        public void Rect(float x0, float y0, float x1, float y1, FrontendGlyphTone tone)
            => _shapes.Add(new Shape { Kind = Kind.Rect, A = new Vector2(x0, y0), B = new Vector2(x1, y1), Tone = tone });

        public void Disc(float cx, float cy, float r, FrontendGlyphTone tone)
            => _shapes.Add(new Shape { Kind = Kind.Disc, A = new Vector2(cx, cy), R = r, Tone = tone });

        /// <summary>A ring of radius <paramref name="r"/> (the middle of the band) and width <paramref name="w"/>.</summary>
        public void Ring(float cx, float cy, float r, float w, FrontendGlyphTone tone)
            => _shapes.Add(new Shape { Kind = Kind.Ring, A = new Vector2(cx, cy), R = r, W = w, Tone = tone });

        public void Tri(Vector2 a, Vector2 b, Vector2 c, FrontendGlyphTone tone)
            => _shapes.Add(new Shape { Kind = Kind.Tri, A = a, B = b, C = c, Tone = tone });

        public void Line(Vector2 a, Vector2 b, float w, FrontendGlyphTone tone)
            => _shapes.Add(new Shape { Kind = Kind.Line, A = a, B = b, W = w, Tone = tone });

        public void Diamond(float cx, float cy, float rx, float ry, FrontendGlyphTone tone)
            => _shapes.Add(new Shape { Kind = Kind.Diamond, A = new Vector2(cx, cy), B = new Vector2(rx, ry), Tone = tone });

        public int ShapeCount => _shapes.Count;

        /// <summary>Clears the shape list and sets the rect the unit box maps into (square, centred).</summary>
        public void Begin(Rect target)
        {
            _shapes.Clear();
            _centre = target.center;
            _half = Mathf.Min(target.width, target.height) * 0.5f;
        }

        /// <summary>Writes the outline pass and the fill pass. <paramref name="glow"/> 0..1 lifts the accent toward white.</summary>
        public void Flush(VertexHelper vh, Color accent, float glow)
        {
            if (_half <= 0f) return;
            // In UNIT space, so the outline is the same weight relative to the icon at any size,
            // with a floor so a small icon still has a contour.
            float o = Mathf.Max(1.4f, _half * 0.075f) / _half;
            var outline = (Color32)FrontendPalette.Outline;
            for (int i = 0; i < _shapes.Count; i++) Draw(vh, _shapes[i], o, outline, accent, glow, true);
            for (int i = 0; i < _shapes.Count; i++) Draw(vh, _shapes[i], 0f, outline, accent, glow, false);
        }

        private void Draw(VertexHelper vh, Shape s, float grow, Color32 outline, Color accent, float glow, bool isOutline)
        {
            switch (s.Kind)
            {
                case Kind.Rect:
                {
                    float x0 = s.A.x - grow, y0 = s.A.y - grow, x1 = s.B.x + grow, y1 = s.B.y + grow;
                    Color32 bottom = isOutline ? outline : Tone(s.Tone, y0, accent, glow);
                    Color32 top = isOutline ? outline : Tone(s.Tone, y1, accent, glow);
                    var p0 = Map(new Vector2(x0, y0));
                    var p1 = Map(new Vector2(x1, y1));
                    FrontendMesh.QuadV(vh, p0.x, p0.y, p1.x - p0.x, p1.y - p0.y, bottom, top);
                    if (!isOutline && s.Tone != FrontendGlyphTone.Dark && y1 - y0 > 0.12f)
                        FrontendMesh.Quad(vh, p0.x, p1.y - 1f, p1.x - p0.x, 1f, FrontendMesh.WithAlpha(Color.white, 0.35f));
                    break;
                }
                case Kind.Disc:
                    Fan(vh, s.A, s.R + grow, s.Tone, isOutline, outline, accent, glow);
                    break;
                case Kind.Ring:
                    RingBand(vh, s.A, s.R - s.W * 0.5f - grow, s.R + s.W * 0.5f + grow, s.Tone, isOutline, outline, accent, glow);
                    break;
                case Kind.Tri:
                {
                    Vector2 c = (s.A + s.B + s.C) / 3f;
                    Vector2 a = Push(s.A, c, grow * 1.9f), b = Push(s.B, c, grow * 1.9f), d = Push(s.C, c, grow * 1.9f);
                    FrontendMesh.Triangle(vh, Map(a), Map(b), Map(d),
                        isOutline ? outline : Tone(s.Tone, a.y, accent, glow),
                        isOutline ? outline : Tone(s.Tone, b.y, accent, glow),
                        isOutline ? outline : Tone(s.Tone, d.y, accent, glow));
                    break;
                }
                case Kind.Line:
                {
                    Vector2 dir = (s.B - s.A).normalized;
                    Vector2 n = new Vector2(-dir.y, dir.x) * (s.W * 0.5f + grow);
                    Vector2 a = s.A - dir * grow, b = s.B + dir * grow;
                    Quad4(vh, a - n, a + n, b + n, b - n, s.Tone, isOutline, outline, accent, glow);
                    break;
                }
                case Kind.Diamond:
                {
                    float rx = s.B.x + grow * 1.4f, ry = s.B.y + grow * 1.4f;
                    var cx = s.A.x; var cy = s.A.y;
                    Quad4(vh, new Vector2(cx, cy - ry), new Vector2(cx - rx, cy), new Vector2(cx, cy + ry), new Vector2(cx + rx, cy),
                          s.Tone, isOutline, outline, accent, glow);
                    break;
                }
            }
        }

        private void Fan(VertexHelper vh, Vector2 c, float r, FrontendGlyphTone tone, bool isOutline, Color32 outline, Color accent, float glow)
        {
            if (r <= 0f) return;
            int start = vh.currentVertCount;
            vh.AddVert(Map(c), isOutline ? outline : Tone(tone, c.y, accent, glow), Vector2.zero);
            for (int i = 0; i <= CircleSegments; i++)
            {
                float a = i / (float)CircleSegments * Mathf.PI * 2f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                vh.AddVert(Map(p), isOutline ? outline : Tone(tone, p.y, accent, glow), Vector2.zero);
            }
            for (int i = 0; i < CircleSegments; i++) vh.AddTriangle(start, start + 1 + i, start + 2 + i);
        }

        private void RingBand(VertexHelper vh, Vector2 c, float r0, float r1, FrontendGlyphTone tone, bool isOutline,
                              Color32 outline, Color accent, float glow)
        {
            r0 = Mathf.Max(0f, r0);
            for (int i = 0; i < CircleSegments; i++)
            {
                float a0 = i / (float)CircleSegments * Mathf.PI * 2f, a1 = (i + 1) / (float)CircleSegments * Mathf.PI * 2f;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                Quad4(vh, c + d0 * r0, c + d0 * r1, c + d1 * r1, c + d1 * r0, tone, isOutline, outline, accent, glow);
            }
        }

        private void Quad4(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, FrontendGlyphTone tone,
                           bool isOutline, Color32 outline, Color accent, float glow)
        {
            int i = vh.currentVertCount;
            vh.AddVert(Map(a), isOutline ? outline : Tone(tone, a.y, accent, glow), Vector2.zero);
            vh.AddVert(Map(b), isOutline ? outline : Tone(tone, b.y, accent, glow), Vector2.zero);
            vh.AddVert(Map(c), isOutline ? outline : Tone(tone, c.y, accent, glow), Vector2.zero);
            vh.AddVert(Map(d), isOutline ? outline : Tone(tone, d.y, accent, glow), Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        private static Vector2 Push(Vector2 p, Vector2 from, float by)
        {
            var d = p - from;
            float len = d.magnitude;
            return len > 0.0001f ? p + d / len * by : p;
        }

        private Vector2 Map(Vector2 unit) => _centre + unit * _half;

        /// <summary>The colour of a tone at unit height <paramref name="y"/> (-1 foot, +1 head).</summary>
        public static Color32 Tone(FrontendGlyphTone tone, float y, Color accent, float glow)
        {
            float t = Mathf.Clamp01((y + 1f) * 0.5f);
            Color c;
            switch (tone)
            {
                case FrontendGlyphTone.Metal:
                    c = Color.Lerp(FrontendPalette.BronzeDark, FrontendPalette.GoldLight, t);
                    c = Color.Lerp(c, Color.white, glow * 0.25f);
                    break;
                case FrontendGlyphTone.Accent:
                    c = Color.Lerp(accent * 0.55f, Color.Lerp(accent, Color.white, 0.25f), t);
                    c = Color.Lerp(c, Color.white, glow * 0.3f);
                    break;
                case FrontendGlyphTone.AccentHot:
                    c = Color.Lerp(Color.Lerp(accent, Color.white, 0.35f), Color.Lerp(accent, Color.white, 0.75f), t);
                    c = Color.Lerp(c, Color.white, glow * 0.3f);
                    break;
                case FrontendGlyphTone.Dark:
                    c = Color.Lerp(FrontendPalette.RecessTop, FrontendPalette.RecessBottom, t);
                    break;
                default:
                    c = Color.Lerp(FrontendPalette.WarmWhite * 0.72f, FrontendPalette.WarmWhite, t);
                    break;
            }
            c.a = 1f;
            return c;
        }
    }
}
