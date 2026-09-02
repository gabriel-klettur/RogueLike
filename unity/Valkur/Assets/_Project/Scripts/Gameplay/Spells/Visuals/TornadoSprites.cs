using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The bands that make up a funnel silhouette.
    ///
    /// <para>WHY AN ARC AND NOT A RING. <c>ElementalSprites.Ring</c> is rotationally symmetric,
    /// so spinning one is invisible — and a tornado that does not visibly spin is a cone. Each
    /// band is a PARTIAL ring that tapers to nothing at both ends, so the eye has something to
    /// track as it goes round.</para>
    ///
    /// <para>The arc is drawn CIRCULAR, never pre-squashed. A funnel is an ellipse on screen
    /// because the camera looks down at it, and that squash belongs on a parent transform with
    /// the rotation on the child — rotating an already-squashed sprite turns the whole ellipse
    /// like a wheel instead of running the arc around its rim. See
    /// <c>SpellCastFlourishFX.Funnel</c>.</para>
    ///
    /// <para>White and alpha only: the hue comes from the spell's own swatch through the
    /// renderer tint, the same rule as <see cref="KiSprites"/> and <see cref="ShieldSprites"/>.</para>
    /// </summary>
    internal static class TornadoSprites
    {
        /// <summary>How many distinct band silhouettes are generated and shared.</summary>
        public const int BandVariants = 4;

        private const int Size = 128;

        /// <summary>Normalized radius the band's bright line sits at, for pinning a world size.</summary>
        public const float BandRadius = 0.82f;

        private static Sprite[] _bands;
        private static Sprite _dust;

        /// <summary>One arc of the funnel wall, centre-pivoted and 1 world unit across.</summary>
        public static Sprite Band(int variant)
        {
            EnsureAll();
            return _bands[((variant % BandVariants) + BandVariants) % BandVariants];
        }

        /// <summary>A torn scrap of debris caught in the wind. Slightly angular, not a dot.</summary>
        public static Sprite Dust { get { EnsureAll(); return _dust; } }

        /// <summary>
        /// Domain Reload is OFF, so these carry DESTROYED native objects into the next Play
        /// session. Nulling the field is a plain <c>stsfld</c>, the only shape
        /// <c>DomainReloadStaticResetTests</c> reads as a reset.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _bands = null;
            _dust = null;
        }

        public static void EnsureAll()
        {
            if (_bands != null && _bands.Length == BandVariants && _bands[0] != null) return;

            _bands = new Sprite[BandVariants];
            for (int v = 0; v < BandVariants; v++) _bands[v] = BuildBand(v);
            _dust = BuildDust(24);
        }

        // ── generators ───────────────────────────────────────────────────────────────

        /// <summary>
        /// A partial ring. Its span, thickness and taper vary per variant so a stack of them
        /// never lines up into a set of concentric circles — which is what a funnel built from
        /// one repeated band looks like, and it reads as a spring rather than as wind.
        /// </summary>
        private static Sprite BuildBand(int variant)
        {
            var rng = new System.Random(0x704E + variant * 6151);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            float spanRadians = Range(2.2f, 4.3f);       // 126deg to 246deg of sweep
            float thickness = Range(0.055f, 0.105f);
            float leadTaper = Range(0.18f, 0.42f);        // fraction of the span spent fading in

            var tex = NewTexture(Size, Size);
            var px = new Color[Size * Size];
            float halfSpan = spanRadians * 0.5f;

            for (int y = 0; y < Size; y++)
            {
                float ny = (y + 0.5f) / Size * 2f - 1f;
                for (int x = 0; x < Size; x++)
                {
                    float nx = (x + 0.5f) / Size * 2f - 1f;

                    float radius = Mathf.Sqrt(nx * nx + ny * ny);
                    float radial = Mathf.Abs(radius - BandRadius) / thickness;
                    if (radial >= 1f) { px[y * Size + x] = Color.clear; continue; }

                    // Angle measured from the arc's centre, so the taper is symmetric.
                    float angle = Mathf.Atan2(ny, nx);
                    float fromCentre = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, 0f)) * Mathf.Deg2Rad;
                    if (fromCentre > halfSpan) { px[y * Size + x] = Color.clear; continue; }

                    // Solid through the middle of the sweep, gone at both ends: the head and
                    // the tail of a gust rather than a hoop with two cut edges.
                    float along = 1f - fromCentre / halfSpan;
                    float ends = Mathf.Clamp01(along / Mathf.Max(0.01f, leadTaper));

                    float across = Mathf.Pow(1f - radial, 1.5f);
                    px[y * Size + x] = new Color(1f, 1f, 1f,
                        Mathf.Clamp01(across * Mathf.Pow(ends, 1.4f)));
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0.5f), Size);
        }

        /// <summary>A scrap of debris: an irregular quad, opaque enough to read as matter.</summary>
        private static Sprite BuildDust(int size)
        {
            var poly = new[]
            {
                new Vector2(-0.62f,  0.30f),
                new Vector2( 0.24f,  0.72f),
                new Vector2( 0.70f, -0.14f),
                new Vector2(-0.18f, -0.68f),
            };

            var tex = NewTexture(size, size);
            var px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float inward = InwardDistance(new Vector2(nx, ny), poly);
                    if (inward <= 0f) { px[y * size + x] = Color.clear; continue; }

                    px[y * size + x] = new Color(1f, 1f, 1f,
                        Mathf.Clamp01(0.45f + 0.55f * Mathf.Clamp01(inward / 0.30f)));
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0.5f), size);
        }

        // ── shared ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Distance to the nearest edge of a CONVEX polygon wound counter-clockwise, positive
        /// inside and 0 outside.
        /// </summary>
        private static float InwardDistance(Vector2 p, Vector2[] poly)
        {
            float nearest = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 edge = poly[i] - poly[j];
                float length = edge.magnitude;
                if (length < 1e-5f) continue;
                float side = (edge.x * (p.y - poly[j].y) - edge.y * (p.x - poly[j].x)) / length;
                if (side <= 0f) return 0f;
                if (side < nearest) nearest = side;
            }
            return nearest == float.MaxValue ? 0f : nearest;
        }

        private static Texture2D NewTexture(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        private static Sprite Finish(Texture2D tex, Color[] px, Vector2 pivot, float ppu)
        {
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu);
        }
    }
}
