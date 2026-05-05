using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Per-level stat increments applied by
    /// <see cref="Valkur.Gameplay.LevelUpStatScalingSystem"/> on every level-up.
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
    }
}
