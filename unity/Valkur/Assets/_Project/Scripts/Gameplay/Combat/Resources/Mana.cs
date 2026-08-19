using System;
using UnityEngine;
using Valkur.Core;

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

        /// <summary>
        /// True while passive regen is actively ticking — i.e. the pool is below
        /// max AND we've waited out the post-cast <see cref="regenDelay"/> grace
        /// window. Drives visual feedback (e.g. <see cref="ManaRegenAura"/>) so
        /// effects only show during real recovery, not during the brief lull
        /// right after a spell consumes mana.
        /// </summary>
        public bool IsRegenerating
            => _currentMana < maxMana && Time.time - _lastConsumeTime >= regenDelay;

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
        /// Overload that lets save/load restore a partial mana pool without
        /// going through <see cref="TryConsume"/> — TryConsume fires
        /// <see cref="OnManaConsumed"/>, which any future spell-cost SFX or
        /// VFX subscriber would treat as a real consumption (the same boot-
        /// time false-event pattern as <c>Health.Initialize(max, current)</c>).
        /// Only fires <see cref="OnManaChanged"/> so the HUD bar updates.
        /// </summary>
        public void Initialize(int max, int current, float regen = 2f)
        {
            maxMana = max;
            _currentMana = Mathf.Clamp(current, 0, max);
            regenPerSecond = regen;
            _lastConsumeTime = 0f;
            _regenAccumulator = 0f;
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
            if (ActiveSpellsEditorSuppressesPlayerManaConsumption()) return true;
            if (_currentMana < amount) return false;

            _currentMana -= amount;
            _lastConsumeTime = Time.time;
            _regenAccumulator = 0f;

            OnManaConsumed?.Invoke(amount);
            OnManaChanged?.Invoke(_currentMana, maxMana);
            return true;
        }

        /// <summary>
        /// The F4 Spells Editor is an authoring surface: every mana charge made by the
        /// player while it is open is a preview charge and must be ignored. Keeping the
        /// decision here covers both the initial <see cref="Spells.SpellCaster"/> cost and
        /// secondary drains such as <see cref="Spells.LaserBeamController"/>'s per-second
        /// channel cost. The manager is queried on every attempt, so closing F4 restores
        /// normal consumption immediately without a stored suppression flag.
        /// </summary>
        private bool ActiveSpellsEditorSuppressesPlayerManaConsumption()
        {
            if (GetComponent<PlayerController>() == null) return false;
            if (!GameEditorManager.HasInstance) return false;

            var active = GameEditorManager.Instance.ActiveEditor;
            var chooser = active as IChoosesPrimaryCastSpell;
            return active != null
                && active.IsActive
                && chooser != null
                && chooser.PrimaryCastIgnoresManaCost;
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
