using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Generic health component for any damageable entity.
    /// Maps to Python's hp/max_hp in entity stats.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] private int maxHp = 100;
        [SerializeField] private int currentHp;
        private bool _invincible;
        private PlayerSpiritState _spiritState;

        public int MaxHp => maxHp;
        public int MaxHealth => maxHp;
        public int CurrentHp => currentHp;
        public bool IsDead => currentHp <= 0;
        public bool IsInvincible => _invincible;
        public float NormalizedHp => maxHp > 0 ? (float)currentHp / maxHp : 0f;

        public event Action<int, int> OnHpChanged;
        public event Action OnDeath;
        public event Action<int> OnDamaged;

        private void Awake()
        {
            currentHp = maxHp;
        }

        public void Initialize(int max)
        {
            maxHp = max;
            currentHp = max;
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        /// <summary>
        /// Overload that lets callers set the current HP independently of the
        /// max — used by save/load to restore a damaged pool without going
        /// through <see cref="TakeDamage"/>. Going through TakeDamage would
        /// fire <see cref="OnDamaged"/> + <c>GameEvents.FireEntityDamaged</c>,
        /// which the combat audio + feedback systems treat as a real hit and
        /// play the damage SFX / hit-flash on game boot — the canonical
        /// "player loses HP and you hear the hurt sound the instant the run
        /// starts" bug. This path only fires <see cref="OnHpChanged"/> so the
        /// HUD updates without faking a damage event.
        /// </summary>
        public void Initialize(int max, int current)
        {
            maxHp = max;
            currentHp = Mathf.Clamp(current, 0, max);
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        public void TakeDamage(int amount)
        {
            if (IsDead || amount <= 0 || _invincible) return;

            // Spirit-form players are intangible: they have IsDead==false because
            // we don't actually keep them at HP=0 (we re-init HP on revive), but
            // until then the controller sets a flag we honour here.
            if (IsPlayerSpirit()) return;

            currentHp = Mathf.Max(0, currentHp - amount);
            OnDamaged?.Invoke(amount);
            OnHpChanged?.Invoke(currentHp, maxHp);

            GameEvents.FireEntityDamaged(gameObject, null, amount);
            if (gameObject.CompareTag("Player"))
                GameEvents.FirePlayerDamaged(amount, currentHp, maxHp);

            if (currentHp <= 0)
            {
                OnDeath?.Invoke();
                GameEvents.FireEntityDied(gameObject, null);
                if (gameObject.CompareTag("Player"))
                    GameEvents.FirePlayerDied();
            }
        }

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;

            currentHp = Mathf.Min(maxHp, currentHp + amount);
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        /// <summary>
        /// Permanently increase the max HP cap and grant the matching amount
        /// of current HP. Used by skill-tree stat boosts and item upgrades
        /// that shouldn't simultaneously heal the entity to full (which is
        /// what <see cref="Initialize"/> would do). Negative deltas are
        /// rejected to keep this call site distinct from a debuff path.
        /// </summary>
        public void IncreaseMaxHp(int delta)
        {
            if (delta <= 0) return;
            maxHp += delta;
            currentHp += delta;
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        public void SetInvincible(bool invincible)
        {
            _invincible = invincible;
        }

        private bool IsPlayerSpirit()
        {
            // Lazy lookup. We can't cache "checked-and-missing" because EntitySetup
            // adds PlayerSpiritState AFTER Health.Awake on the player prefab, so a
            // sticky-null cache would freeze the answer at false for the life of
            // the run. Re-querying GetComponent until we find one is cheap (this
            // only runs on damage events, never per-frame).
            if (_spiritState == null) _spiritState = GetComponent<PlayerSpiritState>();
            return _spiritState != null && _spiritState.IsSpirit;
        }
    }
}
