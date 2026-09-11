using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Every icon the minimap and the world map draw. APPEND ONLY: the value indexes the
    /// atlas, and tests name icons by value.
    /// </summary>
    public enum MinimapIcon
    {
        Dot, EliteDot, Arrow, Diamond, Skull, Shield, Coin, Hammer,
        Axe, Bowl, Pickaxe, Flask, Star, Exclaim, Question, Door,
        Portal, Ankh, Tomb, Chevron, Pin, Glow, SoftRing, Spark,
        Bar, MapFold, Plus, Minus,
    }

    /// <summary>
    /// The icon atlas, generated in code from signed distance functions.
    ///
    /// <para><b>Two tones in one texture, and the second is the point.</b> A pixel inside the
    /// shape is WHITE, a pixel in the outline band around it is BLACK, and both are opaque.
    /// Vertex colour multiplies, so tinting an icon red gives a red shape with a black outline
    /// — which is the only thing that keeps a 7-pixel glyph legible over every kind of terrain.
    /// The minimap this replaced drew bare squares of flat colour with no outline at all, and a
    /// gold vendor square over gold-lit ground simply was not there. Holes inside a shape (the
    /// skull's eyes, the tomb's cross) fall inside the outline band and read as dark by the same
    /// rule, so no icon needs a second colour.</para>
    ///
    /// <para><b>Why generated.</b> Every icon is a few primitives, and generation keeps the
    /// minimap free of an art dependency — the precedent is <c>WorldBarArt</c> and the
    /// <c>ElementalSprites</c> family. The cost is measured once per session and is ~20 ms.</para>
    ///
    /// <para><b>The cell is padded.</b> Shapes are authored in a [-1, 1] square and rasterised
    /// into a [-1.15, 1.15] one, so an outline never touches the cell edge and a mip never pulls
    /// the neighbouring icon in.</para>
    /// </summary>
    public static class MinimapIconAtlas
    {
        public const int CellPixels = 64;
        public const int Columns = 8;
        public const int Rows = 4;
        private const float CellSpan = 1.15f;

        private static Texture2D s_texture;
        private static Rect[] s_uv;
        private static Sprite[] s_sprites;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMinimapIconAtlasStatics()
        {
            s_texture = null;
            s_uv = null;
            s_sprites = null;
        }

        /// <summary>The atlas. Generated on first use.</summary>
        public static Texture2D Texture
        {
            get
            {
                if (s_texture == null) Build();
                return s_texture;
            }
        }

        /// <summary>Normalised UV rect of an icon's cell.</summary>
        public static Rect UvOf(MinimapIcon icon)
        {
            if (s_texture == null) Build();
            int i = (int)icon;
            return i >= 0 && i < s_uv.Length ? s_uv[i] : s_uv[0];
        }

        /// <summary>A sprite over one icon's cell, for a uGUI Image (buttons). Cached.</summary>
        public static Sprite SpriteOf(MinimapIcon icon)
        {
            var tex = Texture;
            s_sprites ??= new Sprite[IconCount];
            int i = (int)icon;
            if (i < 0 || i >= s_sprites.Length) return null;
            if (s_sprites[i] != null) return s_sprites[i];
            var uv = s_uv[i];
            var rect = new Rect(uv.x * tex.width, uv.y * tex.height, uv.width * tex.width, uv.height * tex.height);
            s_sprites[i] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s_sprites[i].name = "MinimapIcon_" + icon;
            return s_sprites[i];
        }

        /// <summary>Number of icons the enum declares.</summary>
        public static int IconCount => System.Enum.GetValues(typeof(MinimapIcon)).Length;

        private static void Build()
        {
            int w = CellPixels * Columns, h = CellPixels * Rows;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, 5, false)
            {
                name = "MinimapIconAtlas",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var px = new Color32[w * h];
            int count = IconCount;
            var uv = new Rect[count];
            for (int i = 0; i < count; i++)
            {
                int cx = i % Columns, cy = i / Columns;
                // Row 0 at the TOP of the texture, so the layout reads the way the enum does.
                int ox = cx * CellPixels, oy = (Rows - 1 - cy) * CellPixels;
                RasterizeCell(px, w, ox, oy, (MinimapIcon)i);
                uv[i] = new Rect((float)ox / w, (float)oy / h, (float)CellPixels / w, (float)CellPixels / h);
            }

            tex.SetPixels32(px);
            tex.Apply(true, false);
            s_texture = tex;
            s_uv = uv;
        }

        private static void RasterizeCell(Color32[] px, int stride, int ox, int oy, MinimapIcon icon)
        {
            float unitPx = CellPixels * 0.5f / CellSpan;
            bool soft = icon == MinimapIcon.Glow || icon == MinimapIcon.SoftRing || icon == MinimapIcon.Spark
                        || icon == MinimapIcon.Bar;
            float outline = OutlineOf(icon);

            for (int y = 0; y < CellPixels; y++)
            for (int x = 0; x < CellPixels; x++)
            {
                var p = new Vector2(
                    ((x + 0.5f) / CellPixels * 2f - 1f) * CellSpan,
                    ((y + 0.5f) / CellPixels * 2f - 1f) * CellSpan);

                Color32 c;
                if (soft)
                {
                    float a = SoftAlpha(icon, p);
                    c = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
                else
                {
                    float d = Sdf(icon, p);
                    float fill = Mathf.Clamp01(0.5f - d * unitPx);
                    float outer = Mathf.Clamp01(0.5f - (d - outline) * unitPx);
                    float a = Mathf.Max(fill, outer);
                    float white = a > 1e-4f ? fill / a : 0f;
                    byte v = (byte)Mathf.RoundToInt(white * 255f);
                    c = new Color32(v, v, v, (byte)Mathf.RoundToInt(a * 255f));
                }
                px[(oy + y) * stride + ox + x] = c;
            }
        }

        /// <summary>Outline thickness per icon, in shape units. Small glyphs need proportionally more.</summary>
        internal static float OutlineOf(MinimapIcon icon)
        {
            switch (icon)
            {
                case MinimapIcon.Dot:      return 0.30f;
                case MinimapIcon.EliteDot: return 0.20f;
                case MinimapIcon.Exclaim:
                case MinimapIcon.Question: return 0.20f;
                default:                   return 0.15f;
            }
        }

        private static float SoftAlpha(MinimapIcon icon, Vector2 p)
        {
            float r = p.magnitude;
            switch (icon)
            {
                case MinimapIcon.Glow:
                    return Mathf.Exp(-r * r * 3.2f) * Mathf.Clamp01((1.08f - r) * 4f);
                case MinimapIcon.SoftRing:
                {
                    float t = (r - 0.80f) / 0.10f;
                    return Mathf.Exp(-t * t) * Mathf.Clamp01((1.08f - r) * 6f);
                }
                case MinimapIcon.Bar:
                    // A solid block for hairlines and ticks. The whole cell is opaque, so a quad
                    // stretched to one pixel is one clean pixel; the UV is inset by the caller's
                    // quad covering only the solid interior.
                    return Mathf.Clamp01((1.10f - Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y))) * 20f);
                default: // Spark: a four-point star with a hot core.
                {
                    float ax = Mathf.Abs(p.x), ay = Mathf.Abs(p.y);
                    float rays = Mathf.Max(Mathf.Exp(-ax * 16f) * Mathf.Exp(-ay * 2.6f),
                                           Mathf.Exp(-ay * 16f) * Mathf.Exp(-ax * 2.6f));
                    float core = Mathf.Exp(-r * r * 22f);
                    return Mathf.Max(rays, core) * Mathf.Clamp01((1.08f - r) * 5f);
                }
            }
        }

        // ── Shapes ─────────────────────────────────────────────────────────

        internal static float Sdf(MinimapIcon icon, Vector2 p)
        {
            switch (icon)
            {
                case MinimapIcon.Dot:
                    return Circle(p, Vector2.zero, 0.50f);

                case MinimapIcon.EliteDot:
                    return Mathf.Min(Circle(p, Vector2.zero, 0.40f),
                                     Mathf.Abs(Circle(p, Vector2.zero, 0.76f)) - 0.09f);

                case MinimapIcon.Arrow:
                    return Poly(p, new Vector2(0f, 0.95f), new Vector2(0.68f, -0.74f),
                                   new Vector2(0f, -0.36f), new Vector2(-0.68f, -0.74f));

                case MinimapIcon.Diamond:
                    return Poly(p, new Vector2(0f, 0.82f), new Vector2(0.62f, 0f),
                                   new Vector2(0f, -0.82f), new Vector2(-0.62f, 0f));

                case MinimapIcon.Skull:
                {
                    float head = Mathf.Min(Circle(p, new Vector2(0f, 0.14f), 0.58f),
                                           Box(p, new Vector2(0f, -0.42f), new Vector2(0.34f, 0.24f), 0.08f));
                    float eyes = Mathf.Min(Circle(p, new Vector2(-0.23f, 0.10f), 0.17f),
                                           Circle(p, new Vector2(0.23f, 0.10f), 0.17f));
                    float nose = Poly(p, new Vector2(0f, -0.10f), new Vector2(0.09f, -0.30f), new Vector2(-0.09f, -0.30f));
                    float teeth = Mathf.Min(Box(p, new Vector2(-0.11f, -0.60f), new Vector2(0.025f, 0.10f), 0f),
                                            Box(p, new Vector2(0.11f, -0.60f), new Vector2(0.025f, 0.10f), 0f));
                    return Sub(Sub(Sub(head, eyes), nose), teeth);
                }

                case MinimapIcon.Shield:
                    return Poly(p, new Vector2(-0.60f, 0.72f), new Vector2(0.60f, 0.72f),
                                   new Vector2(0.60f, 0.08f), new Vector2(0f, -0.86f), new Vector2(-0.60f, 0.08f)) - 0.04f;

                case MinimapIcon.Coin:
                    return Sub(Circle(p, Vector2.zero, 0.64f), Mathf.Abs(Circle(p, Vector2.zero, 0.40f)) - 0.045f);

                case MinimapIcon.Hammer:
                {
                    // Tilted, with a flat face and a claw: upright and symmetric it read as a sword.
                    Vector2 q = Rotate(p, 38f);
                    float head = Box(q, new Vector2(0.06f, 0.42f), new Vector2(0.36f, 0.20f), 0.04f);
                    float face = Box(q, new Vector2(-0.38f, 0.42f), new Vector2(0.12f, 0.25f), 0.03f);
                    float claw = Capsule(q, new Vector2(0.40f, 0.46f), new Vector2(0.70f, 0.22f), 0.09f);
                    float handle = Box(q, new Vector2(0.02f, -0.24f), new Vector2(0.09f, 0.60f), 0.05f);
                    return Mathf.Min(Mathf.Min(head, face), Mathf.Min(claw, handle));
                }

                case MinimapIcon.Axe:
                    return Mathf.Min(Capsule(p, new Vector2(-0.46f, -0.78f), new Vector2(0.22f, 0.64f), 0.09f),
                                     Poly(p, new Vector2(0.02f, 0.76f), new Vector2(0.66f, 0.66f),
                                             new Vector2(0.74f, 0.08f), new Vector2(0.10f, 0.28f)) - 0.03f);

                case MinimapIcon.Bowl:
                {
                    float bowl = Mathf.Max(Circle(p, new Vector2(0f, 0.04f), 0.64f), p.y - 0.06f);
                    float rim = Box(p, new Vector2(0f, 0.12f), new Vector2(0.76f, 0.08f), 0.04f);
                    float steam = Mathf.Min(Capsule(p, new Vector2(-0.22f, 0.36f), new Vector2(-0.12f, 0.74f), 0.07f),
                                            Capsule(p, new Vector2(0.16f, 0.36f), new Vector2(0.24f, 0.76f), 0.07f));
                    return Mathf.Min(Mathf.Min(bowl, rim), steam);
                }

                case MinimapIcon.Pickaxe:
                {
                    float arc = Mathf.Abs(Circle(p, new Vector2(0f, -0.30f), 0.80f)) - 0.12f;
                    arc = Mathf.Max(arc, 0.22f - p.y);
                    float handle = Capsule(p, new Vector2(0f, 0.50f), new Vector2(0f, -0.84f), 0.09f);
                    return Mathf.Min(arc, handle);
                }

                case MinimapIcon.Flask:
                    return Mathf.Min(Mathf.Min(Circle(p, new Vector2(0f, -0.30f), 0.50f),
                                               Box(p, new Vector2(0f, 0.28f), new Vector2(0.15f, 0.30f), 0.02f)),
                                     Box(p, new Vector2(0f, 0.62f), new Vector2(0.27f, 0.07f), 0.03f));

                case MinimapIcon.Star:
                    return Star5(p, 0.86f, 0.42f);

                case MinimapIcon.Exclaim:
                    return Mathf.Min(Capsule(p, new Vector2(0f, 0.72f), new Vector2(0f, 0.02f), 0.17f),
                                     Circle(p, new Vector2(0f, -0.54f), 0.18f));

                case MinimapIcon.Question:
                {
                    Vector2 q = p - new Vector2(0f, 0.34f);
                    float arc = Mathf.Abs(q.magnitude - 0.36f) - 0.13f;
                    if (q.x < 0f && q.y < 0f) arc = Mathf.Max(arc, 0.14f);      // open the lower-left of the hook
                    float stem = Capsule(p, new Vector2(0.18f, 0.10f), new Vector2(0f, -0.20f), 0.13f);
                    float dot = Circle(p, new Vector2(0f, -0.60f), 0.16f);
                    return Mathf.Min(Mathf.Min(arc, stem), dot);
                }

                case MinimapIcon.Door:
                {
                    float outer = Mathf.Min(Box(p, new Vector2(0f, -0.26f), new Vector2(0.54f, 0.52f), 0f),
                                            Circle(p, new Vector2(0f, 0.26f), 0.54f));
                    float inner = Mathf.Min(Box(p, new Vector2(0f, -0.34f), new Vector2(0.27f, 0.52f), 0f),
                                            Circle(p, new Vector2(0f, 0.22f), 0.27f));
                    return Sub(outer, inner);
                }

                case MinimapIcon.Portal:
                    return Mathf.Min(Mathf.Abs(Circle(p, Vector2.zero, 0.56f)) - 0.14f,
                                     Circle(p, Vector2.zero, 0.18f));

                case MinimapIcon.Ankh:
                    return Mathf.Min(Mathf.Min(Mathf.Abs(Circle(p, new Vector2(0f, 0.44f), 0.26f)) - 0.10f,
                                               Box(p, new Vector2(0f, 0.06f), new Vector2(0.54f, 0.10f), 0.03f)),
                                     Box(p, new Vector2(0f, -0.44f), new Vector2(0.11f, 0.44f), 0.03f));

                case MinimapIcon.Tomb:
                {
                    float stone = Mathf.Min(Box(p, new Vector2(0f, -0.22f), new Vector2(0.46f, 0.50f), 0f),
                                            Circle(p, new Vector2(0f, 0.28f), 0.46f));
                    float cross = Mathf.Min(Box(p, new Vector2(0f, 0.10f), new Vector2(0.05f, 0.26f), 0f),
                                            Box(p, new Vector2(0f, 0.20f), new Vector2(0.18f, 0.05f), 0f));
                    float slab = Box(p, new Vector2(0f, -0.78f), new Vector2(0.64f, 0.09f), 0.02f);
                    return Mathf.Min(Sub(stone, cross), slab);
                }

                case MinimapIcon.Chevron:
                    return Poly(p, new Vector2(0.86f, 0f), new Vector2(-0.36f, 0.72f),
                                   new Vector2(-0.04f, 0f), new Vector2(-0.36f, -0.72f));

                case MinimapIcon.Pin:
                {
                    float body = Mathf.Min(Circle(p, new Vector2(0f, 0.30f), 0.52f),
                                           Poly(p, new Vector2(-0.45f, 0.06f), new Vector2(0.45f, 0.06f), new Vector2(0f, -0.90f)));
                    return Sub(body, Circle(p, new Vector2(0f, 0.30f), 0.19f));
                }

                case MinimapIcon.MapFold:
                {
                    float sheet = Poly(p, new Vector2(-0.82f, 0.60f), new Vector2(-0.27f, 0.80f), new Vector2(0.27f, 0.60f),
                                          new Vector2(0.82f, 0.80f), new Vector2(0.82f, -0.60f), new Vector2(0.27f, -0.80f),
                                          new Vector2(-0.27f, -0.60f), new Vector2(-0.82f, -0.80f));
                    float folds = Mathf.Min(Box(p, new Vector2(-0.27f, 0f), new Vector2(0.035f, 0.9f), 0f),
                                            Box(p, new Vector2(0.27f, 0f), new Vector2(0.035f, 0.9f), 0f));
                    return Sub(sheet, folds);
                }

                case MinimapIcon.Plus:
                    return Mathf.Min(Box(p, Vector2.zero, new Vector2(0.64f, 0.17f), 0.05f),
                                     Box(p, Vector2.zero, new Vector2(0.17f, 0.64f), 0.05f));

                case MinimapIcon.Minus:
                    return Box(p, Vector2.zero, new Vector2(0.64f, 0.17f), 0.05f);

                default:
                    return Circle(p, Vector2.zero, 0.5f);
            }
        }

        // ── SDF primitives ─────────────────────────────────────────────────

        private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        private static float Box(Vector2 p, Vector2 c, Vector2 half, float round)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(round, round);
            var outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - round;
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        private static float Sub(float a, float b) => Mathf.Max(a, -b);

        /// <summary>Rotate a sample point by <paramref name="deg"/> clockwise (the shape turns counter-clockwise).</summary>
        private static Vector2 Rotate(Vector2 p, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        /// <summary>Exact signed distance to a simple polygon (convex or not).</summary>
        private static float Poly(Vector2 p, params Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i];
                Vector2 w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        /// <summary>Five-point star (Inigo Quilez's sdStar5), point up.</summary>
        private static float Star5(Vector2 p, float r, float rf)
        {
            var k1 = new Vector2(0.809016994375f, -0.587785252292f);
            var k2 = new Vector2(-k1.x, k1.y);
            p.x = Mathf.Abs(p.x);
            p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
            p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
            p.x = Mathf.Abs(p.x);
            p.y -= r;
            Vector2 ba = rf * new Vector2(-k1.y, k1.x) - new Vector2(0f, 1f);
            float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, r);
            return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
        }
    }
}
