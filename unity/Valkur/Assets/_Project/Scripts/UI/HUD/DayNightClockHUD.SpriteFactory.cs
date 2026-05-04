using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Procedural sprite factory for the day/night clock HUD. All sprites are
    /// generated once on first access and cached as static fields — Domain
    /// Reload is OFF in Valkur, so a static reset hook clears the cache on
    /// Play Mode entry.
    ///
    /// Design notes: each sprite is rendered into a small RGBA32 texture with
    /// soft, anti-aliased edges so it scales cleanly to any HUD size. White
    /// pixels everywhere — the live tint is applied by Image.color so a single
    /// generated sprite serves every phase.
    /// </summary>
    public sealed partial class DayNightClockHUD
    {
        private const int IconSize = 64;

        private static Sprite _circleSprite;
        private static Sprite _ringSprite;
        private static Sprite _sunSprite;
        private static Sprite _moonSprite;
        private static Sprite _solidSprite;
        private static Sprite _trianglePointerSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpriteCacheOnPlayModeEnter()
        {
            // With Domain Reload OFF the cache otherwise survives across runs and
            // points at destroyed Texture2D handles when the editor re-enters Play.
            _circleSprite = _ringSprite = _sunSprite = _moonSprite = _solidSprite = null;
            _trianglePointerSprite = null;
        }

        // 4×4 white quad — universal flat fill for backgrounds and bars.
        private static Sprite SolidSprite()
        {
            if (_solidSprite != null) return _solidSprite;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            _solidSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return _solidSprite;
        }

        // Soft white circle — used for the dial backdrop.
        private static Sprite CircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            var tex = NewIconTex();
            var px  = new Color32[IconSize * IconSize];
            float r = IconSize * 0.5f;
            for (int y = 0; y < IconSize; y++)
            for (int x = 0; x < IconSize; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = Mathf.Clamp01(r - d);
                px[y * IconSize + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        // Hollow ring — used as the sundial track. Image.type=Filled,Radial360
        // sweeps a colored arc around it to indicate the current time of day.
        private static Sprite RingSprite()
        {
            if (_ringSprite != null) return _ringSprite;
            var tex = NewIconTex();
            var px  = new Color32[IconSize * IconSize];
            float r       = IconSize * 0.5f;
            float ringIn  = r - 5f;
            float ringOut = r - 1f;
            for (int y = 0; y < IconSize; y++)
            for (int x = 0; x < IconSize; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float aIn  = Mathf.Clamp01(d - ringIn);
                float aOut = Mathf.Clamp01(ringOut - d);
                float a    = Mathf.Min(aIn, aOut);
                px[y * IconSize + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f));
            return _ringSprite;
        }

        // Stylized sun: filled disc + 8 short rays radiating outward.
        private static Sprite SunSprite()
        {
            if (_sunSprite != null) return _sunSprite;
            var tex = NewIconTex();
            var px  = new Color32[IconSize * IconSize];
            float cx = IconSize * 0.5f;
            float cy = IconSize * 0.5f;
            float discR = IconSize * 0.30f;
            float rayInner = IconSize * 0.36f;
            float rayOuter = IconSize * 0.48f;
            float rayHalfWidth = 1.6f;

            for (int y = 0; y < IconSize; y++)
            for (int x = 0; x < IconSize; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = Mathf.Clamp01(discR - d); // central disc
                px[y * IconSize + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            // 8 rays at 0/45/90/135/180/225/270/315°
            for (int i = 0; i < 8; i++)
            {
                float angle = (i / 8f) * Mathf.PI * 2f;
                Vector2 a = new Vector2(cx + Mathf.Cos(angle) * rayInner, cy + Mathf.Sin(angle) * rayInner);
                Vector2 b = new Vector2(cx + Mathf.Cos(angle) * rayOuter, cy + Mathf.Sin(angle) * rayOuter);
                DrawSoftLine(px, IconSize, a, b, rayHalfWidth);
            }
            tex.SetPixels32(px); tex.Apply();
            _sunSprite = Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f));
            return _sunSprite;
        }

        // Down-pointing triangle used as a slider handle. Wide base on top,
        // apex at bottom-center pointing at the track. The pivot stays at
        // (0.5, 0.5) so Unity's Slider can place the handle by the same rules
        // it uses for any rectangular handle.
        private static Sprite TrianglePointerSprite()
        {
            if (_trianglePointerSprite != null) return _trianglePointerSprite;
            const int W = 16;
            const int H = 16;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px  = new Color32[W * H];
            // Triangle vertices (texture-space, y up): base is the top row,
            // apex is the bottom-center pixel. A 1-pixel margin around all
            // sides keeps the antialiased edge crisp inside the texture.
            Vector2 a = new Vector2(1f,        H - 1f);   // top-left
            Vector2 b = new Vector2(W - 1f,    H - 1f);   // top-right
            Vector2 c = new Vector2(W * 0.5f,  1f);       // apex (bottom)

            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                bool inside = PointInTri(p, a, b, c);
                if (!inside)
                {
                    px[y * W + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                // Soft-edge: distance to each edge → minimum gives the AA.
                float d1 = DistToSeg(p, a, b);
                float d2 = DistToSeg(p, b, c);
                float d3 = DistToSeg(p, c, a);
                float dMin = Mathf.Min(d1, Mathf.Min(d2, d3));
                float aA = Mathf.Clamp01(dMin);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(aA * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            _trianglePointerSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
            return _trianglePointerSprite;
        }

        private static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float s2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float s3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool hasNeg = (s1 < 0f) || (s2 < 0f) || (s3 < 0f);
            bool hasPos = (s1 > 0f) || (s2 > 0f) || (s3 > 0f);
            return !(hasNeg && hasPos);
        }

        private static float DistToSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        // Crescent moon: filled disc minus a smaller disc offset to the upper-right.
        private static Sprite MoonSprite()
        {
            if (_moonSprite != null) return _moonSprite;
            var tex = NewIconTex();
            var px  = new Color32[IconSize * IconSize];
            float cx = IconSize * 0.5f;
            float cy = IconSize * 0.5f;
            float fullR = IconSize * 0.38f;
            float biteR = IconSize * 0.34f;
            float biteOffsetX = IconSize * 0.16f;
            float biteOffsetY = IconSize * 0.10f;

            for (int y = 0; y < IconSize; y++)
            for (int x = 0; x < IconSize; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float dFull = Mathf.Sqrt(dx * dx + dy * dy);
                float dBite = Mathf.Sqrt((dx - biteOffsetX) * (dx - biteOffsetX) +
                                          (dy - biteOffsetY) * (dy - biteOffsetY));
                float aFull = Mathf.Clamp01(fullR - dFull);
                float aBite = Mathf.Clamp01(biteR - dBite);
                float a     = Mathf.Max(0f, aFull - aBite); // moon = full \ bite
                px[y * IconSize + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            _moonSprite = Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f));
            return _moonSprite;
        }

        // ── Pixel helpers ───────────────────────────────────────────────────

        private static Texture2D NewIconTex() =>
            new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

        private static void DrawSoftLine(Color32[] px, int N, Vector2 a, Vector2 b, float halfWidth)
        {
            int minX = Mathf.Max(0, (int)(Mathf.Min(a.x, b.x) - halfWidth - 1));
            int maxX = Mathf.Min(N - 1, (int)(Mathf.Max(a.x, b.x) + halfWidth + 1));
            int minY = Mathf.Max(0, (int)(Mathf.Min(a.y, b.y) - halfWidth - 1));
            int maxY = Mathf.Min(N - 1, (int)(Mathf.Max(a.y, b.y) + halfWidth + 1));
            Vector2 ab = b - a;
            float ablen2 = Mathf.Max(0.0001f, ab.sqrMagnitude);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ablen2);
                Vector2 q = a + ab * t;
                float d   = Vector2.Distance(p, q);
                float aA  = Mathf.Clamp01(halfWidth - d);
                if (aA <= 0) continue;
                var prev = px[y * N + x];
                byte na = (byte)Mathf.Max(prev.a, aA * 255);
                px[y * N + x] = new Color32(255, 255, 255, na);
            }
        }
    }
}
