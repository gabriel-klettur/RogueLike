using System;

namespace Valkur.Data
{
    /// <summary>
    /// Everything that is true of a <see cref="StatKind"/> regardless of who is
    /// carrying it: its player-facing name, its legal range, whether it reads as a
    /// whole number, and whether lower is better.
    ///
    /// It is a static table rather than a ScriptableObject because these are facts
    /// about the CODE, not tuning: <see cref="StatKind.CritChance"/> is a probability
    /// and cannot exceed 1 in any build of any class, and a designer being able to
    /// author it to 3 in an asset would produce a number no consumer could honour.
    /// Tuning lives in the class definition, the curves and the trees.
    ///
    /// The clamps are the last line of defence, applied AFTER every layer composes.
    /// Without them a stacked build reaches 100 % cooldown reduction and every spell
    /// becomes free and instant — the kind of failure that is invisible in each
    /// individual node and obvious only in the composition, which is the shape
    /// CLAUDE.md keeps recording (the spawner drift, the boomerang's borrowed
    /// <c>Projectile</c>, the ice wall's pixel units).
    /// </summary>
    public static class StatCatalog
    {
        /// <summary>Every value of <see cref="StatKind"/>, hoisted so callers iterating
        /// the vocabulary do not allocate an array per frame.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable array of enum values, built once from " +
            "Enum.GetValues and never mutated. Holds no Unity objects, so it cannot go stale " +
            "across a Play session. It also cannot be reset in a form the IL scanner accepts: " +
            "Array.Clear passes the field as an ARGUMENT, which the ratchet reads as no reset " +
            "at all — see CLAUDE.md on stsfld vs field.Clear().")]
        public static readonly StatKind[] All =
            (StatKind[])Enum.GetValues(typeof(StatKind));

        /// <summary>
        /// Neutral value for a stat with no base and no modifiers. Multiplicative stats
        /// rest at 1 and additive ones at 0 — getting this backwards makes a character
        /// with no equipment deal zero spell damage rather than normal spell damage.
        /// </summary>
        public static float NeutralBase(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.SpellPower:
                case StatKind.XpGain:
                    return 1f;
                case StatKind.CritMultiplier:
                    return 1.5f;
                default:
                    return 0f;
            }
        }

        public static float Min(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.MaxHp:                 return 1f;
                case StatKind.MaxMana:               return 0f;
                case StatKind.ManaRegen:             return 0f;
                // A character slowed to a standstill cannot escape the thing slowing
                // them, which is a soft lock rather than a debuff.
                case StatKind.MoveSpeed:             return 0.5f;
                case StatKind.MeleeDamage:           return 1f;
                case StatKind.MeleeRange:            return 0.2f;
                // Below this a swing outruns its own animation and the attack reads as
                // not happening. See CLAUDE.md on retiming an attack retiming its damage.
                case StatKind.MeleeCooldown:         return 0.1f;
                case StatKind.Defense:               return 0f;
                case StatKind.CritChance:            return 0f;
                case StatKind.CritMultiplier:        return 1f;
                case StatKind.SpellPower:            return 0.1f;
                case StatKind.SpellCooldownReduction:return 0f;
                case StatKind.ManaCostReduction:     return 0f;
                case StatKind.XpGain:                return 0f;
                default:                             return 0f;
            }
        }

        public static float Max(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.CritChance:             return 1f;
                case StatKind.CritMultiplier:         return 10f;
                // Not 1. At 100 % every spell is instant and free, which removes the
                // resource game the whole spell layer is built on.
                case StatKind.SpellCooldownReduction: return 0.75f;
                case StatKind.ManaCostReduction:      return 0.8f;
                case StatKind.MoveSpeed:              return 30f;
                default:                              return float.MaxValue;
            }
        }

        public static float Clamp(StatKind stat, float value)
        {
            float min = Min(stat);
            float max = Max(stat);
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>True when the stat is presented to the player as a whole number.</summary>
        public static bool IsInteger(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.MaxHp:
                case StatKind.MaxMana:
                case StatKind.MeleeDamage:
                case StatKind.Defense:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when a SMALLER number is the better one, so the UI can colour
        /// a reduction green instead of red.</summary>
        public static bool LowerIsBetter(StatKind stat) => stat == StatKind.MeleeCooldown;

        /// <summary>True when the stat reads naturally as a percentage on the sheet.</summary>
        public static bool IsPercentage(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.CritChance:
                case StatKind.SpellCooldownReduction:
                case StatKind.ManaCostReduction:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Best-effort parse of a legacy string stat name, from the Python build's
        /// <c>buffStat</c> column and anything else that predates the enum.
        ///
        /// It accepts the enum name, the display name, and the handful of Python spellings
        /// — including the two that name a RESOURCE with an attribute's name, which is the
        /// project's oldest naming debt: <c>maxStrength</c> is the hit-point pool and
        /// <c>maxIntelligence</c> is the mana pool.
        ///
        /// Returns false rather than guessing. A miss is warned about once at the call
        /// site, because a stat name nobody can resolve is content that silently does
        /// nothing, and that is the exact failure this whole layer exists to end.
        /// </summary>
        public static bool TryParse(string raw, out StatKind stat)
        {
            stat = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string key = raw.Trim().Replace("_", "").Replace(" ", "").ToLowerInvariant();

            switch (key)
            {
                case "maxhp":
                case "maxhealth":
                case "hp":
                case "health":
                case "strength":
                case "maxstrength":     stat = StatKind.MaxHp; return true;

                case "maxmana":
                case "mana":
                case "intelligence":
                case "maxintelligence": stat = StatKind.MaxMana; return true;

                case "manaregen":
                case "manaregeneration": stat = StatKind.ManaRegen; return true;

                case "movespeed":
                case "speed":            stat = StatKind.MoveSpeed; return true;

                case "meleedamage":
                case "damage":
                case "attack":           stat = StatKind.MeleeDamage; return true;

                case "meleerange":       stat = StatKind.MeleeRange; return true;

                case "meleecooldown":
                case "attackspeed":      stat = StatKind.MeleeCooldown; return true;

                case "defense":
                case "armor":            stat = StatKind.Defense; return true;

                case "critchance":       stat = StatKind.CritChance; return true;
                case "critmultiplier":
                case "critdamage":       stat = StatKind.CritMultiplier; return true;

                case "spellpower":       stat = StatKind.SpellPower; return true;
                case "cooldownreduction":
                case "spellcooldownreduction": stat = StatKind.SpellCooldownReduction; return true;
                case "manacostreduction": stat = StatKind.ManaCostReduction; return true;
                case "xpgain":            stat = StatKind.XpGain; return true;

                default: return false;
            }
        }

        public static string DisplayName(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.MaxHp:                  return "Max HP";
                case StatKind.MaxMana:                return "Max Mana";
                case StatKind.ManaRegen:              return "Mana Regen";
                case StatKind.MoveSpeed:              return "Move Speed";
                case StatKind.MeleeDamage:            return "Melee Damage";
                case StatKind.MeleeRange:             return "Melee Range";
                case StatKind.MeleeCooldown:          return "Attack Speed";
                case StatKind.Defense:                return "Defense";
                case StatKind.CritChance:             return "Crit Chance";
                case StatKind.CritMultiplier:         return "Crit Damage";
                case StatKind.SpellPower:             return "Spell Power";
                case StatKind.SpellCooldownReduction: return "Cooldown Reduction";
                case StatKind.ManaCostReduction:      return "Mana Cost Reduction";
                case StatKind.XpGain:                 return "XP Gain";
                default:                              return stat.ToString();
            }
        }

        /// <summary>Short player-facing sentence for the character sheet's tooltip.</summary>
        public static string Describe(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.MaxHp:                  return "Damage you can take before dying.";
                case StatKind.MaxMana:                return "Size of your spell pool.";
                case StatKind.ManaRegen:              return "Mana recovered every second out of combat.";
                case StatKind.MoveSpeed:              return "World units travelled per second.";
                case StatKind.MeleeDamage:            return "Damage of one melee swing.";
                case StatKind.MeleeRange:             return "How far a melee swing reaches.";
                case StatKind.MeleeCooldown:          return "Seconds between swings. Lower is faster.";
                case StatKind.Defense:                return "Damage subtracted from every blow you take.";
                case StatKind.CritChance:             return "Chance for a blow to critically strike.";
                case StatKind.CritMultiplier:         return "Damage multiplier on a critical strike.";
                case StatKind.SpellPower:             return "Multiplier on all spell damage.";
                case StatKind.SpellCooldownReduction: return "Fraction cut from every spell cooldown.";
                case StatKind.ManaCostReduction:      return "Fraction cut from every spell's mana cost.";
                case StatKind.XpGain:                 return "Multiplier on experience earned.";
                default:                              return string.Empty;
            }
        }
    }
}
