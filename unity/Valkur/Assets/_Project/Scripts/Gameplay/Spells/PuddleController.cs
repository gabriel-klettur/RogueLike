using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Controls a ground puddle: ticks damage on enemies within radius, optional burn application.
    /// </summary>
    public class PuddleController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private int _damagePerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private LayerMask _targetLayers;
        private string _element;
        private SpriteRenderer _sr;

        public void Initialize(float duration, float radius, int damagePerTick, float tickPeriod,
            LayerMask targetLayers, string element)
        {
            _remaining = duration;
            _radius = radius;
            _damagePerTick = damagePerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _targetLayers = targetLayers;
            _element = element;
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

            // Bubble/shimmer effect
            if (_sr != null)
            {
                float shimmer = 0.5f + Mathf.Sin(Time.time * 3f) * 0.1f;
                var c = _sr.color;
                c.a = shimmer * (_remaining < 1f ? _remaining : 1f);
                _sr.color = c;
            }
        }

        private void DamageTick()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                if (_damagePerTick > 0)
                    health.TakeDamage(_damagePerTick);

                // Apply burn if lava element
                if (_element == "lava" || _element == "fire")
                {
                    var statusMgr = hit.GetComponent<StatusEffectManager>();
                    if (statusMgr != null)
                        statusMgr.Apply(new BurnEffect(3f, 5));
                }
            }
        }
    }
}
