using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Rolls critical strikes for an attacker.
    ///
    /// It exists as one static seam rather than as a branch inside each damage path so
    /// that "did that crit" has exactly one answer in the project. <c>critChance</c> and
    /// <c>critMultiplier</c> had been authored on 14 shipped items and read by nothing but
    /// the F7 editor since the item schema was written; a per-callsite roll would have
    /// given the two melee paths and the spell path three subtly different definitions of
    /// the same word.
    ///
    /// An attacker with no <see cref="PlayerStats"/> — every monster in the game — never
    /// crits, which keeps the whole mechanic on the player's side of combat where its
    /// tuning lives.
    /// </summary>
    public static class CritResolver
    {
        /// <summary>
        /// Rolls once and returns the damage to deal. <paramref name="wasCrit"/> is the
        /// half the feedback systems need: a crit that looks identical to a normal hit is
        /// a stat the player cannot see working, which is how <c>critChance</c> managed to
        /// sit in the item schema unnoticed for as long as it did.
        /// </summary>
        public static int Resolve(int baseDamage, GameObject attacker, out bool wasCrit)
        {
            wasCrit = false;
            if (baseDamage <= 0 || attacker == null) return baseDamage;

            var stats = attacker.GetComponent<PlayerStats>();
            if (stats == null) return baseDamage;

            float chance = stats.Get(StatKind.CritChance);
            if (chance <= 0f) return baseDamage;

            if (Random.value >= chance) return baseDamage;

            wasCrit = true;
            float multiplier = Mathf.Max(1f, stats.Get(StatKind.CritMultiplier));
            return Mathf.Max(baseDamage + 1, Mathf.RoundToInt(baseDamage * multiplier));
        }

        public static int Resolve(int baseDamage, GameObject attacker)
            => Resolve(baseDamage, attacker, out _);
    }
}
