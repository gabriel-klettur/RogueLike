using UnityEngine;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Persistent flame zone controller: damages enemies within radius on a tick.
    /// </summary>
    public class ArcaneFlameController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private int _damagePerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private LayerMask _targetLayers;
        private SpriteRenderer _sr;

        public void Initialize(float duration, float radius, int damagePerTick, float tickPeriod, LayerMask targetLayers)
        {
            _remaining = duration;
            _radius = radius;
            _damagePerTick = damagePerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _targetLayers = targetLayers;
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
                DamageTick();
                _tickTimer = _tickPeriod;
            }

            // Flame flicker
            if (_sr != null)
            {
                float flicker = 0.35f + Mathf.PerlinNoise(Time.time * 8f, 0f) * 0.25f;
                var baseColor = new Color(0.6f, 0.2f, 0.9f, flicker);
                _sr.color = baseColor;

                float scaleFlicker = 1f + Mathf.Sin(Time.time * 5f) * 0.05f;
                transform.localScale = Vector3.one * (_radius * 0.4f) * scaleFlicker;
            }
        }

        private void DamageTick()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                    health.TakeDamage(_damagePerTick);
            }
        }
    }
}
