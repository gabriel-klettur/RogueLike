using UnityEngine;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
        // 32×32 white circle sprite (cached, used by metronome dot + slider handle).
        private static Sprite _circleCache;
        private static Sprite _solidCache;
        private static Sprite BuildSolidSprite()
        {
            if (_solidCache != null) return _solidCache;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            _solidCache = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return _solidCache;
        }
        private static Sprite BuildCircleSprite()
        {
            if (_circleCache != null) return _circleCache;
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[N * N];
            float r = N * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d);
                pixels[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _circleCache = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return _circleCache;
        }

        // ── Procedural icon sprites ─────────────────────────────────────────
        // Cached per-icon. Each is rendered into a small RGBA32 texture with
        // antialiased edges so the icons look crisp at any scale.
        private static Sprite _sPlay, _sPause, _sPrev, _sNext, _sSpeaker, _sSpeakerMute, _sRoundRect, _sChevUp, _sChevDown;

        private static Sprite SpritePlay        { get { if (_sPlay        == null) _sPlay        = BuildPlaySprite();        return _sPlay; } }
        private static Sprite SpritePause       { get { if (_sPause       == null) _sPause       = BuildPauseSprite();       return _sPause; } }
        private static Sprite SpritePrev        { get { if (_sPrev        == null) _sPrev        = BuildPrevNextSprite(true);  return _sPrev; } }
        private static Sprite SpriteNext        { get { if (_sNext        == null) _sNext        = BuildPrevNextSprite(false); return _sNext; } }
        private static Sprite SpriteSpeaker     { get { if (_sSpeaker     == null) _sSpeaker     = BuildSpeakerSprite(false); return _sSpeaker; } }
        private static Sprite SpriteSpeakerMute { get { if (_sSpeakerMute == null) _sSpeakerMute = BuildSpeakerSprite(true);  return _sSpeakerMute; } }
        private static Sprite SpriteChevronUp   { get { if (_sChevUp      == null) _sChevUp      = BuildChevronSprite(true);  return _sChevUp; } }
        private static Sprite SpriteChevronDown { get { if (_sChevDown    == null) _sChevDown    = BuildChevronSprite(false); return _sChevDown; } }
        private static Sprite _sMinus;
        private static Sprite SpriteMinus       { get { if (_sMinus       == null) _sMinus       = BuildMinusSprite();        return _sMinus; } }

        private const int IcoN = 32;

        private static Sprite BuildRoundedRectSprite()
        {
            if (_sRoundRect != null) return _sRoundRect;
            const int N = 16;
            const int R = 4;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x < R ? R - x : (x >= N - R ? x - (N - R - 1) : 0);
                float dy = y < R ? R - y : (y >= N - R ? y - (N - R - 1) : 0);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(R - d);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            // Border = R so Image.Type.Sliced preserves the rounded corners at any size.
            _sRoundRect = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f),
                                        100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
            return _sRoundRect;
        }

        private static Sprite BuildPlaySprite()
        {
            var px = NewIconBuffer();
            // Right-pointing triangle, slightly inset.
            FillTriangleAA(px, IcoN, new Vector2(8f, 5f), new Vector2(8f, IcoN - 5f), new Vector2(IcoN - 6f, IcoN * 0.5f));
            return SpriteFromBuffer(px);
        }

        private static Sprite BuildPauseSprite()
        {
            var px = NewIconBuffer();
            FillRect(px, IcoN, 8, 6, 5, IcoN - 12);
            FillRect(px, IcoN, 8, 6, IcoN - 13, IcoN - 12);
            return SpriteFromBuffer(px);
        }

        private static Sprite BuildPrevNextSprite(bool prev)
        {
            var px = NewIconBuffer();
            if (prev)
            {
                FillRect(px, IcoN, 3, 6, 5, IcoN - 10);
                // Left triangle
                FillTriangleAA(px, IcoN, new Vector2(IcoN - 6f, 5f), new Vector2(IcoN - 6f, IcoN - 5f), new Vector2(8f, IcoN * 0.5f));
            }
            else
            {
                FillRect(px, IcoN, 3, 6, IcoN - 8, IcoN - 10);
                FillTriangleAA(px, IcoN, new Vector2(6f, 5f), new Vector2(6f, IcoN - 5f), new Vector2(IcoN - 8f, IcoN * 0.5f));
            }
            return SpriteFromBuffer(px);
        }

        private static Sprite BuildSpeakerSprite(bool muted)
        {
            var px = NewIconBuffer();
            // Speaker body (small box on left + horn triangle)
            FillRect(px, IcoN, 7, 6, 12, 7);                                    // body
            FillTriangleAA(px, IcoN, new Vector2(7f, 16f), new Vector2(20f, 6f), new Vector2(20f, IcoN - 6f)); // horn
            if (!muted)
            {
                // Two sound arcs on the right.
                DrawArc(px, IcoN, new Vector2(20f, 16f), 5f, 6f, -45f, 45f);
                DrawArc(px, IcoN, new Vector2(20f, 16f), 9f, 10f, -45f, 45f);
            }
            else
            {
                // Diagonal cross on the right.
                DrawLineAA(px, IcoN, new Vector2(22f, 9f),  new Vector2(IcoN - 4f, IcoN - 9f), 1.4f);
                DrawLineAA(px, IcoN, new Vector2(IcoN - 4f, 9f), new Vector2(22f, IcoN - 9f), 1.4f);
            }
            return SpriteFromBuffer(px);
        }

        private static Sprite BuildChevronSprite(bool pointUp)
        {
            // A double chevron (two stacked V shapes) so it reads as "expand" / "collapse".
            var px = NewIconBuffer();
            float cx = IcoN * 0.5f;
            float w = 8f;     // half-width of the chevron arms
            float t = 1.6f;   // line thickness
            // y positions for the two stacked Vs (one above the other)
            float y1 = pointUp ? 11f : 21f;
            float y2 = pointUp ? 19f : 13f;
            float dy = pointUp ? 5f  : -5f; // arm tip drops downward in down-chevron
            // First V
            DrawLineAA(px, IcoN, new Vector2(cx - w, y1 + dy), new Vector2(cx, y1), t);
            DrawLineAA(px, IcoN, new Vector2(cx,     y1),       new Vector2(cx + w, y1 + dy), t);
            // Second V
            DrawLineAA(px, IcoN, new Vector2(cx - w, y2 + dy), new Vector2(cx, y2), t);
            DrawLineAA(px, IcoN, new Vector2(cx,     y2),       new Vector2(cx + w, y2 + dy), t);
            return SpriteFromBuffer(px);
        }

        // A simple horizontal bar — universal "minimize / hide" affordance.
        private static Sprite BuildMinusSprite()
        {
            var px = NewIconBuffer();
            float cy = IcoN * 0.5f;
            DrawLineAA(px, IcoN, new Vector2(7f, cy), new Vector2(IcoN - 7f, cy), 2.4f);
            return SpriteFromBuffer(px);
        }

        // ── Pixel buffer helpers ────────────────────────────────────────────
        private static Color32[] NewIconBuffer() => new Color32[IcoN * IcoN];

        private static Sprite SpriteFromBuffer(Color32[] px)
        {
            var tex = new Texture2D(IcoN, IcoN, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, IcoN, IcoN), new Vector2(0.5f, 0.5f));
        }

        private static void FillRect(Color32[] px, int N, int w, int h, int x0, int y0)
        {
            for (int y = y0; y < y0 + h && y < N; y++)
            for (int x = x0; x < x0 + w && x < N; x++)
                if (x >= 0 && y >= 0) px[y * N + x] = new Color32(255, 255, 255, 255);
        }

        private static void FillTriangleAA(Color32[] px, int N, Vector2 a, Vector2 b, Vector2 c)
        {
            float minX = Mathf.Floor(Mathf.Min(a.x, b.x, c.x)) - 1f;
            float maxX = Mathf.Ceil (Mathf.Max(a.x, b.x, c.x)) + 1f;
            float minY = Mathf.Floor(Mathf.Min(a.y, b.y, c.y)) - 1f;
            float maxY = Mathf.Ceil (Mathf.Max(a.y, b.y, c.y)) + 1f;
            for (int y = (int)Mathf.Max(0, minY); y <= (int)Mathf.Min(N - 1, maxY); y++)
            for (int x = (int)Mathf.Max(0, minX); x <= (int)Mathf.Min(N - 1, maxX); x++)
            {
                // Compute signed distance to triangle (approx via barycentric).
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                if (PointInTriangle(p, a, b, c))
                    px[y * N + x] = new Color32(255, 255, 255, 255);
            }
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = Cross(p - a, b - a);
            float s2 = Cross(p - b, c - b);
            float s3 = Cross(p - c, a - c);
            return (s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0);
        }

        private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        private static void DrawLineAA(Color32[] px, int N, Vector2 a, Vector2 b, float thickness)
        {
            int minX = Mathf.Max(0, (int)(Mathf.Min(a.x, b.x) - thickness - 1));
            int maxX = Mathf.Min(N - 1, (int)(Mathf.Max(a.x, b.x) + thickness + 1));
            int minY = Mathf.Max(0, (int)(Mathf.Min(a.y, b.y) - thickness - 1));
            int maxY = Mathf.Min(N - 1, (int)(Mathf.Max(a.y, b.y) + thickness + 1));
            Vector2 ab = b - a; float ablen2 = ab.sqrMagnitude;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ablen2));
                Vector2 q = a + ab * t;
                float d = Vector2.Distance(p, q);
                float aA = Mathf.Clamp01(thickness - d);
                if (aA > 0)
                {
                    var prev = px[y * N + x];
                    byte na = (byte)Mathf.Max(prev.a, aA * 255);
                    px[y * N + x] = new Color32(255, 255, 255, na);
                }
            }
        }

        private static void DrawArc(Color32[] px, int N, Vector2 c, float rIn, float rOut, float angMinDeg, float angMaxDeg)
        {
            int minX = Mathf.Max(0, (int)(c.x - rOut - 1));
            int maxX = Mathf.Min(N - 1, (int)(c.x + rOut + 1));
            int minY = Mathf.Max(0, (int)(c.y - rOut - 1));
            int maxY = Mathf.Min(N - 1, (int)(c.y + rOut + 1));
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - c.x, dy = y + 0.5f - c.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < rIn || d > rOut) continue;
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (ang < angMinDeg || ang > angMaxDeg) continue;
                float a = Mathf.Min(d - rIn, rOut - d);
                a = Mathf.Clamp01(a);
                var prev = px[y * N + x];
                byte na = (byte)Mathf.Max(prev.a, a * 255);
                px[y * N + x] = new Color32(255, 255, 255, na);
            }
        }
    }
}
