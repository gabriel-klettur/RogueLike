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
                case StatKind.MaxHp:                  return "Vida máxima";
                case StatKind.MaxMana:                return "Maná máximo";
                case StatKind.ManaRegen:              return "Regeneración de maná";
                case StatKind.MoveSpeed:              return "Velocidad";
                case StatKind.MeleeDamage:            return "Daño cuerpo a cuerpo";
                case StatKind.MeleeRange:             return "Alcance cuerpo a cuerpo";
                case StatKind.MeleeCooldown:          return "Velocidad de ataque";
                case StatKind.Defense:                return "Defensa";
                case StatKind.CritChance:             return "Probabilidad de crítico";
                case StatKind.CritMultiplier:         return "Daño crítico";
                case StatKind.SpellPower:             return "Poder mágico";
                case StatKind.SpellCooldownReduction: return "Reducción de recarga";
                case StatKind.ManaCostReduction:      return "Reducción de coste de maná";
                case StatKind.XpGain:                 return "Ganancia de experiencia";
                default:                              return stat.ToString();
            }
        }

        /// <summary>Short player-facing sentence for the character sheet's tooltip.</summary>
        public static string Describe(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.MaxHp:                  return "Daño que aguantas antes de morir.";
                case StatKind.MaxMana:                return "Tamaño de tu reserva de maná.";
                case StatKind.ManaRegen:              return "Maná que recuperas cada segundo fuera de combate.";
                case StatKind.MoveSpeed:              return "Distancia que recorres por segundo.";
                case StatKind.MeleeDamage:            return "Daño de un golpe cuerpo a cuerpo.";
                case StatKind.MeleeRange:             return "Hasta dónde llega un golpe cuerpo a cuerpo.";
                case StatKind.MeleeCooldown:          return "Segundos entre golpes. Menos es más rápido.";
                case StatKind.Defense:                return "Daño que se resta a cada golpe que recibes.";
                case StatKind.CritChance:             return "Probabilidad de que un golpe sea crítico.";
                case StatKind.CritMultiplier:         return "Multiplicador de daño de un golpe crítico.";
                case StatKind.SpellPower:             return "Multiplicador del daño de todos los hechizos.";
                case StatKind.SpellCooldownReduction: return "Fracción que se descuenta de cada recarga.";
                case StatKind.ManaCostReduction:      return "Fracción que se descuenta del coste de maná.";
                case StatKind.XpGain:                 return "Multiplicador de la experiencia ganada.";
                default:                              return string.Empty;
            }
        }
    }
}
