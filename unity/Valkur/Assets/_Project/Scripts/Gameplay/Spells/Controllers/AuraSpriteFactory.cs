using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Procedural sprite factory for <see cref="AuraController"/>.
    /// All sprites and textures are generated once and cached statically to avoid
    /// per-cast allocations.  Call <see cref="EnsureSprites"/> before reading any
    /// cached field.
    /// </summary>
    internal static class AuraSpriteFactory
    {
        // --- Cached procedural sprites ---
        internal static Sprite _runeOuterSprite;
        internal static Sprite _runeInnerSprite;
        internal static Sprite _innerGlowSprite;
        internal static Sprite _pulseRingSprite;
        internal static Sprite _pillarSprite;
        internal static Sprite _haloSprite;
        internal static Sprite _sparkleSprite;

        /// <summary>
        /// Generates and caches all sprites used by the aura VFX rig.
        /// Safe to call repeatedly — skips any sprite already built.
        /// </summary>
        internal static void EnsureSprites()
        {
            if (_runeOuterSprite == null) _runeOuterSprite = BuildRuneOuter(256);
            if (_runeInnerSprite == null) _runeInnerSprite = BuildRuneDodecahedron(256);
            if (_innerGlowSprite == null) _innerGlowSprite = BuildRadialGlow(128);
            if (_pulseRingSprite == null) _pulseRingSprite = BuildRing(128, 0.86f, 1.0f);
            if (_pillarSprite    == null) _pillarSprite    = BuildPillar(64, 256);
            if (_haloSprite      == null) _haloSprite      = BuildRadialGlow(128);
            if (_sparkleSprite   == null) _sparkleSprite   = BuildSparkleStar(32);
        }

        internal static Sprite SpriteFromTex(Texture2D tex, float ppu = 128f)
        {
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), ppu);
        }

        internal static Sprite BuildRadialGlow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            float maxR = c;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a; // soften
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f); // 1u radius
        }

        internal static Sprite BuildRing(int size, float innerR, float outerR)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            float maxR = c;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    float a = 0f;
                    if (r >= innerR && r <= outerR)
                    {
                        float k = Mathf.InverseLerp(innerR, outerR, r);
                        a = Mathf.Sin(k * Mathf.PI); // peak at middle of band
                    }
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }

        /// <summary>
        /// Outer rune circle: thick ring + tick marks + inner thin ring.
        /// </summary>
        internal static Sprite BuildRuneOuter(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            float maxR = cx;

            // Bands (in normalized radius)
            const float outerHi = 0.99f, outerLo = 0.92f;
            const float midHi   = 0.86f, midLo   = 0.84f;
            const float innerHi = 0.62f, innerLo = 0.60f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    float a = 0f;
                    if (r >= outerLo && r <= outerHi)
                        a = Mathf.Max(a, Mathf.Sin(Mathf.InverseLerp(outerLo, outerHi, r) * Mathf.PI));
                    if (r >= midLo && r <= midHi)
                        a = Mathf.Max(a, 0.7f);
                    if (r >= innerLo && r <= innerHi)
                        a = Mathf.Max(a, 0.85f);

                    // Tick marks between mid and outer ring (12 ticks every 30°)
                    if (r > midHi && r < outerLo)
                    {
                        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        float tickAng = Mathf.Repeat(ang + 360f, 30f);
                        if (tickAng < 2.5f || tickAng > 27.5f)
                            a = Mathf.Max(a, 0.85f);
                    }

                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }

        /// <summary>
        /// 2D projection of a regular dodecahedron (Schlegel diagram): a central
        /// pentagon surrounded by 5 pentagons, each sharing an edge with the centre.
        /// Ten of the 12 faces are visible; the back face is implicit at the centre,
        /// the front face is the outer boundary of the diagram.
        /// </summary>
        internal static Sprite BuildRuneDodecahedron(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            // Central pentagon circumradius. Outer pentagons reach ~2.618 * r,
            // so we keep r small enough for the whole diagram to fit.
            float r = cx * 0.30f;
            float lineHalf = Mathf.Max(1.5f, size / 110f);

            // Central pentagon vertices (point-up at 90°).
            Vector2[] pent = new Vector2[5];
            for (int i = 0; i < 5; i++)
            {
                float a = (90f + 72f * i) * Mathf.Deg2Rad;
                pent[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
            }

            // Draw central pentagon.
            for (int i = 0; i < 5; i++)
                DrawLine(px, size, pent[i], pent[(i + 1) % 5], lineHalf);

            // For each edge of the central pentagon, build the surrounding pentagon
            // by reflecting the 3 non-shared vertices across the shared edge.
            for (int edge = 0; edge < 5; edge++)
            {
                int ia = edge;
                int ib = (edge + 1) % 5;
                Vector2 a = pent[ia];
                Vector2 b = pent[ib];

                Vector2[] outer = new Vector2[5];
                for (int k = 0; k < 5; k++)
                {
                    if (k == ia || k == ib) outer[k] = pent[k];
                    else outer[k] = ReflectAcrossLine(pent[k], a, b);
                }

                // Draw the 4 non-shared edges (skip shared edge ia-ib, already drawn).
                for (int k = 0; k < 5; k++)
                {
                    int k2 = (k + 1) % 5;
                    if (k == ia && k2 == ib) continue;     // shared edge
                    if (k == ib && k2 == ia) continue;     // (defensive, won't happen with k+1 mod 5)
                    DrawLine(px, size, outer[k], outer[k2], lineHalf * 0.85f);
                }
            }

            // Bright vertex dots for the central pentagon.
            float dotR = size * 0.012f;
            for (int i = 0; i < 5; i++)
                DrawDot(px, size, pent[i], dotR);

            // Tiny center dot.
            DrawDot(px, size, new Vector2(cx, cy), size * 0.018f);

            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }

        /// <summary>
        /// Inner hexagram (Star of David): two overlaid equilateral triangles.
        /// </summary>
        internal static Sprite BuildRuneHexagram(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            float radius = cx * 0.78f;
            float lineHalf = Mathf.Max(1.5f, size / 96f); // line thickness in px

            // Triangle 1 (point up): 90, 210, 330
            var t1 = new Vector2[3];
            // Triangle 2 (point down): 30, 150, 270
            var t2 = new Vector2[3];
            for (int i = 0; i < 3; i++)
            {
                float a1 = (90f + 120f * i) * Mathf.Deg2Rad;
                float a2 = (30f + 120f * i) * Mathf.Deg2Rad;
                t1[i] = new Vector2(cx + Mathf.Cos(a1) * radius, cy + Mathf.Sin(a1) * radius);
                t2[i] = new Vector2(cx + Mathf.Cos(a2) * radius, cy + Mathf.Sin(a2) * radius);
            }

            void DrawTriangle(Vector2[] verts)
            {
                for (int i = 0; i < 3; i++)
                {
                    var a = verts[i];
                    var b = verts[(i + 1) % 3];
                    DrawLine(px, size, a, b, lineHalf);
                }
            }
            DrawTriangle(t1);
            DrawTriangle(t2);

            // Center bright dot.
            float dotR = size * 0.04f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= dotR)
                    {
                        float k = 1f - (d / dotR);
                        int idx = y * size + x;
                        var c = px[idx];
                        c.a = Mathf.Max(c.a, k);
                        px[idx] = c;
                    }
                }

            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }

        /// <summary>
        /// Vertical light pillar: bright bottom, fades to top with soft horizontal edges.
        /// </summary>
        internal static Sprite BuildPillar(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var px = new Color[width * height];
            float cx = (width - 1) * 0.5f;
            for (int y = 0; y < height; y++)
            {
                float vy = (float)y / (height - 1); // 0 bottom, 1 top
                float vAlpha = Mathf.Pow(1f - vy, 1.6f); // bright bottom
                for (int x = 0; x < width; x++)
                {
                    float dx = (x - cx) / cx;        // -1..1
                    float hAlpha = Mathf.Clamp01(1f - Mathf.Abs(dx));
                    hAlpha = hAlpha * hAlpha;
                    px[y * width + x] = new Color(1f, 1f, 1f, vAlpha * hAlpha);
                }
            }
            tex.SetPixels(px);
            // Pivot at bottom-center for the pillar so it grows from the ground.
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.05f), height);
        }

        /// <summary>
        /// 4-pointed sparkle star with soft falloff.
        /// </summary>
        internal static Sprite BuildSparkleStar(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = Mathf.Clamp01(1f - d);
                    core = core * core;
                    // Cross arms.
                    float armX = Mathf.Clamp01(1f - Mathf.Abs(dx) * 1.0f) * Mathf.Clamp01(1f - Mathf.Abs(dy) * 6f);
                    float armY = Mathf.Clamp01(1f - Mathf.Abs(dy) * 1.0f) * Mathf.Clamp01(1f - Mathf.Abs(dx) * 6f);
                    float a = Mathf.Clamp01(core + 0.85f * Mathf.Max(armX, armY));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }

        internal static Vector2 ReflectAcrossLine(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = Mathf.Max(1e-6f, Vector2.Dot(ab, ab));
            float t = Vector2.Dot(p - a, ab) / len2;
            Vector2 proj = a + ab * t;
            return 2f * proj - p;
        }

        internal static void DrawDot(Color[] px, int size, Vector2 c, float radius)
        {
            int minX = Mathf.Clamp(Mathf.FloorToInt(c.x - radius - 1f), 0, size - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt (c.x + radius + 1f), 0, size - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(c.y - radius - 1f), 0, size - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt (c.y + radius + 1f), 0, size - 1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - c.x, dy = y - c.y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= radius)
                    {
                        float k = 1f - (d / radius);
                        int idx = y * size + x;
                        var col = px[idx];
                        col.r = col.g = col.b = 1f;
                        col.a = Mathf.Max(col.a, k);
                        px[idx] = col;
                    }
                }
            }
        }

        internal static void DrawLine(Color[] px, int size, Vector2 a, Vector2 b, float halfThickness)
        {
            // Bounding box.
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - halfThickness - 1f), 0, size - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt (Mathf.Max(a.x, b.x) + halfThickness + 1f), 0, size - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) - halfThickness - 1f), 0, size - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt (Mathf.Max(a.y, b.y) + halfThickness + 1f), 0, size - 1);

            Vector2 ab = b - a;
            float len2 = Mathf.Max(0.0001f, Vector2.Dot(ab, ab));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x, y);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    Vector2 proj = a + ab * t;
                    float d = Vector2.Distance(p, proj);
                    if (d <= halfThickness)
                    {
                        float alpha = 1f - Mathf.Clamp01((d - halfThickness * 0.6f) / (halfThickness * 0.4f + 0.001f));
                        int idx = y * size + x;
                        var c = px[idx];
                        c.a = Mathf.Max(c.a, alpha);
                        c.r = c.g = c.b = 1f;
                        px[idx] = c;
                    }
                }
            }
        }
    }
}
