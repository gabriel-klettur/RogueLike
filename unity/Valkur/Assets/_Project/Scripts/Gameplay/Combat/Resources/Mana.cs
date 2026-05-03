using System;
using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Mana resource component for spell casting.
    /// Maps to Python's mana fields in player stats (mana_regen_per_second, manaCost on spells).
    /// 
    /// Provides current/max mana, passive regeneration, and consumption API.
    /// Fires events for UI binding (HUD mana bar).
    /// </summary>
    public class Mana : MonoBehaviour
    {
        [SerializeField] private int maxMana = 100;
        [SerializeField] private float regenPerSecond = 2f;
        [SerializeField] private float regenDelay = 1.5f;

        private int _currentMana;
        private float _lastConsumeTime;

        public int CurrentMana => _currentMana;
        public int MaxMana => maxMana;
        public float NormalizedMana => maxMana > 0 ? (float)_currentMana / maxMana : 0f;

        public event Action<int, int> OnManaChanged;
        public event Action<int> OnManaConsumed;

        public void Initialize(int max, float regen = 2f)
        {
            maxMana = max;
            _currentMana = max;
            regenPerSecond = regen;
            OnManaChanged?.Invoke(_currentMana, maxMana);
        }

        /// <summary>
        /// Permanently increase the max mana cap and grant the matching
        /// amount of current mana. Used by skill-tree stat boosts; mirrors
        /// <see cref="Health.IncreaseMaxHp"/>. Reset-and-refill is the job
        /// of <see cref="Initialize"/>.
        /// </summary>
        public void IncreaseMaxMana(int delta)
        {
            if (delta <= 0) return;
            maxMana += delta;
            _currentMana += delta;
            OnManaChanged?.Invoke(_currentMana, maxMana);
        }

        /// <summary>
        /// Adds permanent mana-regeneration bonus on top of the current
        /// rate. Stacks linearly across calls — wired by AuraRegistry's
        /// "manaflow" aura so multiple skill nodes can compound.
        /// </summary>
        public void AddRegenBonus(float amountPerSec)
        {
            if (amountPerSec <= 0f) return;
            regenPerSecond += amountPerSec;
        }

        public float RegenPerSecond => regenPerSecond;

        private void Awake()
        {
            _currentMana = maxMana;
        }

        private void Update()
        {
            if (_currentMana >= maxMana) return;
            if (Time.time - _lastConsumeTime < regenDelay) return;

            float regenAmount = regenPerSecond * Time.deltaTime;
            int regenInt = Mathf.FloorToInt(regenAmount * 100f);

            // Accumulate fractional regen
            _regenAccumulator += regenAmount;
            if (_regenAccumulator >= 1f)
            {
                int toRegen = Mathf.FloorToInt(_regenAccumulator);
                _regenAccumulator -= toRegen;
                _currentMana = Mathf.Min(_currentMana + toRegen, maxMana);
                OnManaChanged?.Invoke(_currentMana, maxMana);
            }
        }

        private float _regenAccumulator;

        /// <summary>
        /// Try to consume mana. Returns true if enough mana was available.
        /// </summary>
        public bool TryConsume(int amount)
        {
            if (amount <= 0) return true;
            if (_currentMana < amount) return false;

            _currentMana -= amount;
            _lastConsumeTime = Time.time;
            _regenAccumulator = 0f;

            OnManaConsumed?.Invoke(amount);
            OnManaChanged?.Invoke(_currentMana, maxMana);
            return true;
        }

        /// <summary>
        /// Restore mana (e.g., from potions).
        /// </summary>
        public void Restore(int amount)
        {
            if (amount <= 0) return;
            _currentMana = Mathf.Min(_currentMana + amount, maxMana);
            OnManaChanged?.Invoke(_currentMana, maxMana);
        }

        /// <summary>
        /// Check if enough mana is available without consuming.
        /// </summary>
        public bool HasMana(int amount)
        {
            return _currentMana >= amount;
        }
    }
}
