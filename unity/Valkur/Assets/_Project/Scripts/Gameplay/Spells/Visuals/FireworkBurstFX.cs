using UnityEngine;
using Valkur.Core;
using Valkur.Data.Feel;
using Valkur.Gameplay.Feel;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The shell opening: a chrysanthemum of coloured stars over a white flash, a shockwave
    /// ring, and embers falling out of it.
    ///
    /// <para>WHY A RIG AND NOT A PRESET. The same argument <c>VortexFunnelFX</c> and
    /// <see cref="FlameConeFX"/> record. A firework is a SHAPE — a sphere of stars thrown from
    /// one point, drooping into a willow as they slow — and a particle preset is one material
    /// and one behaviour, so a preset laid on top of a preset is a fourth uncoordinated layer
    /// rather than a firework. The rig also lets the star colours come from the spell's own
    /// swatch through <see cref="FireworkPalette"/>, which a shared preset asset cannot do.</para>
    ///
    /// <para>WHY THE STARS ARE ONE ParticleSystem AND NOT N GameObjects. The version this
    /// replaces built eighteen <c>GameObject</c>s with a <c>SpriteRenderer</c> and a
    /// <c>MonoBehaviour</c> each, per cast, with no pooling — against a project whose
    /// convention is <c>Core/ObjectPool.cs</c> for exactly this. <c>Emit(EmitParams)</c> gives
    /// per-particle velocity, colour, size and lifetime in one system and one draw call, which
    /// is the only way the star count can be a look decision rather than a budget one.</para>
    ///
    /// <para>THREE THINGS ARE LOAD-BEARING AND EASY TO UNDO:</para>
    /// <list type="bullet">
    /// <item>Everything except the embers is ADDITIVE. On the alpha material the brightest
    /// pixel a star can produce is its own colour, so a shell could never blow out — the trap
    /// <c>ElementalSprites.SharedAdditiveMaterial</c> exists to answer.</item>
    /// <item>The embers are the ONE opaque layer, and that is what separates "the sky is being
    /// lit" from "something is burning up there". <c>KiAuraFX</c> and <c>VortexFunnelFX</c>
    /// both record the same rule for their ground debris.</item>
    /// <item>Per-star alpha is divided by <see cref="STAR_ALPHA_REFERENCE_COUNT"/>, so raising
    /// <see cref="STARS"/> buys RESOLUTION and not brightness. On an additive stack a pixel
    /// receives the SUM of everything over it, so a count is otherwise a brightness dial —
    /// measured on the vortex, doubling its bands doubled its light and washed a red effect
    /// out to white.</item>
    /// </list>
    /// </summary>
    public partial class FireworkBurstFX : MonoBehaviour
    {
        // ── shape ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Stars in the shell. A resolution dial, not a brightness one — see the class doc.
        /// Below about 24 the sphere reads as a handful of sparks rather than as a burst.
        /// </summary>
        public const int STARS = 46;

        /// <summary>
        /// The count <see cref="STAR_ALPHA"/> was tuned against. Per-star alpha is divided by
        /// it, so the summed brightness of the shell is unchanged when <see cref="STARS"/>
        /// moves. Kept as its own constant rather than reusing <see cref="STARS"/> directly,
        /// so the relationship survives someone editing one of them.
        /// </summary>
        public const int STAR_ALPHA_REFERENCE_COUNT = 46;

        private const float STAR_ALPHA = 0.85f;

        /// <summary>
        /// The alpha ONE star is emitted with, for a shell of <paramref name="starCount"/>.
        /// Exposed rather than inlined so the count-independence rule can be measured directly
        /// instead of inferred from a frame of particles — a quantity spread over a run cannot
        /// be read off one sample, which this project has been bitten by twice.
        /// </summary>
        internal static float StarAlphaFor(int starCount)
            => Mathf.Clamp01(STAR_ALPHA * STAR_ALPHA_REFERENCE_COUNT / Mathf.Max(1, starCount));

        /// <summary>
        /// How wide the shockwave sprite must be drawn for its bright band to land ON
        /// <paramref name="radius"/>. <c>ElementalSprites.Ring</c> peaks at normalized radius
        /// 0.78, so the span is <c>radius / 0.39</c>. Getting this wrong is invisible in code
        /// and puts the effect's only hard contour somewhere the effect is not — measured once
        /// on the arcane flame, whose contour sat at 1.511 u against a 2.5 u circle.
        /// </summary>
        internal static float RingSpanFor(float radius) => radius / 0.39f;

        /// <summary>Embers that fall out of the shell once the stars begin to die.</summary>
        public const int EMBERS = 22;

        /// <summary>
        /// How long a star burns. Long enough that the shell droops into a willow before it
        /// goes out — the droop is the whole difference between a firework and an explosion.
        /// </summary>
        public const float STAR_LIFETIME = 1.45f;

        private const float EMBER_LIFETIME = 2.10f;

        /// <summary>
        /// The white core, over in a fifth of a second. It is what the shell OPENING looks
        /// like; everything after it is the shell burning.
        /// </summary>
        private const float FLASH_SECONDS = 0.24f;

        private const float RING_SECONDS = 0.36f;

        /// <summary>
        /// How long the light lasts, as a fraction of a star's life. Deliberately not a hard
        /// constant: a light that dies while its stars are still visibly burning is the exact
        /// failure the old 0.20 s <c>Destroy</c> produced — the flash popped off and left a
        /// lit effect sitting in unlit air.
        /// </summary>
        public const float LIGHT_LIFE_FRACTION = 0.80f;

        // ── sorting ──────────────────────────────────────────────────────────────────
        //
        // The burst happens ABOVE the rooftops, so it belongs on Overhead rather than VFX —
        // VFX still sorts under wall tops in this project's ladder. Each order is derived from
        // the one below it rather than written as a literal, the way VortexFunnelFX derives
        // ORDER_DUST: a hand-maintained number is right until the layer under it grows.

        internal const int ORDER_RING  = 40;
        internal const int ORDER_STAR  = ORDER_RING + 2;
        internal const int ORDER_EMBER = ORDER_STAR + 2;
        internal const int ORDER_FLASH = ORDER_EMBER + 2;

        // ── light ────────────────────────────────────────────────────────────────────

        private const float LIGHT_RADIUS_MUL = 2.6f;
        private const float LIGHT_BODY_INTENSITY = 2.4f;
        private const float LIGHT_CORE_INTENSITY = 1.1f;

        /// <summary>
        /// How hard the shell pushes the GLOBAL light. Below the storm strike's boost on
        /// purpose — see <see cref="SkyFlash"/>.
        /// </summary>
        private const float SKY_STRENGTH = 0.80f;

        private const float SKY_SECONDS = 0.55f;

        // ── state ────────────────────────────────────────────────────────────────────

        private FireworkPalette _palette;
        private float _radius;
        private float _age;
        private float _totalLife;

        private SpriteRenderer _flashCore, _flashGlow, _flashHalo, _ring;
        private float _ringSpan;

        /// <summary>
        /// Build a burst at <paramref name="worldPos"/>. <paramref name="radius"/> is the
        /// world-unit radius the stars reach, which is the same number the ring draws — a
        /// shell whose light and whose silhouette disagree reads as two effects.
        /// </summary>
        internal static FireworkBurstFX Spawn(Vector3 worldPos, FireworkPalette palette, float radius)
        {
            var go = new GameObject("FireworkBurst");
            go.transform.position = worldPos;

            var fx = go.AddComponent<FireworkBurstFX>();
            fx._palette = palette;
            fx._radius = Mathf.Max(0.5f, radius);
            fx.Build();
            return fx;
        }

        private void Build()
        {
            ElementalSprites.EnsureAll();

            _totalLife = Mathf.Max(STAR_LIFETIME, EMBER_LIFETIME) + 0.35f;

            BuildFlash();
            BuildRing();
            BuildLight();
            BuildStars();
            BuildEmbers();

            // The sky, the frame and the ear, in that order.
            SkyFlash.Pulse(_palette.Sky, SKY_STRENGTH, SKY_SECONDS);

            // ImpactLight and not ImpactMedium: the shell is metres overhead and cosmetic.
            // A firework that kicks the camera like a meteor lands reads as a mistake.
            CameraFeel.Cue(CameraFeelCue.ImpactLight, Vector2.up, 0.55f);

            // Guarded because Destroy is refused outright in Edit Mode, and the contract tests
            // build this rig there — the same Application.isPlaying shape SpellCastFlourishFX
            // uses. In play there is no behavioural difference.
            if (Application.isPlaying) Destroy(gameObject, _totalLife);
        }

        private void BuildFlash()
        {
            // Three concentric additive sprites: a hot centre, a bloom and a wide faint halo.
            // One sprite cannot be all three — a single quad with a soft edge is either small
            // and hard or large and vague.
            _flashCore = MakeSprite("FlashCore", ElementalSprites.HotCore, ORDER_FLASH + 2, _radius * 0.55f);
            _flashGlow = MakeSprite("FlashGlow", ElementalSprites.Glow, ORDER_FLASH + 1, _radius * 1.15f);
            _flashHalo = MakeSprite("FlashHalo", ElementalSprites.Halo, ORDER_FLASH, _radius * 2.30f);
        }

        private void BuildRing()
        {
            // ElementalSprites.Ring peaks at normalized radius 0.78, so a ring drawn to land
            // ON a world radius is scaled by radius / 0.39. Getting this wrong is invisible in
            // code and puts the only hard contour in the effect somewhere the effect is not.
            _ringSpan = RingSpanFor(_radius);
            _ring = MakeSprite("Shockwave", ElementalSprites.Ring, ORDER_RING, 0.01f);
        }

        private SpriteRenderer MakeSprite(string name, Sprite sprite, int order, float worldSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * Mathf.Max(0.001f, worldSize);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // Additive: alpha here is COVERAGE and the colour is the brightness dial, which is
            // why the envelopes below drive colour above 1 rather than reaching for alpha.
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
            sr.sortingOrder = order;
            sr.color = new Color(1f, 1f, 1f, 0f);
            return sr;
        }
    }
}
