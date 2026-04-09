using UnityEngine;

namespace Valkur.Gameplay.Player
{
    /// <summary>
    /// Energy resource component for the player.
    /// Mirrors Python's EnergyComponent (current/max int pair).
    /// Consumed by sprinting, abilities, and restored by items/rest.
    /// </summary>
    public class Energy : MonoBehaviour
    {
        [SerializeField, Tooltip("Maximum energy.")]
        private int maxEnergy = 100;

        [SerializeField, Tooltip("Current energy.")]
        private int currentEnergy = 100;

        [SerializeField, Tooltip("Energy regen per second while idle.")]
        private float regenRate = 2f;

        private float _regenAccumulator;

        public int Current => currentEnergy;
        public int Max => maxEnergy;
        public float Normalized => maxEnergy > 0 ? (float)currentEnergy / maxEnergy : 0f;

        public void Initialize(int max)
        {
            maxEnergy = max;
            currentEnergy = max;
        }

        /// <summary>Spend energy. Returns true if enough was available.</summary>
        public bool Spend(int amount)
        {
            if (amount <= 0 || currentEnergy < amount) return false;
            currentEnergy -= amount;
            return true;
        }

        /// <summary>Restore energy (clamped to max).</summary>
        public void Restore(int amount)
        {
            if (amount <= 0) return;
            currentEnergy = Mathf.Min(currentEnergy + amount, maxEnergy);
        }

        private void Update()
        {
            if (currentEnergy >= maxEnergy) return;
            _regenAccumulator += regenRate * Time.deltaTime;
            if (_regenAccumulator >= 1f)
            {
                int ticks = Mathf.FloorToInt(_regenAccumulator);
                currentEnergy = Mathf.Min(currentEnergy + ticks, maxEnergy);
                _regenAccumulator -= ticks;
            }
        }
    }
}
