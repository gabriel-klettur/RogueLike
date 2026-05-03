using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Restores HP and MP whenever an entity levels up. The classic
    /// roguelike "you gained a level — fully healed" feedback that closes
    /// the XP loop with something tangible the player feels.
    ///
    /// Listens to <see cref="GameEvents.OnLevelUp"/>. Heals the entity that
    /// fired the event (so an NPC that ever levels up — boss enrage, etc.
    /// — also benefits without extra wiring). Both <see cref="Health"/> and
    /// <see cref="Mana"/> components are healed when present; missing
    /// components are silently skipped so a HP-only NPC doesn't error out
    /// on the missing Mana component.
    ///
    /// Heal fraction (0..1) defaults to 1.0 (full restore). Designers can
    /// tune via the inspector if they want partial restores per balancing.
    /// </summary>
    public class LevelUpRestoreSystem : MonoBehaviour
    {
        [Header("Restore policy")]
        [Tooltip("Fraction of max HP/MP restored on level-up. 1.0 = full heal.")]
        [SerializeField, Range(0f, 1f)] private float restoreFraction = 1f;

        private void OnEnable()
        {
            GameEvents.OnLevelUp += OnLevelUp;
        }

        private void OnDisable()
        {
            GameEvents.OnLevelUp -= OnLevelUp;
        }

        private void OnLevelUp(GameObject entity, int newLevel)
        {
            if (entity == null) return;

            var health = entity.GetComponent<Health>();
            if (health != null && health.MaxHealth > 0)
            {
                int healAmount = Mathf.RoundToInt(health.MaxHealth * restoreFraction);
                if (healAmount > 0) health.Heal(healAmount);
            }

            var mana = entity.GetComponent<Mana>();
            if (mana != null && mana.MaxMana > 0)
            {
                int restoreAmount = Mathf.RoundToInt(mana.MaxMana * restoreFraction);
                if (restoreAmount > 0) mana.Restore(restoreAmount);
            }
        }
    }
}
