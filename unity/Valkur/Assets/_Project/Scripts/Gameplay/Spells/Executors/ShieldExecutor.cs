using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Raises a sphere of force around the caster, turning away every blow for a duration.
    ///
    /// <para><c>radius</c> IS THE SPHERE RADIUS, IN WORLD UNITS. It used to be divided by 16 —
    /// the same leftover from the Python build that shipped <c>wall_ice</c> as a barrier
    /// twelve screen pixels wide — and then it did not matter anyway, because the controller's
    /// first act was to overwrite the root scale with a hard-coded <c>1.2</c>. Every shield was
    /// the same size and the authored dial reached nothing at all.</para>
    ///
    /// <para><c>particleColor</c> is the one swatch the whole palette is derived from, exactly
    /// as for an energy charge. Before this the colour was hard-coded blue in the controller
    /// and the authored value was passed only to a telegraph that flashed for 0.4 s.</para>
    /// </summary>
    public class ShieldExecutor : ISpellExecutor
    {
        private const float DefaultDurationSeconds = 5f;

        /// <summary>
        /// Fallback radius as a multiple of the caster's own height, used when the spell
        /// authors none. A sphere has to clear the silhouette it encloses or it reads as a
        /// belt; slightly over half the body height puts the shell just outside the shoulders.
        /// </summary>
        private const float RadiusPerBodyHeight = 0.62f;

        private const float FallbackRadius = 1.5f;

        public void Execute(SpellContext ctx)
        {
            if (ctx.Caster == null || ctx.Spell == null) return;

            float duration = ctx.Spell.duration > 0f ? ctx.Spell.duration : DefaultDurationSeconds;
            float radius = ctx.Spell.radius > 0f
                ? ctx.Spell.radius
                : ResolveRadiusFromBody(ctx.Caster);

            var go = new GameObject("SpellShield_" + ctx.Spell.spellKey);
            // Identity rotation and unit scale, and never parented to the caster: a Light2D
            // under a scaled transform renders its authored radius at some other value, and
            // the entity's scale is exactly what parenting would inherit.
            go.transform.position = ctx.Caster.position;

            var controller = go.AddComponent<ShieldController>();

            // TRACKED BEFORE IT IS INITIALIZED, and the order is load-bearing. Tracking is what
            // evicts the previous shield, and eviction RESTORES the invincibility flag that
            // shield had claimed. Initialize first and the sequence runs backwards: the new
            // shield claims the flag, then the old one's teardown puts it back to `false` — so
            // the player stands unprotected inside a shell that has just visibly closed around
            // them. Measured: cast twice and IsInvincible came back False with both spheres on
            // screen.
            SpellEffectRegistry.Track(go, ctx.Spell, ctx.Caster.gameObject);

            controller.Initialize(new ShieldController.Setup
            {
                Caster = ctx.Caster,
                Duration = duration,
                // Shares the charge's palette derivation on purpose: the question is the same
                // one — turn a single authored swatch into an ordered core/mid/edge/light set
                // that cannot be authored inside-out — and it is already pinned by tests.
                Palette = KiPalette.From(ctx.Spell.particleColor, 0.6f),
                Radius = radius,
                // wallHP is reused as the absorb pool. Reuse rather than a new field because
                // the two mean the same thing -- how much punishment this piece of conjured
                // matter takes before it fails -- and the wall already had the better name for
                // it. Zero keeps the historical pure-timer shield, which is what
                // sphere_magic_shield authors.
                AbsorbPool = ctx.Spell.wallHP,
            });
        }

        private static float ResolveRadiusFromBody(Transform caster)
        {
            var renderer = caster.GetComponentInChildren<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) return FallbackRadius;

            float height = renderer.bounds.size.y;
            return height > 0.1f ? height * RadiusPerBodyHeight : FallbackRadius;
        }
    }
}
