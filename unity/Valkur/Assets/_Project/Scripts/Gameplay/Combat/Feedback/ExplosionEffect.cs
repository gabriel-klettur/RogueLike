using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Self-destructing explosion effect.
    /// Maps Python's FireExplosionModel + ExplosionSystem (explosions_models.py / explosion_system.py).
    ///
    /// Usage (static factory):
    ///   ExplosionEffect.Spawn(pos, radius:2f, damage:15, instigator:gameObject);
    ///
    /// The prefab is NOT required — a lightweight runtime GameObject is created.
    /// For visual polish, an optional particle preset ID can trigger VFXManager.SpawnParticlePreset.
    /// </summary>
    public class ExplosionEffect : MonoBehaviour
    {
        // ── Configuration (set via Initialize) ───────────────────────

        private float      _radius;
        private float      _damage;
        private LayerMask  _targetLayers;
        private Color      _color;
        private GameObject _instigator;
        private string     _particlePreset;

        // ── State ─────────────────────────────────────────────────────

        private bool _detonated;

        // ── Static Factory ────────────────────────────────────────────

        /// <summary>
        /// Spawn an explosion at the given world position.
        /// </summary>
        /// <param name="position">World-space center.</param>
        /// <param name="radius">Blast radius in world units.</param>
        /// <param name="damage">Max damage at center (falls off to 0 at edge).</param>
        /// <param name="instigator">Who caused the explosion (used for kill attribution).</param>
        /// <param name="targetLayers">Layers that can be hit. Defaults to NPC(9)+Player(8) mask.</param>
        /// <param name="color">VFX color tint.</param>
        /// <param name="particlePreset">Optional particle preset ID for VFXManager.</param>
        public static ExplosionEffect Spawn(
            Vector3    position,
            float      radius        = 2f,
            float      damage        = 15f,
            GameObject instigator    = null,
            LayerMask  targetLayers  = default,
            Color?     color         = null,
            string     particlePreset = "fire_explosion")
        {
            if (targetLayers == default)
                targetLayers = (1 << 8) | (1 << 9); // Player + NPC

            var go  = new GameObject("ExplosionEffect");
            go.transform.position = position;

            var fx = go.AddComponent<ExplosionEffect>();
            fx.Initialize(radius, damage, instigator, targetLayers,
                          color ?? new Color(1f, 0.55f, 0.1f, 1f), particlePreset);
            return fx;
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Initialize(
            float radius, float damage, GameObject instigator,
            LayerMask targetLayers, Color color, string particlePreset)
        {
            _radius         = radius;
            _damage         = damage;
            _instigator     = instigator;
            _targetLayers   = targetLayers;
            _color          = color;
            _particlePreset = particlePreset;
        }

        private void Start()
        {
            Detonate();
        }

        // ── Core Logic ────────────────────────────────────────────────

        private void Detonate()
        {
            if (_detonated) return;
            _detonated = true;

            SpawnVFX();
            DealAreaDamage();
            Destroy(gameObject);
        }

        private void SpawnVFX()
        {
            if (!VFXManager.HasInstance) return;

            // Area ring flash
            VFXManager.Instance.SpawnAreaIndicator(transform.position, _color, _radius, 0.4f);
            // Particle burst (fire, electric, etc. depending on preset)
            if (!string.IsNullOrEmpty(_particlePreset))
                VFXManager.Instance.SpawnParticlePreset(_particlePreset, transform.position, 0.6f, _radius * 0.5f);
        }

        private void DealAreaDamage()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayers);
            if (hits.Length == 0) return;

            // Track already-hit objects to avoid double-damage from compound colliders
            var seen = new HashSet<GameObject>();

            foreach (var col in hits)
            {
                GameObject target = col.gameObject;
                if (!seen.Add(target)) continue;
                if (target == _instigator) continue;

                // Distance falloff: damage * (1 - dist/radius), clamped to 1 minimum
                float dist    = Vector2.Distance(transform.position, col.transform.position);
                float falloff = 1f - Mathf.Clamp01(dist / _radius);
                int   dealt   = Mathf.Max(1, Mathf.RoundToInt(_damage * falloff));

                var health = target.GetComponent<Health>();
                if (health == null)
                    health = target.GetComponentInParent<Health>();

                if (health != null && !health.IsDead)
                {
                    // TakeDamage raises FireEntityDamaged itself, now with the instigator.
                    // Raising it again here made every explosion report each victim twice.
                    health.TakeDamage(dealt, _instigator);
                    GameEvents.FireHitDealt(_instigator, target, dealt);
                    VFXManager.Instance?.SpawnImpact(col.transform.position, _color, 0.2f, 0.5f);
                }
            }
        }
    }
}
