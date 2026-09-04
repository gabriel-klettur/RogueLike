using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Designer-tunable XP curve. Drives <see cref="Valkur.Gameplay.Experience"/>
    /// when assigned, replacing the inline <c>baseXp * level^exponent</c> defaults.
    ///
    /// Two ways to express the curve:
    ///   1. Formula mode — <see cref="baseXp"/> + <see cref="exponent"/>, used
    ///      whenever <see cref="explicitThresholds"/> is empty.
    ///   2. Lookup mode — <see cref="explicitThresholds"/> populated. For any
    ///      level ≤ Length the table wins; outside the table the formula
    ///      extrapolates. This lets designers fine-tune low levels without
    ///      writing a full table.
    ///
    /// Total XP required to BE at level <c>L</c> is the canonical semantics
    /// (matches <c>Experience.XpRequiredForLevel</c>): level 0 = 0 XP,
    /// level 1 = first threshold, etc.
    /// </summary>
    [CreateAssetMenu(fileName = "XpCurve", menuName = "Valkur/Data/XP Curve")]
    public class XpCurveDefinition : ScriptableObject
    {
        [Header("Formula")]
        [Tooltip("XP required for level N = baseXp * N^exponent.")]
        [Min(1)] public int baseXp = 100;
        [Min(0.1f)] public float exponent = 1.5f;

        [Header("Cap")]
        [Tooltip("Hard level cap. 0 = no cap. The entity stops levelling once " +
                 "it reaches this level even if more XP is added.")]
        [Min(0)] public int levelCap = 0;

        [Header("Override table")]
        [Tooltip("Optional explicit thresholds (total XP to BE at level [i+1]). " +
                 "When populated, indices 0..Length-1 win over the formula. " +
                 "Levels beyond the table fall back to the formula.")]
        public int[] explicitThresholds;

        /// <summary>
        /// Total XP required to BE at <paramref name="level"/>. Mirrors
        /// <c>Experience.XpRequiredForLevel</c> contract — level 0 = 0.
        /// </summary>
        public int XpRequiredForLevel(int level)
        {
            if (level <= 0) return 0;

            // Lookup table wins inside its range.
            if (explicitThresholds != null && level - 1 < explicitThresholds.Length)
                return Mathf.Max(0, explicitThresholds[level - 1]);

            return Mathf.RoundToInt(baseXp * Mathf.Pow(level, exponent));
        }

        public bool IsAtCap(int level) => levelCap > 0 && level >= levelCap;
    }
}
