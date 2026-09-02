using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Entry point for every <see cref="SpellType.Slash"/>. It resolves the shared cast
    /// origin, picks the tint, and hands the cast to the attack that owns both the drawing
    /// and the damage from there on.
    ///
    /// There are two of those attacks and no third path. <c>slash_regular</c> keeps its own
    /// authored crescent; everything else runs <see cref="SlashAttack"/>, whose silhouette
    /// and timing come from the spell's arc, radius and lifetime. The old fallbacks — a
    /// fixed blade sprite that ignored the arc, and a 3D vendor prefab authored for a
    /// perspective camera — are gone: neither could tell a 40 degree thrust from a 260
    /// degree sweep, and both drew somewhere other than where the damage landed.
    /// </summary>
    public class SlashExecutor : ISpellExecutor
    {
        /// <summary>
        /// Minimum max-channel brightness for <c>slash_regular</c>'s tint, so an edit to
        /// its colour can never render the authored crescent as a near-invisible
        /// silhouette. <see cref="SlashAttack"/> needs no such floor — it renders a dark
        /// tint as a void blade with a bright rim instead of lifting it towards grey.
        /// </summary>
        private const float MIN_REGULAR_SLASH_BRIGHTNESS = 0.35f;

        /// <summary>Tint used when a slash leaves <c>particleColor</c> unset.</summary>
        private static readonly Color DefaultTint = new Color(0.92f, 0.95f, 1f, 1f);

        /// <summary>
        /// The colour this slash actually draws with — the blade, and the cast gather that
        /// precedes it, so the two cannot disagree.
        ///
        /// <para>The "unset" test used to be <c>particleColor != Color.clear</c>, and NO
        /// shipped spell has an alpha-zero swatch, so that branch was unreachable and
        /// <see cref="DefaultTint"/> was dead code. The real sentinel for an untouched
        /// <c>particleColor</c> is OPAQUE WHITE — <see cref="KiPalette.IsUnauthored"/> owns
        /// that rule — and using the wrong one had a visible cost: plain <c>slash</c> swung a
        /// pure-white arc while its flourish, which does use the right sentinel, read the same
        /// field as unauthored and gathered arcane violet.</para>
        ///
        /// <para>The regular slash's brightness floor is applied HERE rather than at the call
        /// site, because a gather that ignored it would be darker than the blade it announces.</para>
        /// </summary>
        public static Color ResolveTint(SpellDefinition spell)
        {
            if (spell == null) return DefaultTint;

            Color tint = KiPalette.IsUnauthored(spell.particleColor)
                ? DefaultTint
                : spell.particleColor;

            return RegularSlashAttack.Matches(spell)
                ? EnsureMinBrightness(tint, MIN_REGULAR_SLASH_BRIGHTNESS)
                : tint;
        }

        /// <summary>Arc used when a slash leaves <c>arcRangeDegrees</c> unset.</summary>
        private const float DEFAULT_ARC_DEGREES = 90f;

        /// <summary>Reach used when neither <c>hitRadius</c> nor <c>range</c> is authored.</summary>
        private const float DEFAULT_HIT_RADIUS = 1.5f;

        public void Execute(SpellContext ctx)
        {
            float arc = ctx.Spell.arcRangeDegrees > 0f
                ? ctx.Spell.arcRangeDegrees
                : DEFAULT_ARC_DEGREES;

            float hitRadius = ctx.Spell.hitRadius > 0f ? ctx.Spell.hitRadius : ctx.Spell.range;
            if (hitRadius <= 0f) hitRadius = DEFAULT_HIT_RADIUS;

            // The arc begins at Fireball's canonical hand point. Gameplay geometry and the
            // visual share it, so the swing cannot detach from its damage area.
            Vector2 castStart = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);

            Color tint = ResolveTint(ctx.Spell);

            if (RegularSlashAttack.Matches(ctx.Spell))
            {
                RegularSlashAttack.Spawn(ctx, castStart, hitRadius, arc, tint);
                return;
            }

            // SlashAttack reports its own hits through GameEvents.FireHitDealt as it sweeps,
            // so the combo counter sees each target on the frame the edge crosses it.
            SlashAttack.Spawn(ctx, castStart, hitRadius, arc, tint);
        }

        /// <summary>
        /// Lifts <paramref name="c"/>'s brightness so its strongest channel reaches at least
        /// <paramref name="floor"/>, preserving the hue ratio between channels and the alpha.
        /// A pure-black input is promoted to a neutral grey at the floor brightness.
        /// </summary>
        private static Color EnsureMinBrightness(Color c, float floor)
        {
            float maxComp = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (maxComp >= floor) return c;
            if (maxComp <= 0.0001f) return new Color(floor, floor, floor, c.a);
            float scale = floor / maxComp;
            return new Color(
                Mathf.Min(1f, c.r * scale),
                Mathf.Min(1f, c.g * scale),
                Mathf.Min(1f, c.b * scale),
                c.a);
        }
    }
}
