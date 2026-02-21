using System;
using UnityEngine;

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

        public int MaxHp => maxHp;
        public int CurrentHp => currentHp;
        public bool IsDead => currentHp <= 0;
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

        public void TakeDamage(int amount)
        {
            if (IsDead || amount <= 0) return;

            currentHp = Mathf.Max(0, currentHp - amount);
            OnDamaged?.Invoke(amount);
            OnHpChanged?.Invoke(currentHp, maxHp);

            if (currentHp <= 0)
            {
                OnDeath?.Invoke();
            }
        }

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;

            currentHp = Mathf.Min(maxHp, currentHp + amount);
            OnHpChanged?.Invoke(currentHp, maxHp);
        }
    }
}
