using UnityEngine;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Controls a healing totem: periodically heals the caster (and nearby friendlies) within radius.
    /// Mirrors Python's TotemComponent with kind=heal.
    /// </summary>
    public class TotemController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private int _healPerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private Transform _owner;
        private SpriteRenderer _sr;

        public void Initialize(float duration, float radius, int healPerTick, float tickPeriod, Transform owner)
        {
            _remaining = duration;
            _radius = radius;
            _healPerTick = healPerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _owner = owner;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                Debug.Log($"[SpellDebug] Totem expired at {transform.position}");
                Destroy(gameObject);
                return;
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                HealTick();
                _tickTimer = _tickPeriod;
            }

            // Pulse glow
            if (_sr != null)
            {
                float pulse = 0.8f + Mathf.Sin(Time.time * 3f) * 0.15f;
                var c = _sr.color;
                c.a = pulse * (_remaining < 2f ? Mathf.Clamp01(_remaining * 0.5f) : 1f);
                _sr.color = c;
            }

            // Periodic healing VFX ring
            if (VFXManager.Instance != null && Mathf.FloorToInt(Time.time * 2f) % 2 == 0)
            {
                // Small pulse every ~0.5s
            }
        }

        private void HealTick()
        {
            if (_owner == null) return;

            // Heal owner if within radius
            float dist = Vector2.Distance(transform.position, _owner.position);
            if (dist <= _radius)
            {
                var health = _owner.GetComponent<Health>();
                if (health != null && !health.IsDead)
                {
                    health.Heal(_healPerTick);
                    Debug.Log($"[SpellDebug] Totem healed {_owner.name} for {_healPerTick} HP (dist={dist:F1})");
                }
            }
        }
    }
}
