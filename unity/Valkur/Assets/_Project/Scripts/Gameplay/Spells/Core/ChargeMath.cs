using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The single answer to "how much of this spell did the caster actually charge".
    ///
    /// <para>Hold-to-charge is authored as four fields on <see cref="SpellDefinition"/> and
    /// delivered as one scalar on <see cref="SpellContext.ChargeFraction"/>. Every consumer —
    /// the executor that scales the damage, the rig that sizes the ball, the caster that
    /// decides when the shot is released — has to agree about what a half-held cast means, and
    /// the cheapest way to guarantee that is for none of them to do the arithmetic.</para>
    ///
    /// <para>The neutral answer for a spell that does not charge is <b>1</b>, never 0. That is
    /// the whole reason this helper exists rather than the multiplication being inlined: a
    /// struct field defaults to 0, so an executor reading <c>ctx.ChargeFraction</c> raw would
    /// make every existing projectile in the game deal its minimum damage the moment the field
    /// was added — silently, and only for the spells nobody thought to re-test.</para>
    /// </summary>
    public static class ChargeMath
    {
        /// <summary>
        /// Fraction of the charged values this cast earns, in <c>[chargeMinFraction, 1]</c>.
        /// Returns 1 for a spell that is not chargeable.
        /// </summary>
        public static float Resolve(SpellDefinition spell, float chargeFraction)
        {
            if (spell == null || !spell.IsChargeable) return 1f;
            float min = Mathf.Clamp01(spell.chargeMinFraction);
            return Mathf.Lerp(min, 1f, Mathf.Clamp01(chargeFraction));
        }

        /// <summary>
        /// Damage multiplier for this cast. A non-chargeable spell gets exactly 1, so an
        /// executor can multiply unconditionally.
        /// </summary>
        public static float DamageMultiplier(SpellDefinition spell, float chargeFraction)
        {
            if (spell == null || !spell.IsChargeable) return 1f;
            float top = Mathf.Max(0f, spell.chargeDamageMultiplier);
            if (top <= 0f) top = 1f;
            return Mathf.Lerp(1f, top, Resolve(spell, chargeFraction));
        }

        /// <summary>
        /// Size multiplier for this cast — the visual scale and, where an executor honours
        /// it, the hit radius. Kept separate from the damage curve because they are genuinely
        /// different dials: a charge that doubles its damage and doubles its width at the same
        /// rate reads as one number, and the spell loses the "small and sharp versus big and
        /// slow" reading that makes charging interesting.
        /// </summary>
        public static float ScaleMultiplier(SpellDefinition spell, float chargeFraction)
        {
            if (spell == null || !spell.IsChargeable) return 1f;
            float top = Mathf.Max(0f, spell.chargeScaleMultiplier);
            if (top <= 0f) top = 1f;
            return Mathf.Lerp(1f, top, Resolve(spell, chargeFraction));
        }

        /// <summary>
        /// True when this cast is close enough to full that the spell should show its
        /// "fully charged" behaviour — the splash, the heavier kick, the brighter core.
        /// One threshold, shared, so the visual promise and the mechanical payoff cannot
        /// land at different points on the ramp.
        /// </summary>
        public const float FULL_CHARGE_THRESHOLD = 0.92f;

        public static bool IsFullyCharged(SpellDefinition spell, float chargeFraction)
            => spell != null && spell.IsChargeable && chargeFraction >= FULL_CHARGE_THRESHOLD;
    }
}
