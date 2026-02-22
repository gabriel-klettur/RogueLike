using System;
using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Melee combat component for both player and NPCs.
    /// Maps to Python's melee_damage, melee_cooldown, melee_range stats.
    /// </summary>
    public class MeleeCombat : MonoBehaviour
    {
        [Header("Melee Stats")]
        [SerializeField] private int damage = 5;
        [SerializeField] private float cooldown = 1f;
        [SerializeField] private float range = 1f;
        [SerializeField] private float arcDegrees = 90f;

        [Header("VFX")]
        [SerializeField] private Color slashVfxColor = new Color(0.9f, 0.95f, 1f, 0.8f);
        [SerializeField] private bool showSlashVfx = true;

        [Header("Layers")]
        [SerializeField] private LayerMask targetLayers;

        private float _lastAttackTime = -999f;

        /// <summary>Fired when this entity hits a target. Args: (hitGameObject, damage)</summary>
        public event Action<GameObject, int> OnHitTarget;

        public bool CanAttack => Time.time >= _lastAttackTime + cooldown;
        public float CooldownRemaining => Mathf.Max(0f, (_lastAttackTime + cooldown) - Time.time);
        public float CooldownTotal => cooldown;
        public int Damage => damage;
        public float Range => range;
        public float ArcDegrees => arcDegrees;

        public void Initialize(int dmg, float cd, float rng)
        {
            damage = dmg;
            cooldown = cd;
            range = rng;
        }

        public void SetSlashVfxColor(Color color)
        {
            slashVfxColor = color;
            showSlashVfx = true;
        }

        public void SetTargetLayers(LayerMask layers)
        {
            targetLayers = layers;
        }

        public void TryAttack(Vector2 direction)
        {
            if (!CanAttack) return;

            _lastAttackTime = Time.time;
            PerformAttack(direction);
            SpawnSlashVFX(direction);
        }

        private void PerformAttack(Vector2 direction)
        {
            // Cast from entity center; the overlap circle covers the full attack range in front
            Vector2 origin = (Vector2)transform.position;
            Vector2 center = origin + direction.normalized * (range * 0.5f);
            var hits = Physics2D.OverlapCircleAll(center, range, targetLayers);

            int hitCount = 0;
            foreach (var hit in hits)
            {
                if (hit.gameObject == gameObject) continue;

                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                {
                    // Arc check: angle from attack direction to target direction
                    Vector2 toTarget = ((Vector2)hit.transform.position - origin).normalized;
                    float angle = Vector2.Angle(direction.normalized, toTarget);
                    if (angle <= arcDegrees * 0.5f)
                    {
                        health.TakeDamage(damage);
                        hitCount++;

                        // Apply knockback via CombatFeedback
                        var feedback = hit.GetComponent<Combat.CombatFeedback>();
                        if (feedback != null)
                            feedback.ApplyKnockback(origin);

                        OnHitTarget?.Invoke(hit.gameObject, damage);
                    }
                }
            }

            if (hitCount > 0)
                Debug.Log($"[MeleeCombat] {gameObject.name} hit {hitCount} target(s) for {damage} damage");
        }

        private void SpawnSlashVFX(Vector2 direction)
        {
            if (!showSlashVfx) return;
            if (VFXManager.Instance == null) return;

            Vector3 vfxPos = transform.position + (Vector3)(direction.normalized * (range * 0.5f));
            VFXManager.Instance.SpawnSlashArc(vfxPos, direction, slashVfxColor, arcDegrees, range, 0.2f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
