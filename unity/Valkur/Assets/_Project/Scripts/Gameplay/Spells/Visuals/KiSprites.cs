using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Procedural sprite family for a ki charge: the flame tongues that make up the aura,
    /// the vertical streaks that stream off it, and the pebbles it tears off the ground.
    ///
    /// <para>WHITE AND ALPHA ONLY, unlike <see cref="IceSprites"/>. Ice bakes its colour in
    /// because a crystal's base is a different colour from its tip and one
    /// <c>SpriteRenderer.color</c> cannot say that. A ki aura is the opposite case: seven
    /// spells share one shape and differ ONLY by hue, so the colour has to stay in the tint —
    /// baking it would mean seven copies of every texture. The pebbles are the exception and
    /// carry their own grey, because rock torn off the ground is not made of ki.</para>
    ///
    /// <para>Shaping lives in the ALPHA, which on an additive material is brightness: a tongue
    /// that is opaque at its base and thin at its tip adds more light low down, which is what
    /// a flame does.</para>
    /// </summary>
    internal static class KiSprites
    {
        /// <summary>How many distinct flame silhouettes are generated and shared.</summary>
        public const int TongueVariants = 4;

        /// <summary>World height of a tongue sprite at <c>localScale = 1</c> (it is 1x2).</summary>
        public const float TongueUnitHeight = 2f;

        private const int TongueW = 64;
        private const int TongueH = 128;

        private static Sprite[] _tongue;
        private static Sprite _column;
        private static Sprite _streak;
        private static Sprite _pebble;

        /// <summary>One flame tongue, pivoted at its base so it grows upward.</summary>
        public static Sprite Tongue(int variant)
        {
            EnsureAll();
            return _tongue[((variant % TongueVariants) + TongueVariants) % TongueVariants];
        }

        /// <summary>The smooth body-hugging core the tongues sit in front of. Base-pivoted.</summary>
        public static Sprite Column { get { EnsureAll(); return _column; } }

        /// <summary>A vertical spark for the ki streaming upward. Centre-pivoted, 1x2 units.</summary>
        public static Sprite Streak { get { EnsureAll(); return _streak; } }

        /// <summary>An angular chunk of ground. Opaque and grey — this one is not ki.</summary>
        public static Sprite Pebble { get { EnsureAll(); return _pebble; } }

        /// <summary>Size a base-pivoted tongue or column in world units.</summary>
        public static void ScaleTongue(Transform t, float widthWu, float heightWu)
            => t.localScale = new Vector3(widthWu, heightWu / TongueUnitHeight, 1f);

        /// <summary>
        /// Domain Reload is OFF, so these carry DESTROYED native objects into the next Play
        /// session. Nulling the field is a plain <c>stsfld</c>, the only shape
        /// <c>DomainReloadStaticResetTests</c> reads as a reset.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tongue = null;
            _column = null;
            _streak = null;
            _pebble = null;
        }

        public static void EnsureAll()
        {
            if (_tongue != null && _tongue.Length == TongueVariants && _tongue[0] != null) return;

            _tongue = new Sprite[TongueVariants];
            for (int v = 0; v < TongueVariants; v++) _tongue[v] = BuildTongue(v);

            _column = BuildColumn();
            _streak = BuildStreak();
            _pebble = BuildPebble(24);
        }

        // ── generators ───────────────────────────────────────────────────────────────

        /// <summary>
        /// A flame tongue: wide and solid at the base, tapering to a point, with a rippling
        /// edge. The ripple is what stops four static sprites reading as four static sprites —
        /// each variant carries a different phase, so a ring of them never lines up.
        /// </summary>
        private static Sprite BuildTongue(int variant)
        {
            var rng = new System.Random(0x1CE0 + variant * 7919);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            float baseHalf = Range(0.30f, 0.42f);
            float taper = Range(0.85f, 1.35f);
            float lean = Range(-0.13f, 0.13f);
            float rippleFreq = Range(5f, 9f);
            float ripplePhase = Range(0f, 6.28f);
            float rippleAmp = Range(0.06f, 0.14f);

            var tex = NewTexture(TongueW, TongueH);
            var px = new Color[TongueW * TongueH];

            for (int y = 0; y < TongueH; y++)
            {
                float v = (y + 0.5f) / TongueH;

                float half = baseHalf * Mathf.Pow(1f - v, taper);
                half *= 1f + rippleAmp * Mathf.Sin(v * rippleFreq + ripplePhase);
                // The very tip is drawn to a point rather than being clipped flat.
                half *= Mathf.Clamp01((1f - v) * 6f);
                float centre = lean * Mathf.Pow(v, 1.4f);

                for (int x = 0; x < TongueW; x++)
                {
                    float u = (x + 0.5f) / TongueW - 0.5f;
                    float d = Mathf.Abs(u - centre);
                    if (half <= 1e-4f || d > half) { px[y * TongueW + x] = Color.clear; continue; }

                    float inward = 1f - d / half;               // 1 at the spine, 0 at the edge

                    // Hot spine, soft flanks. On an additive material this IS the brightness
                    // ramp, so the tongue adds most light where the flame is thickest.
                    float a = Mathf.Pow(inward, 0.85f);
                    // Denser low down: a flame is fed from its base.
                    a *= Mathf.Lerp(1f, 0.35f, Mathf.Pow(v, 0.7f));
                    px[y * TongueW + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0f), TongueW);
        }

        /// <summary>
        /// The smooth column the tongues stand in. Deliberately featureless: it is the mass of
        /// the aura, and any detail on it competes with the tongues that carry the movement.
        /// </summary>
        private static Sprite BuildColumn()
        {
            var tex = NewTexture(TongueW, TongueH);
            var px = new Color[TongueW * TongueH];

            for (int y = 0; y < TongueH; y++)
            {
                float v = (y + 0.5f) / TongueH;
                // A teardrop: round at the bottom, drawn out to a point at the top.
                float half = 0.46f * Mathf.Pow(1f - v, 0.55f) * Mathf.Clamp01((1f - v) * 5f);
                half *= Mathf.Clamp01(0.35f + v * 4f);   // pinched right at the floor

                for (int x = 0; x < TongueW; x++)
                {
                    float u = (x + 0.5f) / TongueW - 0.5f;
                    float d = Mathf.Abs(u);
                    if (half <= 1e-4f || d > half) { px[y * TongueW + x] = Color.clear; continue; }

                    float inward = 1f - d / half;
                    float a = Mathf.Pow(inward, 1.6f) * Mathf.Lerp(1f, 0.25f, Mathf.Pow(v, 0.8f));
                    px[y * TongueW + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a * 0.9f));
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0f), TongueW);
        }

        /// <summary>
        /// A vertical spark. Drawn TALL and rendered as a plain billboard, not as a stretched
        /// one: Unity's stretched billboard aligns the quad's U axis with velocity, so a
        /// stretch-mode streak has to be wider than it is tall — the trap the weather rain
        /// shipped with. A billboard with a tall texture needs no such contortion.
        /// </summary>
        private static Sprite BuildStreak()
        {
            const int w = 16, h = 64;
            var tex = NewTexture(w, h);
            var px = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                // Bright and fat at the bottom, fading to nothing at the top: a spark that is
                // being left behind by its own motion.
                float half = 0.42f * Mathf.Pow(1f - v, 0.65f);
                float bright = Mathf.Pow(1f - v, 1.4f);

                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w - 0.5f;
                    float d = Mathf.Abs(u);
                    if (half <= 1e-4f || d > half) { px[y * w + x] = Color.clear; continue; }

                    float inward = 1f - d / half;
                    px[y * w + x] = new Color(1f, 1f, 1f,
                        Mathf.Clamp01(Mathf.Pow(inward, 1.2f) * bright));
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0.5f), h * 0.5f);
        }

        /// <summary>A chip of ground. Opaque grey with a lit top edge, so it reads as rock.</summary>
        private static Sprite BuildPebble(int size)
        {
            var poly = new[]
            {
                new Vector2(-0.10f,  0.86f),
                new Vector2( 0.62f,  0.24f),
                new Vector2( 0.38f, -0.70f),
                new Vector2(-0.46f, -0.58f),
                new Vector2(-0.72f,  0.18f),
            };

            var tex = NewTexture(size, size);
            var px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    var p = new Vector2(nx, ny);
                    if (!PointInPolygon(p, poly)) { px[y * size + x] = Color.clear; continue; }

                    // Lit from above, like everything else in a top-down scene.
                    float lit = Mathf.Clamp01(ny * 0.5f + 0.5f);
                    float grey = Mathf.Lerp(0.22f, 0.62f, Mathf.Pow(lit, 1.4f));
                    px[y * size + x] = new Color(grey, grey * 0.96f, grey * 0.90f, 1f);
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0.5f), size);
        }

        // ── shared ───────────────────────────────────────────────────────────────────

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

        private static bool PointInPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (poly[i].y > p.y == poly[j].y > p.y) continue;
                float xCross = (poly[j].x - poly[i].x) * (p.y - poly[i].y) /
                               (poly[j].y - poly[i].y) + poly[i].x;
                if (p.x < xCross) inside = !inside;
            }
            return inside;
        }
    }
}
