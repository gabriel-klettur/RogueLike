using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spawns sequential meteors falling from the sky in a circular area. Each meteor
    /// uses <see cref="MeteorMissileFX"/> for the descent and <see cref="ElementalImpactFX"/>
    /// for the landing burst. Damage resolves on impact, not at spawn time.
    /// </summary>
    public class MeteorStrikeController : MonoBehaviour
    {
        private int _remaining;
        private float _interval;
        private float _timer;
        private float _areaRadius;
        private float _impactRadius;
        private int _damage;
        private LayerMask _targetLayers;
        private string _impactPreset;
        private GameObject _caster;
        private SpellElement? _element;
        private StatusApplication[] _statusApplications;

        public void Initialize(int count, float interval, float areaRadius, float impactRadius,
            int damage, LayerMask targetLayers, string impactPreset, GameObject caster = null,
            SpellElement? element = null, StatusApplication[] statusApplications = null)
        {
            _remaining = count;
            _interval = interval;
            _timer = 0f;
            _areaRadius = areaRadius;
            _impactRadius = impactRadius;
            _damage = damage;
            _targetLayers = targetLayers;
            _impactPreset = impactPreset;
            _caster = caster;
            _element = element;
            _statusApplications = statusApplications;
        }

        private void Update()
        {
            if (_remaining <= 0)
            {
                Destroy(gameObject, 1.5f);
                enabled = false;
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                LaunchMeteor();
                _remaining--;
                _timer = _interval;
            }
        }

        private void LaunchMeteor()
        {
            Vector2 offset = Random.insideUnitCircle * _areaRadius;
            Vector2 impactPos = (Vector2)transform.position + offset;

            // Falling missile resolves damage + extra preset on landing
            MeteorMissileFX.Spawn(impactPos, OnMeteorLanded);

        }

        private void OnMeteorLanded(Vector3 worldImpact)
        {
            var hits = Physics2D.OverlapCircleAll(worldImpact, _impactRadius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponentInParent<Health>();
                if (health != null && !health.IsDead)
                {
                    // A meteor impact is a discrete, attributable event (not a periodic
                    // tick), so it respects the post-hit grace window like any other hit.
                    health.TakeDamage(_damage, _caster, _element);
                    StatusApplicationFactory.ApplyAll(_statusApplications, health.gameObject, _caster);
                }
            }

            if (VFXManager.Instance != null)
            {
                VFXManager.Instance.SpawnAreaIndicator(worldImpact,
                    new Color(1f, 0.3f, 0.05f, 0.7f), _impactRadius, 0.6f);
                if (!string.IsNullOrEmpty(_impactPreset))
                    VFXManager.Instance.SpawnParticlePreset(_impactPreset, worldImpact);
            }
        }
    }
}
