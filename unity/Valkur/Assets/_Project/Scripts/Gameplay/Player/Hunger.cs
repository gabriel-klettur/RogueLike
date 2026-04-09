using UnityEngine;

namespace Valkur.Gameplay.Player
{
    /// <summary>
    /// Hunger resource component for the player.
    /// Mirrors Python's HungerComponent (current/max int pair).
    /// Decreases over time; replenished by consuming food items.
    /// At 0 hunger, health degrades slowly.
    /// </summary>
    public class Hunger : MonoBehaviour
    {
        [SerializeField, Tooltip("Maximum hunger points.")]
        private int maxHunger = 100;

        [SerializeField, Tooltip("Current hunger points.")]
        private int currentHunger = 100;

        [SerializeField, Tooltip("Hunger decay rate per second.")]
        private float decayRate = 0.2f;

        [SerializeField, Tooltip("Health damage per second when starving (hunger == 0).")]
        private float starveDps = 1f;

        private float _decayAccumulator;
        private Health _health;

        public int Current => currentHunger;
        public int Max => maxHunger;
        public float Normalized => maxHunger > 0 ? (float)currentHunger / maxHunger : 0f;
        public bool IsStarving => currentHunger <= 0;

        public void Initialize(int max)
        {
            maxHunger = max;
            currentHunger = max;
        }

        /// <summary>Feed: restore hunger points (clamped to max).</summary>
        public void Feed(int amount)
        {
            if (amount <= 0) return;
            currentHunger = Mathf.Min(currentHunger + amount, maxHunger);
        }

        private void Awake()
        {
            _health = GetComponent<Health>();
        }

        private void Update()
        {
            // Decay hunger over time
            _decayAccumulator += decayRate * Time.deltaTime;
            if (_decayAccumulator >= 1f)
            {
                int ticks = Mathf.FloorToInt(_decayAccumulator);
                currentHunger = Mathf.Max(currentHunger - ticks, 0);
                _decayAccumulator -= ticks;
            }

            // Starving — damage health
            if (IsStarving && _health != null && _health.CurrentHp > 0)
            {
                _health.TakeDamage(Mathf.RoundToInt(starveDps * Time.deltaTime));
            }
        }
    }
}
