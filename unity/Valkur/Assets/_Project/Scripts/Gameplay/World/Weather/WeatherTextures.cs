using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// The procedural textures every weather layer draws with, built once and shared.
    ///
    /// Procedural rather than authored PNGs because these are pure gradients — a drop is a
    /// soft capsule, a splash is a soft annulus — and an imported sprite would additionally
    /// have to survive the atlas rules, the PPU policy and the postprocessor. Nothing here
    /// is art; it is the alpha ramp a layer's start colour is multiplied through.
    ///
    /// Every texture is white RGB with shaped alpha, so a layer's colour is entirely a
    /// property of its start colour and the day/night tint folded into it.
    /// </summary>
    internal static class WeatherTextures
    {
        // Domain Reload is OFF and these are runtime-created Texture2Ds: the managed handles
        // survive a recompile while the native objects do not, so a stale entry would hand a
        // destroyed texture to a material on the second Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _cache.Clear();

        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// A horizontal soft capsule, for every layer rendered in
        /// <see cref="ParticleSystemRenderMode.Stretch"/>.
        ///
        /// HORIZONTAL is not a style choice. Unity's stretched billboard aligns the quad's
        /// U axis with the particle's velocity, so a texture drawn as a vertical strip is
        /// stretched across its own short axis: the rain used to ship a 4x16 vertical strip
        /// and every drop rendered as a smear perpendicular to its fall.
        /// </summary>
        public static Texture2D Streak(int w, int h, float coreBias = 1f)
            => Get($"streak_{w}x{h}_{coreBias:F2}", w, h, (u, v) =>
            {
                // Fade to nothing at both ends of the long axis so a stretched drop has no
                // hard head or tail, and taper across the short axis so it has a lit core
                // rather than a flat bar.
                float along  = Mathf.Sin(u * Mathf.PI);
                float across = Mathf.Sin(v * Mathf.PI);
                return Mathf.Pow(along, 0.65f) * Mathf.Pow(across, coreBias);
            });

        /// <summary>
        /// A soft round dot. <paramref name="falloff"/> below 1 keeps the core solid and
        /// fades late (a snowflake); above 1 fades immediately from the centre (a haze puff).
        /// </summary>
        public static Texture2D Dot(int n, float falloff = 0.7f)
            => Get($"dot_{n}_{falloff:F2}", n, n, (u, v) =>
            {
                float dx = u - 0.5f, dy = v - 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                return Mathf.Pow(Mathf.Clamp01(1f - d), falloff);
            });

        /// <summary>
        /// A six-armed snow crystal. Only the near snow layer uses it: at the two or three
        /// screen pixels a far flake occupies, arms are indistinguishable from a dot and cost
        /// an extra texture bind, so the far layers stay on <see cref="Dot"/>.
        /// </summary>
        public static Texture2D Crystal(int n)
            => Get($"crystal_{n}", n, n, (u, v) =>
            {
                float dx = u - 0.5f, dy = v - 0.5f;
                float r  = Mathf.Sqrt(dx * dx + dy * dy) * 2f;   // 0 at centre, 1 at edge
                if (r > 1f) return 0f;

                // Fold the angle into one 60-degree sector so three lines through the centre
                // become six arms without drawing six of anything.
                float ang     = Mathf.Atan2(dy, dx);
                float sector  = Mathf.Repeat(ang, Mathf.PI / 3f) - Mathf.PI / 6f;
                float offAxis = Mathf.Abs(Mathf.Sin(sector)) * r;  // distance from the nearest arm

                float arm    = Mathf.Exp(-Mathf.Pow(offAxis / 0.085f, 2f)) * Mathf.Clamp01(1f - r);
                float centre = Mathf.Exp(-Mathf.Pow(r / 0.30f, 2f));

                // Two rings of side spurs at 40% and 68% of the radius; without them the arms
                // read as a plain asterisk.
                float spur = Mathf.Exp(-Mathf.Pow((r - 0.40f) / 0.10f, 2f))
                           + Mathf.Exp(-Mathf.Pow((r - 0.68f) / 0.09f, 2f));
                spur *= Mathf.Exp(-Mathf.Pow(offAxis / 0.26f, 2f)) * 0.42f;

                return Mathf.Clamp01(arm + centre * 0.85f + spur);
            });

        /// <summary>
        /// A soft annulus — the expanding ring a raindrop leaves where it lands. Slightly
        /// brighter on the outer edge than the inner one, which is what makes a flat ring
        /// read as a ripple travelling outward rather than as a circle being scaled up.
        /// </summary>
        public static Texture2D Ring(int n)
            => Get($"ring_{n}", n, n, (u, v) =>
            {
                float dx = u - 0.5f, dy = v - 0.5f;
                float r  = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                if (r > 1f) return 0f;
                float band = Mathf.Exp(-Mathf.Pow((r - 0.68f) / 0.17f, 2f));
                float lead = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.09f, 2f)) * 0.45f;
                return Mathf.Clamp01(band + lead) * Mathf.Clamp01((1f - r) / 0.22f + 0.35f);
            });

        /// <summary>
        /// A small tapered blade — the leaf/debris the wind carries. Drawn as an ellipse
        /// pinched toward +X so it has a nose and a tail; particle rotation does the rest.
        /// </summary>
        public static Texture2D Leaf(int n)
            => Get($"leaf_{n}", n, n, (u, v) =>
            {
                float x = u * 2f - 1f;              // -1..1 along the blade
                float y = (v - 0.5f) * 2f;          // -1..1 across it
                float halfWidth = Mathf.Cos(x * Mathf.PI * 0.5f);        // 1 at centre, 0 at the tips
                halfWidth *= Mathf.Lerp(0.85f, 0.45f, Mathf.Clamp01(x)); // taper the nose
                if (halfWidth <= 0.001f) return 0f;
                float across = Mathf.Abs(y) / halfWidth;
                if (across > 1f) return 0f;
                // A soft body with a defined spine, so a tumbling leaf still has an edge when
                // it turns side-on and its silhouette collapses toward a line.
                float body  = Mathf.Pow(1f - across, 0.55f);
                float spine = Mathf.Exp(-Mathf.Pow(across / 0.18f, 2f)) * 0.35f;
                return Mathf.Clamp01(body + spine);
            });

        // ── build + cache ────────────────────────────────────────────────────────────

        private static Texture2D Get(string key, int w, int h, System.Func<float, float, float> alpha)
        {
            // Unity's overloaded null also catches a texture destroyed by a Play-mode exit.
            if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name       = $"Weather_{key}",
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
                hideFlags  = HideFlags.DontSave,
            };

            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float a = Mathf.Clamp01(alpha(u, v));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);

            _cache[key] = tex;
            return tex;
        }
    }
}
