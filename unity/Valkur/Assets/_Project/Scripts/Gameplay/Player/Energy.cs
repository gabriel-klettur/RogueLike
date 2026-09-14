using System;
using UnityEngine;

namespace Valkur.Gameplay.Player
{
    /// <summary>
    /// The player's energy: what running spends and what food with <c>energy</c> restores.
    ///
    /// <para><b>IT WAS INERT FOR THE LIFE OF THE PROJECT.</b> This class shipped with a doc line
    /// saying it was "consumed by sprinting", and nothing added it to anybody — so
    /// <c>ItemConsumer</c>'s <c>GetComponent&lt;Energy&gt;()</c> answered null and every item card
    /// promising "+N de energía" did nothing. <c>EntitySetup</c> attaches it to the player now and
    /// <c>PlayerController.Locomotion</c> drives it.</para>
    ///
    /// <para><b>WHOLE NUMBERS OUTSIDE, A FRACTION INSIDE.</b> The public pair stays an int, which
    /// is what the item layer and the saves speak. Running drains at a RATE (nine a second, a
    /// fraction per frame), so the sub-unit remainder lives in <see cref="_fraction"/> — without
    /// it a 60 fps drain of 0.15 per frame would floor to nothing and running would be free.</para>
    ///
    /// <para><b>ONE REGENERATOR AT A TIME.</b> The built-in trickle is for anything that carries
    /// energy with no locomotion driving it. The player's regeneration depends on whether they
    /// are standing, walking or just stopped running, which only the locomotion partial knows, so
    /// it switches the trickle off through <see cref="SetExternallyRegulated"/>. Two regenerators
    /// adding to one pool is the SetInvincible shape: each correct alone.</para>
    /// </summary>
    public class Energy : MonoBehaviour
    {
        [SerializeField, Tooltip("Maximum energy.")]
        private int maxEnergy = 100;

        [SerializeField, Tooltip("Current energy.")]
        private int currentEnergy = 100;

        [SerializeField, Tooltip("Energy regen per second while nothing else regulates the pool.")]
        private float regenRate = 2f;

        private float _regenAccumulator;
        private float _fraction;
        private bool _externallyRegulated;

        /// <summary>Raised after any change, with the exact 0..1 fill.</summary>
        public event Action<float> Changed;

        public int Current => currentEnergy;
        public int Max => maxEnergy;

        /// <summary>The pool including the sub-unit remainder.</summary>
        public float Exact => Mathf.Clamp(currentEnergy + _fraction, 0f, maxEnergy);

        public float Normalized => maxEnergy > 0 ? Exact / maxEnergy : 0f;

        public bool IsFull => currentEnergy >= maxEnergy;
        public bool IsEmpty => Exact <= 0.0001f;

        public void Initialize(int max)
        {
            maxEnergy = Mathf.Max(0, max);
            currentEnergy = maxEnergy;
            _fraction = 0f;
            RaiseChanged();
        }

        /// <summary>
        /// Resizes the pool from the stat layer. IDEMPOTENT, because <c>PlayerStats</c> calls it on
        /// every recompute: a pool that was full stays full (a class seeded at 104 must not boot
        /// at 100/104), and one that was not keeps its value, clamped.
        /// </summary>
        public void SetMax(int max)
        {
            max = Mathf.Max(1, max);
            if (max == maxEnergy) return;
            bool wasFull = currentEnergy >= maxEnergy;
            maxEnergy = max;
            if (wasFull) { currentEnergy = maxEnergy; _fraction = 0f; }
            else if (currentEnergy > maxEnergy) { currentEnergy = maxEnergy; _fraction = 0f; }
            RaiseChanged();
        }

        /// <summary>True switches the built-in trickle off; the caller owns regeneration.</summary>
        public void SetExternallyRegulated(bool value) => _externallyRegulated = value;

        /// <summary>Spend energy. Returns true if enough was available.</summary>
        public bool Spend(int amount)
        {
            if (amount <= 0 || currentEnergy < amount) return false;
            currentEnergy -= amount;
            RaiseChanged();
            return true;
        }

        /// <summary>Restore energy (clamped to max).</summary>
        public void Restore(int amount)
        {
            if (amount <= 0) return;
            currentEnergy = Mathf.Min(currentEnergy + amount, maxEnergy);
            if (currentEnergy >= maxEnergy) _fraction = 0f;
            RaiseChanged();
        }

        /// <summary>Continuous spend. Clamps at zero and returns what was actually taken.</summary>
        public float Drain(float amount)
        {
            if (amount <= 0f) return 0f;
            float before = Exact;
            SetExact(before - amount);
            return before - Exact;
        }

        /// <summary>Continuous regeneration, clamped at the maximum.</summary>
        public void Regenerate(float amount)
        {
            if (amount <= 0f || IsFull) return;
            SetExact(Exact + amount);
        }

        private void SetExact(float value)
        {
            value = Mathf.Clamp(value, 0f, maxEnergy);
            // The epsilon absorbs float accumulation: sixty drains of 0.15 land on 90.99999, which
            // must read as 91 whole, not 90.
            int whole = Mathf.FloorToInt(value + 0.0005f);
            if (whole > maxEnergy) whole = maxEnergy;
            float frac = Mathf.Max(0f, value - whole);
            if (whole == currentEnergy && Mathf.Approximately(frac, _fraction)) return;
            currentEnergy = whole;
            _fraction = whole >= maxEnergy ? 0f : frac;
            RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke(Normalized);

        private void Update()
        {
            if (_externallyRegulated || currentEnergy >= maxEnergy) return;
            _regenAccumulator += regenRate * Time.deltaTime;
            if (_regenAccumulator >= 1f)
            {
                int ticks = Mathf.FloorToInt(_regenAccumulator);
                currentEnergy = Mathf.Min(currentEnergy + ticks, maxEnergy);
                _regenAccumulator -= ticks;
                RaiseChanged();
            }
        }
    }
}
