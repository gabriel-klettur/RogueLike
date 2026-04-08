using UnityEngine;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Controls a placed mine: arming phase → proximity detection → explosion.
    /// Mirrors Python's MineComponent lifecycle.
    /// </summary>
    public class MineController : MonoBehaviour
    {
        private float _armingTimer;
        private float _triggerRadius;
        private float _explosionRadius;
        private int _explosionDamage;
        private float _ttl;
        private LayerMask _targetLayers;
        private string _impactPreset;
        private bool _armed;
        private SpriteRenderer _sr;

        public void Initialize(float armingTime, float triggerRadius, float explosionRadius,
            int explosionDamage, float ttl, LayerMask targetLayers, string impactPreset)
        {
            _armingTimer = armingTime;
            _triggerRadius = triggerRadius;
            _explosionRadius = explosionRadius;
            _explosionDamage = explosionDamage;
            _ttl = ttl;
            _targetLayers = targetLayers;
            _impactPreset = impactPreset;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            _ttl -= Time.deltaTime;
            if (_ttl <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (!_armed)
            {
                _armingTimer -= Time.deltaTime;
                if (_armingTimer <= 0f)
                {
                    _armed = true;
                    // Visual feedback: armed
                    if (_sr != null) _sr.color = new Color(1f, 0.1f, 0.1f, 1f);
                    Debug.Log($"[SpellDebug] Mine armed at {transform.position}");
                }
                else
                {
                    // Blink during arming
                    if (_sr != null)
                    {
                        float alpha = Mathf.PingPong(Time.time * 4f, 1f) * 0.5f + 0.3f;
                        var c = _sr.color;
                        c.a = alpha;
                        _sr.color = c;
                    }
                }
                return;
            }

            // Proximity check
            var hits = Physics2D.OverlapCircleAll(transform.position, _triggerRadius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                {
                    Detonate();
                    return;
                }
            }
        }

        private void Detonate()
        {
            Debug.Log($"[SpellDebug] Mine detonated at {transform.position}, dmg={_explosionDamage}, radius={_explosionRadius:F1}");

            // Damage all in explosion radius
            var hits = Physics2D.OverlapCircleAll(transform.position, _explosionRadius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                    health.TakeDamage(_explosionDamage);
            }

            // VFX
            if (VFXManager.Instance != null)
            {
                if (!string.IsNullOrEmpty(_impactPreset))
                    VFXManager.Instance.SpawnParticlePreset(_impactPreset, transform.position);
                VFXManager.Instance.SpawnAreaIndicator(transform.position,
                    new Color(1f, 0.4f, 0.1f, 0.7f), _explosionRadius, 0.5f);
            }

            Destroy(gameObject);
        }
    }
}
