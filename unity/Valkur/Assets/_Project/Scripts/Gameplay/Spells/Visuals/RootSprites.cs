using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Procedural sprites for the root field. Built once, cached in statics guarded by
    /// <c>== null</c> so they survive Domain Reload being OFF the same way
    /// <see cref="ElementalSprites"/> and <c>TornadoSprites</c> do.
    ///
    /// <para>THE TENDRIL PIVOT IS AT ITS BASE, and that is the whole reason this class
    /// exists instead of another entry in <see cref="ElementalSprites"/>, every one of
    /// which is centre-pivoted and exactly 1x1 world unit. A stem grows UP OUT OF THE
    /// GROUND: with a centre pivot, scaling it sinks half the stem into the floor and
    /// raises the other half off it, so the sprout has to be faked by moving the transform
    /// in lockstep with the scale — two numbers that must agree and eventually will not.
    /// With the pivot on the base, <c>localScale.y</c> IS the height in world units and
    /// nothing else moves.</para>
    ///
    /// <para>SHADING IS BAKED INTO RGB, NOT LEFT TO THE TINT. A silhouette in flat white
    /// takes one colour from <c>SpriteRenderer.color</c> and reads as a paper cut-out. The
    /// stem texture ramps from a dark base to a bright tip in VALUE only, so the single
    /// authored colour multiplies through and comes out shaded — the base of every stem is
    /// in its own shadow for free, at no extra draw call.</para>
    /// </summary>
    internal static class RootSprites
    {
        private static Sprite _tendril, _clod, _crack, _burst;

        /// <summary>
        /// Domain Reload is OFF: the managed handles survive a recompile while the native
        /// Sprites and their Texture2Ds do not, so a cached entry would be a destroyed
        /// object on the second Play. Assigning the fields directly is also the only reset
        /// shape <c>DomainReloadStaticResetTests</c> reads as a reset — it scans the IL, so
        /// clearing through a helper that takes the field as an ARGUMENT counts as nothing.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tendril = null;
            _clod = null;
            _crack = null;
            _burst = null;
        }

        /// <summary>
        /// A tapered, barbed stem. Pivot on the base; one world unit TALL at scale 1, and
        /// <see cref="TendrilWorldWidth"/> wide. Bends to the RIGHT in texture space — the
        /// rig mirrors it on X for half the field so a patch is not every stem leaning the
        /// same way.
        /// </summary>
        public static Sprite Tendril { get { EnsureAll(); return _tendril; } }

        /// <summary>An opaque chip of thrown earth. Centre pivot, 1x1 world unit.</summary>
        public static Sprite Clod { get { EnsureAll(); return _clod; } }

        /// <summary>
        /// A fissure running along +X from the pivot, so rotating it aims it outward.
        /// Pivot at the inner end. One world unit long.
        /// </summary>
        public static Sprite Crack { get { EnsureAll(); return _crack; } }

        /// <summary>A soft radial pop for the moment a stem breaks the surface. Centre pivot.</summary>
        public static Sprite Burst { get { EnsureAll(); return _burst; } }

        /// <summary>
        /// How wide a tendril is at scale 1, in world units. The texture is half as wide as
        /// it is tall and <c>pixelsPerUnit</c> is its HEIGHT, so the height is the unit and
        /// this is the leftover. Exposed because the rig sizes the lean off it, and a test
        /// pins it.
        /// </summary>
        public const float TendrilWorldWidth = 0.5f;

        /// <summary>
        /// Fraction of the texture height the drawn stem actually reaches. The top rows are
        /// deliberately empty so a bilinear filter has somewhere to fade into instead of
        /// clipping the tip flat — the same reason the atlas padding rule exists.
        /// </summary>
        public const float TendrilFill = 0.94f;

        public static void EnsureAll()
        {
            if (_tendril == null) _tendril = MakeTendril(64, 128);
            if (_clod == null) _clod = MakeClod(32);
            if (_crack == null) _crack = MakeCrack(64, 16);
            if (_burst == null) _burst = MakeBurst(48);
        }

        // Four barbs up the stem, each an outward spike. Fractions of the height, and
        // deliberately NOT evenly spaced: an even ladder reads as a machined part.
        [Valkur.Core.SelfHealingStatic("Immutable lookup tables of float literals, read once per texture build and never written. Hold no Unity objects, so nothing here can go stale across a Play session.")]
        private static readonly float[] BarbAt = { 0.26f, 0.45f, 0.63f, 0.80f };
        [Valkur.Core.SelfHealingStatic("See BarbAt: immutable float literals, never written.")]
        private static readonly float[] BarbSide = { 1f, -1f, 1f, -1f };
        [Valkur.Core.SelfHealingStatic("See BarbAt: immutable float literals, never written.")]
        private static readonly float[] BarbLen = { 0.30f, 0.26f, 0.21f, 0.15f };

        private static Sprite MakeTendril(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            float texel = 2f / w;

            for (int y = 0; y < h; y++)
            {
                float t = (y + 0.5f) / h;                    // 0 at the base, 1 at the top
                float along = Mathf.Clamp01(t / TendrilFill);

                // The stem's own curve, in normalized x. A straight stem is a stick; the
                // bend makes it read as something that grew.
                float curve = Mathf.Sin(along * 2.35f) * 0.30f * Mathf.Pow(along, 0.75f);

                // Half-width: thick at the base, needle at the tip, with a slight swell
                // just above the ground so the stem looks anchored rather than pushed in.
                float taper = Mathf.Pow(1f - along, 1.35f);
                float swell = 1f + 0.35f * Mathf.Exp(-Mathf.Pow((along - 0.06f) / 0.10f, 2f));
                float half = (0.030f + 0.170f * taper) * swell;

                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w * 2f - 1f;     // -1..1 across the texture
                    float d = Mathf.Abs(nx - curve);
                    float a = Mathf.Clamp01((half - d) / texel);

                    // Barbs: short spikes leaving the stem sideways and slightly upward.
                    for (int b = 0; b < BarbAt.Length; b++)
                    {
                        float by = BarbAt[b];
                        float baseX = curve + half * BarbSide[b] * 0.6f;
                        float tipX = baseX + BarbSide[b] * BarbLen[b];
                        float tipY = by + BarbLen[b] * 0.55f;
                        float dist = DistSegment(new Vector2(nx, along),
                                                 new Vector2(baseX, by),
                                                 new Vector2(tipX, tipY));
                        // Thickness tapers along the barb the way the stem does.
                        float u = Mathf.Clamp01((along - by) / Mathf.Max(1e-4f, tipY - by));
                        float barbHalf = 0.045f * (1f - u * 0.85f);
                        a = Mathf.Max(a, Mathf.Clamp01((barbHalf - dist) / texel));
                    }

                    if (t > TendrilFill) a = 0f;

                    // Fibre: a faint lengthwise grain so a wide stem is not a flat slab.
                    float fibre = 0.88f + 0.12f * Mathf.Sin((nx - curve) * 46f + along * 7f);

                    // Value ramp, base to tip. This is what the authored colour multiplies
                    // through, and it is the entire reason the stem reads as round.
                    float shade = Mathf.Lerp(0.42f, 1f, Mathf.Pow(along, 0.65f)) * fibre;

                    px[y * w + x] = new Color(shade, shade, shade, Mathf.Clamp01(a));
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            // pixelsPerUnit = HEIGHT: one world unit tall, TendrilWorldWidth wide.
            // Pivot a hair above the very bottom row so the base sinks into the soil
            // instead of hovering on it.
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.03f), h);
        }

        private static Sprite MakeClod(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float ang = Mathf.Atan2(dy, dx);
                    // Lumpy radius: three harmonics, so no two bearings match and the chip
                    // never reads as a circle at any rotation.
                    float r = 0.62f + 0.16f * Mathf.Sin(ang * 3f + 0.7f)
                                    + 0.09f * Mathf.Sin(ang * 5f - 1.9f)
                                    + 0.05f * Mathf.Sin(ang * 8f + 2.6f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((r - d) * (size * 0.25f));
                    // Lit from above-left, the same key every other ground piece assumes.
                    float shade = Mathf.Clamp01(0.55f + 0.45f * (0.35f * -dx + 0.65f * dy));
                    px[y * size + x] = new Color(shade, shade, shade, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Sprite MakeCrack(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;                    // 0 inner, 1 outer
                    float v = (y + 0.5f) / h * 2f - 1f;          // -1..1 across
                    // Wanders, and thins to nothing at the far end.
                    float centre = 0.18f * Mathf.Sin(u * 6.1f) * u;
                    float half = 0.34f * Mathf.Pow(1f - u, 1.6f);
                    float a = Mathf.Clamp01((half - Mathf.Abs(v - centre)) * h * 0.10f);
                    // Fades in from the pivot too, so a ring of these does not draw a hard
                    // rosette at the centre.
                    a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 4f));
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            // pixelsPerUnit = WIDTH: one world unit long, pivot at the inner end, so
            // localScale.x is the reach and the rotation is the bearing.
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0f, 0.5f), w);
        }

        private static Sprite MakeBurst(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.6f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static float DistSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
