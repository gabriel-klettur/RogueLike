using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Permanently grows MaxHp / MaxMana on every level-up using a
    /// <see cref="LevelStatCurve"/>. Sibling to
    /// <see cref="LevelUpRestoreSystem"/> (refills) and
    /// <see cref="LevelUpSkillPointSystem"/> (rewards SP); designers can
    /// enable any subset independently. Without a curve assigned the
    /// system is a silent no-op so it can be added to the bootstrap
    /// list ahead of designers wiring up the SO.
    ///
    /// Heals the granted delta into the current pool too (via
    /// <see cref="Health.IncreaseMaxHp"/> / <see cref="Mana.IncreaseMaxMana"/>),
    /// so the player feels the level-up immediately. <c>LevelUpRestoreSystem</c>
    /// then tops up the rest if it's wired.
    /// </summary>
    public class LevelUpStatScalingSystem : MonoBehaviour
    {
        [Header("Curve")]
        [Tooltip("Per-level stat increments. None assigned = system disabled.")]
        [SerializeField] private LevelStatCurve curve;

        public LevelStatCurve Curve => curve;

        public void SetCurve(LevelStatCurve c) => curve = c;

        private void OnEnable()  => GameEvents.OnLevelUp += OnLevelUp;
        private void OnDisable() => GameEvents.OnLevelUp -= OnLevelUp;

        private void OnLevelUp(GameObject entity, int newLevel)
        {
            if (curve == null || entity == null) return;

            int hpDelta   = curve.HpDelta(newLevel);
            int manaDelta = curve.ManaDelta(newLevel);

            if (hpDelta > 0)
            {
                var health = entity.GetComponent<Health>();
                if (health != null) health.IncreaseMaxHp(hpDelta);
            }
            if (manaDelta > 0)
            {
                var mana = entity.GetComponent<Mana>();
                if (mana != null) mana.IncreaseMaxMana(manaDelta);
            }
        }
    }
}
