using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Procedural sprite family for crystalline ice: shard bodies, their rim light,
    /// their inner facet, a crack overlay, ground rime and flying debris.
    ///
    /// <para>WHY A SECOND LIBRARY. <see cref="ElementalSprites"/> is radial — every one of
    /// its eleven sprites is a disc, a ring or a star, which is the right vocabulary for
    /// something that happens AT a point. A wall happens along a LINE, and the thing that
    /// makes ice read as ice is the silhouette: a row of tapered spikes of uneven height,
    /// each with a bright edge and a dark translucent core. None of that can be assembled
    /// out of circles, which is exactly why the old ice wall (an <see cref="AreaFXRig"/> of
    /// concentric discs stretched onto a sub-pixel quad) never looked like one.</para>
    ///
    /// <para>Unlike <see cref="ElementalSprites"/> these textures carry COLOUR, not just
    /// alpha. A single <c>SpriteRenderer.color</c> cannot express "deep blue at the base,
    /// near-white at the tip", and that vertical gradient is most of what sells depth in a
    /// flat top-down projection. The renderer tint still works — it multiplies — so the
    /// controller keeps fading and flashing through <c>color</c> as usual.</para>
    /// </summary>
    internal static partial class IceSprites
    {
        /// <summary>How many distinct shard silhouettes are generated and shared.</summary>
        public const int VariantCount = 5;

        /// <summary>
        /// World size of a shard sprite at <c>localScale = 1</c>: 1 unit wide, 2 units tall.
        /// The textures are 64x128 at 64 PPU, so a caller that wants a shard W wide and H
        /// tall sets <c>localScale = (W, H / ShardUnitHeight)</c> — which is what
        /// <see cref="ScaleShard"/> does, so nobody has to remember the constant.
        /// </summary>
        public const float ShardUnitHeight = 2f;

        private const int ShardW = 64;
        private const int ShardH = 128;

        private static Sprite[] _body;
        private static Sprite[] _rim;
        private static Sprite[] _facet;
        private static Sprite[] _crack;
        private static Sprite _rime;
        private static Sprite _debris;

        /// <summary>Opaque crystal silhouette, base-pivoted, with its colour baked in.</summary>
        public static Sprite Body(int variant) { EnsureAll(); return _body[Wrap(variant)]; }

        /// <summary>Edge-only band of the matching body, for an additive rim light.</summary>
        public static Sprite Rim(int variant) { EnsureAll(); return _rim[Wrap(variant)]; }

        /// <summary>Inner specular wedge of the matching body, for an additive highlight.</summary>
        public static Sprite Facet(int variant) { EnsureAll(); return _facet[Wrap(variant)]; }

        /// <summary>Branching fracture lines clipped to the matching body.</summary>
        public static Sprite Crack(int variant) { EnsureAll(); return _crack[Wrap(variant)]; }

        /// <summary>Soft elongated frost patch for the ground under the wall. 2x1 units.</summary>
        public static Sprite Rime { get { EnsureAll(); return _rime; } }

        /// <summary>Small angular ice chunk thrown by a shatter. 1x1 unit.</summary>
        public static Sprite Debris { get { EnsureAll(); return _debris; } }

        /// <summary>
        /// Size a shard child in world units, hiding <see cref="ShardUnitHeight"/> from
        /// every caller. The sprite is base-pivoted, so this grows upward from the ground.
        /// </summary>
        public static void ScaleShard(Transform t, float widthWu, float heightWu)
            => t.localScale = new Vector3(widthWu, heightWu / ShardUnitHeight, 1f);

        /// <summary>
        /// Domain Reload is OFF, so these six statics carry DESTROYED native objects into the
        /// next Play session. Nulling the array fields is a plain <c>stsfld</c>, which is what
        /// <c>DomainReloadStaticResetTests</c> reads the IL for — clearing the arrays in place
        /// would pass the field as an argument and register as no reset at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _body = null;
            _rim = null;
            _facet = null;
            _crack = null;
            _rime = null;
            _debris = null;
        }

        private static int Wrap(int variant)
            => ((variant % VariantCount) + VariantCount) % VariantCount;

        public static void EnsureAll()
        {
            if (_body != null && _body.Length == VariantCount && _body[0] != null) return;

            _body = new Sprite[VariantCount];
            _rim = new Sprite[VariantCount];
            _facet = new Sprite[VariantCount];
            _crack = new Sprite[VariantCount];

            for (int v = 0; v < VariantCount; v++)
            {
                var shape = ShardShape.For(v, ShardH);
                _body[v] = BuildBody(shape);
                _rim[v] = BuildRim(shape);
                _facet[v] = BuildFacet(shape);
                _crack[v] = BuildCrack(shape, v);
            }

            _rime = BuildRime(160, 80);
            _debris = BuildDebris(32);
        }

        // ── shared raster helpers ────────────────────────────────────────────────────

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

        internal static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
