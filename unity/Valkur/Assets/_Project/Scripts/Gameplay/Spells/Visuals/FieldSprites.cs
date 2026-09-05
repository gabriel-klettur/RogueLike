using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The shapes the five PERSISTENT FIELD rigs need and no existing library has: a stone
    /// shaft, a charred ground patch, a lumpy cloud puff, a crawling arc, and a horizontal
    /// speed streak.
    ///
    /// <para>WHY A SIXTH LIBRARY RATHER THAN GROWING ONE. <see cref="ElementalSprites"/>,
    /// <see cref="IceSprites"/>, <see cref="RootSprites"/> and <see cref="KiSprites"/> are each
    /// owned by the effect family that created them, and every one of them is already read by
    /// several rigs — adding to any of them makes an unrelated effect's silhouette a shared
    /// resource. These five belong to the ground/volume fields and to nothing else.</para>
    ///
    /// <para>WHITE AND ALPHA, WITH ONE EXCEPTION. Everything here is tinted through
    /// <c>SpriteRenderer.color</c>, so one shape serves five hues. <see cref="Shaft"/> is the
    /// exception and bakes a grey RAMP into its RGB for the same reason <see cref="IceSprites"/>
    /// does: a stone column is lighter at the top than at the base and darker along its seam,
    /// and a single tint cannot express a luminance ramp. Tinting a baked ramp multiplies, so
    /// the shading survives whatever colour the spell asks for.</para>
    ///
    /// <para>Every sprite is created with its own WIDTH as <c>pixelsPerUnit</c>, so a sprite is
    /// exactly one world unit WIDE and <c>height/width</c> units tall. That is what makes a
    /// scale constant readable as a world size — the rule <see cref="ElementalSprites"/>
    /// records, where all eleven are 1x1.</para>
    /// </summary>
    internal static class FieldSprites
    {
        /// <summary>World height of a base-pivoted <see cref="Shaft"/> at localScale 1.</summary>
        public const float ShaftUnitHeight = 2f;

        /// <summary>World height of a centre-pivoted <see cref="Arc"/> at localScale 1.</summary>
        public const float ArcUnitHeight = 0.25f;

        private static Sprite _shaft, _scorch, _puff, _arc, _streak;

        /// <summary>An opaque stone column, pivoted at its base so it grows upward.</summary>
        public static Sprite Shaft { get { EnsureAll(); return _shaft; } }

        /// <summary>An irregular burnt patch of ground. Alpha-mottled, so soot lets the floor
        /// show through instead of stamping a flat disc on it.</summary>
        public static Sprite Scorch { get { EnsureAll(); return _scorch; } }

        /// <summary>A soft lumpy blob. The unit of anything that reads as a cloud.</summary>
        public static Sprite Puff { get { EnsureAll(); return _puff; } }

        /// <summary>
        /// A jagged discharge running along +X from a LEFT-CENTRE pivot, so a caller places it
        /// at point A, rotates it to the bearing of B and scales x by the distance. A
        /// centre-pivoted bolt would have to be positioned at the midpoint, which is the same
        /// information stated twice and drifts the first time one half is edited.
        /// </summary>
        public static Sprite Arc { get { EnsureAll(); return _arc; } }

        /// <summary>
        /// A horizontal speed streak, deliberately WIDER than it is tall.
        ///
        /// <para>Unity's stretched billboard aligns the quad's U axis with VELOCITY, so a
        /// vertical strip is smeared ACROSS its own fall instead of along it — which is exactly
        /// how rain shipped before <c>WeatherTextures.Streak</c> was drawn horizontally.</para>
        /// </summary>
        public static Sprite Streak { get { EnsureAll(); return _streak; } }

        /// <summary>Size a base-pivoted shaft in world units.</summary>
        public static void ScaleShaft(Transform t, float widthWu, float heightWu)
            => t.localScale = new Vector3(widthWu, heightWu / ShaftUnitHeight, 1f);

        /// <summary>
        /// Domain Reload is OFF, so these survive a recompile holding DESTROYED native objects.
        /// A plain field assignment is the only reset shape <c>DomainReloadStaticResetTests</c>
        /// reads off the IL — an <c>Array.Clear</c> passes the field as an argument and counts
        /// as no reset at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _shaft = null;
            _scorch = null;
            _puff = null;
            _arc = null;
            _streak = null;
        }

        public static void EnsureAll()
        {
            if (_shaft == null)  _shaft  = BuildShaft(64, 128);
            if (_scorch == null) _scorch = BuildScorch(96);
            if (_puff == null)   _puff   = BuildPuff(96);
            if (_arc == null)    _arc    = BuildArc(128, 32);
            if (_streak == null) _streak = BuildStreak(48, 12);
        }

        // ── generators ───────────────────────────────────────────────────────────────

        private static Texture2D NewTexture(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        /// <summary>
        /// A dressed stone column: a slight taper, a wider footing and capital, a vertical seam
        /// and a soft top-lit ramp. The ramp is baked into RGB — see the class doc.
        /// </summary>
        private static Sprite BuildShaft(int w, int h)
        {
            var tex = NewTexture(w, h);
            var px = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;                 // 0 at the base, 1 at the top

                // Body taper plus a footing at the bottom and a capital at the top. Both
                // flares are short: a column that is wide for a third of its height reads as
                // a cone, and a cone reads as a pile rather than as something placed.
                float half = Mathf.Lerp(0.30f, 0.25f, v);
                half += 0.09f * Mathf.Clamp01(1f - v / 0.09f);
                half += 0.07f * Mathf.Clamp01((v - 0.86f) / 0.14f);

                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f;   // -1..1 across the shaft
                    float d = Mathf.Abs(u);

                    // One texel of feather, no more: this is the rig's OPAQUE layer and a soft
                    // edge on it is what turns "something was placed here" back into a glow.
                    float a = Mathf.Clamp01((half - d) * w * 0.5f);
                    if (a <= 0f) { px[y * w + x] = Color.clear; continue; }

                    // Cylindrical shading: bright a third of the way from the left, falling to
                    // the right-hand seam. Plus a slow vertical lift so the capital catches
                    // more light than the footing.
                    float across = Mathf.Clamp01((u / Mathf.Max(1e-4f, half) + 1f) * 0.5f);
                    float round = 0.62f + 0.38f * Mathf.Sin(Mathf.Clamp01(across) * Mathf.PI);
                    round *= 1f - 0.20f * Mathf.Clamp01(across - 0.55f);
                    float lift = Mathf.Lerp(0.80f, 1f, v);

                    // Two carved bands. They are the only thing that gives the shaft a SCALE:
                    // an unbroken column could be one metre tall or ten.
                    float band = Mathf.Max(
                        Mathf.Clamp01(1f - Mathf.Abs(v - 0.20f) * 55f),
                        Mathf.Clamp01(1f - Mathf.Abs(v - 0.78f) * 55f));
                    float grain = 0.94f + 0.06f * Mathf.PerlinNoise(u * 6f + 11f, v * 22f);

                    float value = Mathf.Clamp01(round * lift * grain * (1f - 0.28f * band));
                    px[y * w + x] = new Color(value, value, value, a);
                }
            }

            tex.SetPixels(px); tex.Apply();
            // Base pivot: the shaft stands on the ground the transform sits on.
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), w);
        }

        /// <summary>
        /// A patch of burnt ground. The edge is eaten away by noise and the interior is
        /// mottled, because a clean disc at uniform alpha is a decal and a fire does not leave
        /// one.
        /// </summary>
        private static Sprite BuildScorch(int n)
        {
            var tex = NewTexture(n, n);
            var px = new Color[n * n];
            float c = n * 0.5f;

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);

                    // The rim wobbles by angle, so the patch is a blot rather than a circle.
                    float wobble = 0.80f
                                 + 0.13f * Mathf.Sin(ang * 3f + 0.7f)
                                 + 0.07f * Mathf.Sin(ang * 7f + 2.1f);
                    float edge = Mathf.Clamp01((wobble - r) / 0.20f);

                    // Interior mottling: charcoal is not uniform, and at full alpha the patch
                    // would hide the tile it is burnt into.
                    float mottle = 0.62f + 0.38f * Mathf.PerlinNoise(dx * 3.4f + 4f, dy * 3.4f + 9f);
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(edge * mottle));
                }

            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        /// <summary>
        /// A lumpy soft blob. The lobes are what separate a cloud from a radial gradient — four
        /// of <see cref="ElementalSprites.Glow"/> overlapping still read as four discs.
        /// </summary>
        private static Sprite BuildPuff(int n)
        {
            var tex = NewTexture(n, n);
            var px = new Color[n * n];
            float c = n * 0.5f;

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);

                    float lobes = 1f
                                + 0.16f * Mathf.Sin(ang * 4f + 1.3f)
                                + 0.09f * Mathf.Sin(ang * 9f - 0.4f);
                    float rr = r / Mathf.Max(0.35f, lobes);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - rr), 2.0f);

                    // Interior break-up, so a large puff does not read as a lens flare.
                    a *= 0.80f + 0.20f * Mathf.PerlinNoise(dx * 2.6f + 17f, dy * 2.6f + 3f);
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }

            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        /// <summary>
        /// A discharge along +X with a hot core and a soft halo. Five kinks: fewer reads as a
        /// bent line, more reads as noise at the sizes an arc is actually drawn at.
        /// </summary>
        private static Sprite BuildArc(int w, int h)
        {
            var tex = NewTexture(w, h);
            var px = new Color[w * h];

            var pts = new[]
            {
                new Vector2(0.00f,  0.00f),
                new Vector2(0.19f,  0.52f),
                new Vector2(0.38f, -0.34f),
                new Vector2(0.57f,  0.40f),
                new Vector2(0.78f, -0.22f),
                new Vector2(1.00f,  0.00f),
            };

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float v = ((y + 0.5f) / h) * 2f - 1f;
                    var p = new Vector2(u, v);

                    float best = 10f;
                    for (int k = 0; k < pts.Length - 1; k++)
                        best = Mathf.Min(best, DistSegment(p, pts[k], pts[k + 1]));

                    // Distances are in "half-height" units on Y and "length" units on X, so the
                    // multipliers below are deliberately different from a square sprite's.
                    float core = Mathf.Max(0f, 1f - best * 26f);
                    float halo = Mathf.Max(0f, 1f - best * 7f) * 0.35f;

                    // Taper both ends so an arc emerges from a point and lands on one rather
                    // than starting at full width in mid-air.
                    float taper = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.06f);
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((core + halo) * taper));
                }

            tex.SetPixels(px); tex.Apply();
            // Left-centre pivot. See the Arc doc for why.
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0f, 0.5f), w);
        }

        /// <summary>A horizontal capsule, brightest along its spine and tapering at both ends.</summary>
        private static Sprite BuildStreak(int w, int h)
        {
            var tex = NewTexture(w, h);
            var px = new Color[w * h];

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float v = ((y + 0.5f) / h) * 2f - 1f;

                    float along = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);       // 0 -> 1 -> 0
                    float across = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v)), 1.6f);
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Pow(along, 0.8f) * across));
                }

            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
        }

        private static float DistSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
