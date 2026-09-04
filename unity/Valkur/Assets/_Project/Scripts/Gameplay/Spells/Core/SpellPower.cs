using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Answers "how hard does THIS caster hit with spells" at the moment a damage or heal
    /// number is computed.
    ///
    /// It is a lookup on the caster rather than a field threaded through
    /// <see cref="SpellContext"/> on purpose. Spell damage is read in twenty places across
    /// executors, controllers and projectiles, and half of those are persistent objects
    /// (a puddle, an aura, a totem) that outlive the context that spawned them. Widening
    /// the struct would still leave every controller needing its own copy of the number
    /// plumbed through its own Initialize — twenty signatures instead of twenty call
    /// sites, and a controller that forgot to pass it would silently scale nothing.
    ///
    /// Asking the caster is exact for the instant case and defensible for the persistent
    /// one: a totem you empowered while it burns is a channel of your CURRENT power, which
    /// is the reading the player will make of it anyway.
    ///
    /// A monster has no <see cref="PlayerStats"/>, so every one of these returns 1 and the
    /// whole mechanism is invisible to the AI side of combat.
    /// </summary>
    public static class SpellPower
    {
        /// <summary>Multiplier for a caster's spell damage and healing. 1 when the caster
        /// is null, destroyed, or has no stat store.</summary>
        public static float Of(GameObject caster)
        {
            if (caster == null) return 1f;
            var stats = caster.GetComponent<PlayerStats>();
            return stats != null ? stats.SpellDamageMultiplier : 1f;
        }

        public static float Of(Component caster)
            => caster == null ? 1f : Of(caster.gameObject);

        /// <summary>Scales a float damage / heal value by the caster's spell power.</summary>
        public static float Scale(float amount, GameObject caster)
            => amount * Of(caster);

        public static float Scale(float amount, Component caster)
            => amount * Of(caster);

        /// <summary>
        /// Scales and rounds to the integer the damage systems take. Floors at 1 whenever
        /// the authored amount was positive: a spell scaled down to 0 by a debuff would
        /// register as a miss, and a miss and a weak hit are different events to every
        /// feedback system downstream.
        /// </summary>
        public static int ScaleToInt(float amount, GameObject caster)
        {
            if (amount <= 0f) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(amount * Of(caster)));
        }

        public static int ScaleToInt(float amount, Component caster)
            => ScaleToInt(amount, caster == null ? null : caster.gameObject);
    }
}
