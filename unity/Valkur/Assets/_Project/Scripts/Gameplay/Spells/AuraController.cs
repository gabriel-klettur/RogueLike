using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Ticking healing area controller: heals the caster (and friendly units) within radius.
    /// </summary>
    public class AuraController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private int _healPerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private Transform _caster;
        private SpriteRenderer _sr;

        public void Initialize(float duration, float radius, int healPerTick, float tickPeriod, Transform caster)
        {
            _remaining = duration;
            _radius = radius;
            _healPerTick = healPerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _caster = caster;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                HealTick();
                _tickTimer = _tickPeriod;
            }

            // Pulse visual
            if (_sr != null)
            {
                float pulse = 0.25f + Mathf.Sin(Time.time * 4f) * 0.1f;
                var c = _sr.color;
                c.a = pulse;
                _sr.color = c;
            }
        }

        private void HealTick()
        {
            if (_caster == null) return;
            var health = _caster.GetComponent<Health>();
            if (health != null && !health.IsDead)
            {
                health.Heal(_healPerTick);
                Debug.Log($"[SpellDebug] Aura healed {_caster.name} for {_healPerTick} HP");
            }
        }
    }
}
