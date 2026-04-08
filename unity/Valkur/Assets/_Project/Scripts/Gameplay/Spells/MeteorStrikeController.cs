using UnityEngine;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spawns sequential meteor impacts in a circular area.
    /// Each meteor deals AoE damage at a random position within the area.
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

        public void Initialize(int count, float interval, float areaRadius, float impactRadius,
            int damage, LayerMask targetLayers, string impactPreset)
        {
            _remaining = count;
            _interval = interval;
            _timer = 0f;
            _areaRadius = areaRadius;
            _impactRadius = impactRadius;
            _damage = damage;
            _targetLayers = targetLayers;
            _impactPreset = impactPreset;
        }

        private void Update()
        {
            if (_remaining <= 0)
            {
                Destroy(gameObject, 0.5f);
                enabled = false;
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                SpawnMeteorStrike();
                _remaining--;
                _timer = _interval;
            }
        }

        private void SpawnMeteorStrike()
        {
            // Random position within area
            Vector2 offset = Random.insideUnitCircle * _areaRadius;
            Vector2 impactPos = (Vector2)transform.position + offset;

            // Damage
            var hits = Physics2D.OverlapCircleAll(impactPos, _impactRadius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                    health.TakeDamage(_damage);
            }

            // VFX
            if (VFXManager.Instance != null)
            {
                Color impactColor = new Color(1f, 0.3f, 0.05f, 0.8f);
                VFXManager.Instance.SpawnAreaIndicator((Vector3)impactPos, impactColor, _impactRadius, 0.5f);
                VFXManager.Instance.SpawnImpact((Vector3)impactPos, new Color(1f, 0.5f, 0.1f), 0.4f, _impactRadius);

                if (!string.IsNullOrEmpty(_impactPreset))
                    VFXManager.Instance.SpawnParticlePreset(_impactPreset, (Vector3)impactPos);
            }

            Debug.Log($"[SpellDebug] Meteor strike #{_remaining} at {impactPos}, dmg={_damage}");
        }
    }
}
