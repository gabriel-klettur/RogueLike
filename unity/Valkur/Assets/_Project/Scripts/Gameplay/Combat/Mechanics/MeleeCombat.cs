using System;
using UnityEngine;
using Valkur.Core;
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
                        health.TakeDamage(damage, gameObject);
                        hitCount++;

                        // Apply knockback via CombatFeedback
                        var feedback = hit.GetComponent<Combat.CombatFeedback>();
                        if (feedback != null)
                            feedback.ApplyKnockback(origin);

                        OnHitTarget?.Invoke(hit.gameObject, damage);
                        GameEvents.FireHitDealt(gameObject, hit.gameObject, damage);
                    }
                }
            }

            if (hitCount > 0)
                Debug.Log($"[MeleeCombat] {gameObject.name} hit {hitCount} target(s) for {damage} damage");
        }

        /// <summary>
        /// The same crescent every slash spell draws, sized from this entity's own reach and
        /// arc.
        ///
        /// This used to call VFXManager.SpawnSlashArc, which despite its name discarded both
        /// the direction and the arc and drew a hard-edged filled circle of diameter 2x range
        /// at 80% opacity — a coloured ball on the ground, on the Entities sorting layer,
        /// wherever a monster swung. The arc is the whole point of a melee attack: it is what
        /// tells the player which side of them is dangerous.
        /// </summary>
        private void SpawnSlashVFX(Vector2 direction)
        {
            if (!showSlashVfx) return;

            Vector2 origin = transform.position;
            Spells.SlashAttack.SpawnVisual(transform, origin, direction.normalized,
                                           range, arcDegrees, slashVfxColor);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
