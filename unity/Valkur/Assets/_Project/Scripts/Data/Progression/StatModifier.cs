using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One authored change to one stat. This is the ONLY currency in which a skill
    /// node, a grimoire node, a piece of equipment or a potion may express what it
    /// does to a character — which is what makes those four systems interchangeable
    /// from the stat store's point of view, and what stops each of them growing its
    /// own private effect vocabulary the way the string-keyed first attempt did.
    ///
    /// It is a struct with three plain fields on purpose: a hundred of these serialize
    /// inline inside their owning asset instead of becoming a hundred sub-assets
    /// tracked by GUID. The same reasoning <see cref="SkillNode"/> records for keeping
    /// its effects flat.
    /// </summary>
    [Serializable]
    public struct StatModifier
    {
        [Tooltip("Which number this changes.")]
        public StatKind stat;

        [Tooltip("How it combines with the other modifiers on the same stat. " +
                 "Flat adds before percentages; PercentAdd pools with every other " +
                 "PercentAdd; PercentMult is its own independent factor.")]
        public StatOp op;

        [Tooltip("Magnitude. For Flat this is the raw amount (+12 hp). For the two " +
                 "percent ops it is a FRACTION, not a percentage: 0.05 means +5 %.")]
        public float value;

        public StatModifier(StatKind stat, StatOp op, float value)
        {
            this.stat = stat;
            this.op = op;
            this.value = value;
        }

        public static StatModifier Flat(StatKind stat, float value)
            => new StatModifier(stat, StatOp.Flat, value);

        public static StatModifier Percent(StatKind stat, float fraction)
            => new StatModifier(stat, StatOp.PercentAdd, fraction);

        public static StatModifier Multiplicative(StatKind stat, float fraction)
            => new StatModifier(stat, StatOp.PercentMult, fraction);

        /// <summary>
        /// Player-facing text for one modifier, e.g. "+12 Max HP" or "+5% Melee Damage".
        /// Lives here rather than in the HUD because the skill tree, the grimoire, the
        /// character sheet and the item tooltip all have to say the same thing about the
        /// same modifier — four independent formatters is four chances to disagree.
        /// </summary>
        public string Describe()
        {
            string name = StatCatalog.DisplayName(stat);
            bool lowerIsBetter = StatCatalog.LowerIsBetter(stat);

            if (op == StatOp.Flat)
            {
                string sign = value >= 0f ? "+" : "";
                return $"{sign}{FormatAmount(value)} {name}";
            }

            float pct = value * 100f;
            string pctSign = pct >= 0f ? "+" : "";
            // A negative cooldown modifier is a BUFF, so say so rather than showing the
            // player a minus sign next to a good thing.
            if (lowerIsBetter && pct < 0f)
                return $"-{FormatAmount(-pct)}% {name}";

            return $"{pctSign}{FormatAmount(pct)}% {name}";
        }

        private static string FormatAmount(float v)
        {
            float rounded = Mathf.Round(v * 100f) / 100f;
            return Mathf.Approximately(rounded, Mathf.Round(rounded))
                ? Mathf.RoundToInt(rounded).ToString()
                : rounded.ToString("0.##");
        }

        public override string ToString() => Describe();
    }
}
