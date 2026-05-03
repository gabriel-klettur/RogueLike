using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Per-second passive HP regen attached to an entity. Multiple skill
    /// nodes can stack regen via <see cref="AddRegen"/> — the cumulative
    /// rate is what each tick heals. Uses a simple float accumulator so
    /// fractional rates (e.g. 0.5 HP/s) round into 1-HP heals over two
    /// seconds.
    ///
    /// Wired by <see cref="AuraRegistry"/> when a skill with the
    /// "toughness" aura key is learned. Designers tuning balance use
    /// the SkillEffect.value field as the per-second amount.
    /// </summary>
    public class HpRegenAura : MonoBehaviour
    {
        private float _ratePerSecond;
        private float _accumulator;
        private Health _health;

        public float RatePerSecond => _ratePerSecond;

        public void AddRegen(float amountPerSec)
        {
            if (amountPerSec <= 0f) return;
            _ratePerSecond += amountPerSec;
        }

        private void Awake()
        {
            _health = GetComponent<Health>();
        }

        private void Update()
        {
            if (_ratePerSecond <= 0f) return;
            if (_health == null || _health.IsDead) return;
            if (_health.CurrentHp >= _health.MaxHp) return;

            _accumulator += _ratePerSecond * Time.deltaTime;
            int whole = Mathf.FloorToInt(_accumulator);
            if (whole > 0)
            {
                _health.Heal(whole);
                _accumulator -= whole;
            }
        }

        // Test seam — drives the accumulator without waiting for Update.
        public void TickForTest(float dt)
        {
            if (_health == null) _health = GetComponent<Health>();
            if (_ratePerSecond <= 0f || _health == null || _health.IsDead) return;
            if (_health.CurrentHp >= _health.MaxHp) return;
            _accumulator += _ratePerSecond * dt;
            int whole = Mathf.FloorToInt(_accumulator);
            if (whole > 0)
            {
                _health.Heal(whole);
                _accumulator -= whole;
            }
        }
    }
}
