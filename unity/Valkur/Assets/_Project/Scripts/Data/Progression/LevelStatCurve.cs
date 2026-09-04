using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Per-level stat increments applied by
    /// <see cref="Valkur.Gameplay.PlayerProgression"/>, which rebuilds the Level stat
    /// layer from the CURRENT level rather than adding one level's delta at a time.
    ///
    /// Two modes (same precedence as <see cref="XpCurveDefinition"/>):
    ///   1. Linear — <see cref="hpPerLevel"/> + <see cref="manaPerLevel"/>.
    ///   2. Curve  — <see cref="hpCurve"/> / <see cref="manaCurve"/> evaluated
    ///      at the new level (returns the delta to add). When the curve is
    ///      empty (length 0) the linear fields take over.
    ///
    /// All deltas are clamped to ≥ 0 so a stray negative key on the curve
    /// can never debuff the player on level-up.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelStatCurve", menuName = "Valkur/Data/Level Stat Curve")]
    public class LevelStatCurve : ScriptableObject
    {
        [Header("Linear (default)")]
        [Tooltip("MaxHp added to the entity on each level-up.")]
        [Min(0)] public int hpPerLevel = 10;

        [Tooltip("MaxMana added to the entity on each level-up.")]
        [Min(0)] public int manaPerLevel = 5;

        [Header("Curve override (optional)")]
        [Tooltip("Per-level HP delta as a curve. When length > 0, evaluated at " +
                 "the new level instead of using hpPerLevel.")]
        public AnimationCurve hpCurve = new AnimationCurve();

        [Tooltip("Per-level Mana delta as a curve. When length > 0, evaluated at " +
                 "the new level instead of using manaPerLevel.")]
        public AnimationCurve manaCurve = new AnimationCurve();

        public int HpDelta(int newLevel)
        {
            if (hpCurve != null && hpCurve.length > 0)
                return Mathf.Max(0, Mathf.RoundToInt(hpCurve.Evaluate(newLevel)));
            return Mathf.Max(0, hpPerLevel);
        }

        public int ManaDelta(int newLevel)
        {
            if (manaCurve != null && manaCurve.length > 0)
                return Mathf.Max(0, Mathf.RoundToInt(manaCurve.Evaluate(newLevel)));
            return Mathf.Max(0, manaPerLevel);
        }

        [Header("Other stats per level")]
        [Tooltip("Modifiers granted for EACH level earned beyond the first. HP and mana " +
                 "have their own fields above for historical reasons; anything else — a " +
                 "point of melee damage every level, a little defense — is expressed here. " +
                 "Values are multiplied by the number of levels earned, so one authored " +
                 "row describes the whole curve.")]
        public StatModifier[] perLevelModifiers = System.Array.Empty<StatModifier>();

        /// <summary>
        /// <see cref="perLevelModifiers"/> scaled by how many levels the character has
        /// earned. Returns an empty array at level 1 so a fresh character contributes
        /// nothing rather than a row of zeroes.
        ///
        /// Cumulative and absolute on purpose: the caller rebuilds the whole Level layer
        /// from the CURRENT level rather than adding one level's worth at a time, which is
        /// the only form that can also answer "a save was just loaded at level 30", where
        /// there is no sequence of level-ups to replay.
        /// </summary>
        public StatModifier[] ModifiersForLevels(int levelsEarned)
        {
            if (levelsEarned <= 0 || perLevelModifiers == null || perLevelModifiers.Length == 0)
                return System.Array.Empty<StatModifier>();

            var result = new StatModifier[perLevelModifiers.Length];
            for (int i = 0; i < perLevelModifiers.Length; i++)
            {
                var m = perLevelModifiers[i];
                result[i] = new StatModifier(m.stat, m.op, m.value * levelsEarned);
            }
            return result;
        }
    }
}
