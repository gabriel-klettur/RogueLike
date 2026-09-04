using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;
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

        // One entity may present several colliders to the overlap query (body +
        // hurtbox + perception trigger). Hoisted and cleared per swing rather than
        // allocated, since a pack of monsters swings every frame.
        private static readonly System.Collections.Generic.HashSet<int> _damagedThisSwing =
            new System.Collections.Generic.HashSet<int>();

        // Domain Reload is OFF, so the buffer would carry the last session's instance
        // IDs into the next Play. PerformAttack clears it before every swing, but a
        // shared static that survives a Play boundary is exactly what
        // DomainReloadStaticResetTests exists to refuse.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => _damagedThisSwing.Clear();

        /// <summary>Fired when this entity hits a target. Args: (hitGameObject, damage)</summary>
        public event Action<GameObject, int> OnHitTarget;

        /// <summary>
        /// Cooldown multiplier contributed by the LAST swing's attack variant. A heavy move
        /// that hits harder should also leave a longer opening, and the opening only makes
        /// sense measured from the move that created it — so this is stamped by
        /// <see cref="TryAttack"/> and read by every cooldown query until the next swing.
        /// 1 for every entity with no variants, which is all but one shipped monster.
        /// </summary>
        private float _cooldownScale = 1f;

        private float ScaledCooldown => cooldown * _cooldownScale;

        // Stamped by the last swing so feedback systems can ask after the fact rather than
        // having the flag threaded through every damage callback.
        private bool _lastSwingWasCrit;

        /// <summary>True when the most recent swing was a critical strike.</summary>
        public bool LastSwingWasCrit => _lastSwingWasCrit;

        public bool CanAttack => Time.time >= _lastAttackTime + ScaledCooldown;
        public float CooldownRemaining => Mathf.Max(0f, (_lastAttackTime + ScaledCooldown) - Time.time);
        public float CooldownTotal => ScaledCooldown;
        public int Damage => damage;
        public float Range => range;
        public float ArcDegrees => arcDegrees;

        public void Initialize(int dmg, float cd, float rng)
        {
            damage = dmg;
            cooldown = cd;
            range = rng;
        }

        // The three absolute setters below are what PlayerStats pushes through on every
        // recompute. They exist separately from Initialize because a recompute must be able
        // to move ONE of the three without asserting anything about the other two — the
        // sword changes damage, the boots change nothing here, and a caller forced to
        // re-supply all three would have to know the current value of the ones it does not
        // own, which is exactly the "read the total, change it, write it back" pattern the
        // layered store exists to delete.
        //
        // Monsters never call these: their numbers come from EntityStats through Initialize
        // and do not change once the entity is configured.

        public void SetDamage(int value) => damage = Mathf.Max(1, value);

        public void SetRange(float value) => range = Mathf.Max(0.01f, value);

        public void SetCooldown(float value) => cooldown = Mathf.Max(0.01f, value);

        public void SetSlashVfxColor(Color color)
        {
            slashVfxColor = color;
            showSlashVfx = true;
        }

        public void SetTargetLayers(LayerMask layers)
        {
            targetLayers = layers;
        }

        /// <summary>
        /// Swing. The three multipliers come from the attack variant the FSM picked, so one
        /// entity's moveset can differ move to move instead of being five animations over
        /// one identical hit. All default to 1, which is exactly the old behaviour.
        /// </summary>
        /// <param name="damageMultiplier">Scales this swing's damage. Floors at 1 point.</param>
        /// <param name="rangeMultiplier">Scales reach AND the drawn arc, together.</param>
        /// <param name="cooldownMultiplier">Scales the opening this swing leaves behind.</param>
        public void TryAttack(Vector2 direction,
                              float damageMultiplier = 1f,
                              float rangeMultiplier = 1f,
                              float cooldownMultiplier = 1f)
        {
            if (!CanAttack) return;

            _cooldownScale = Mathf.Max(0.01f, cooldownMultiplier);
            _lastAttackTime = Time.time;

            int swingDamage = Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Max(0f, damageMultiplier)));

            // Rolled once per SWING, not once per target: a cleave that crits on the first
            // enemy and not the second reads as inconsistent damage rather than as a crit,
            // and the number the player sees is the swing's, not each victim's.
            swingDamage = CritResolver.Resolve(swingDamage, gameObject, out bool wasCrit);
            _lastSwingWasCrit = wasCrit;
            float swingRange = Mathf.Max(0.01f, range * Mathf.Max(0.01f, rangeMultiplier));

            PerformAttack(direction, swingDamage, swingRange);
            SpawnSlashVFX(direction, swingRange);
        }

        private void PerformAttack(Vector2 direction, int swingDamage, float swingRange)
        {
            // The damage query is centred on the entity with radius `swingRange` — exactly
            // the circle SpawnSlashVFX draws the crescent inside, and exactly what
            // OnDrawGizmosSelected shows. It used to be centred at
            // `origin + dir * range * 0.5` with radius `range`, so the furthest
            // damaged point was range * 1.5: you were hit a tile and a half outside
            // the visible arc, and three and a half tiles outside it on barbol_boss.
            Vector2 origin = (Vector2)transform.position;
            var hits = Physics2D.OverlapCircleAll(origin, swingRange, targetLayers);

            int hitCount = 0;
            _damagedThisSwing.Clear();
            foreach (var hit in hits)
            {
                if (hit.gameObject == gameObject) continue;

                // GetComponentInParent, not GetComponent: an entity may carry its
                // body collider on a child (SlashAttack.Damage already resolves it
                // this way). The self-check has to be repeated on the resolved owner
                // — a child hurtbox of our own would otherwise walk up to our Health.
                var health = hit.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;

                var victim = health.gameObject;
                if (victim == gameObject) continue;

                // One entity, one hit: resolving through the parent means a body
                // collider and a perception trigger on the same entity both land here.
                if (!_damagedThisSwing.Add(victim.GetInstanceID())) continue;

                // Arc check, measured against the entity we are actually damaging
                // rather than whichever of its colliders the query happened to return.
                Vector2 victimPos = victim.transform.position;
                Vector2 toTarget = (victimPos - origin).normalized;
                float angle = Vector2.Angle(direction.normalized, toTarget);
                if (angle > arcDegrees * 0.5f) continue;

                // A swing does not pass through world geometry. barbol_boss reaches
                // 7 units, which is most of a building — without this it hit players
                // standing on the far side of one.
                if (World.LineOfSight.IsBlocked(origin, victimPos)) continue;

                health.TakeDamage(swingDamage, gameObject);
                hitCount++;

                // Apply knockback via CombatFeedback
                var feedback = victim.GetComponent<Combat.CombatFeedback>();
                if (feedback != null)
                    feedback.ApplyKnockback(origin);

                OnHitTarget?.Invoke(victim, swingDamage);
                GameEvents.FireHitDealt(gameObject, victim, swingDamage);
            }

            // Destructible obstacles are not on any target layer — they sit on Building so
            // they can block — so the overlap query above can never return one. Normally the
            // registry is empty and this costs a Count check.
            if (DestructibleObstacleRegistry.Count > 0)
                hitCount += DestructibleObstacleRegistry.DamageInArc(
                    origin, swingRange, direction.normalized, arcDegrees, swingDamage, gameObject, null);

            // Harvest seams are reached the same way and for the same reason, but through a
            // registry of their own. They deliberately do NOT implement IDestructibleObstacle:
            // Projectile resolves that interface directly off the collider's parents, so a
            // seam that implemented it could be emptied by any stray fireball that clipped it.
            // See HarvestSwingRegistry.
            if (Valkur.Gameplay.World.HarvestSwingRegistry.Count > 0)
                hitCount += Valkur.Gameplay.World.HarvestSwingRegistry.WorkInArc(
                    origin, swingRange, direction.normalized, arcDegrees, swingDamage, gameObject, null);

            if (hitCount > 0)
                Valkur.Core.VerboseLog.Log(Valkur.Core.VerboseLog.Category.Combat,
                    () => $"[MeleeCombat] {gameObject.name} hit {hitCount} target(s) for {swingDamage} damage");
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
        private void SpawnSlashVFX(Vector2 direction, float swingRange)
        {
            if (!showSlashVfx) return;

            Vector2 origin = transform.position;
            Spells.SlashAttack.SpawnVisual(transform, origin, direction.normalized,
                                           swingRange, arcDegrees, slashVfxColor);
        }

        /// <summary>
        /// Draws the crescent this entity is ABOUT to swing, dimmed, at the start of the
        /// windup — the "this is going to hit you, here" tell.
        ///
        /// It reuses the very shape the real swing draws rather than inventing a separate
        /// marker, so the promise and the payoff cannot disagree about reach or direction:
        /// the same origin, the same arc, the same range the damage query will use. That
        /// mattered enough to fix once already — the damage circle used to reach 1.5x what
        /// the arc showed.
        ///
        /// Driven by <c>MonsterDefinition.useAttackTelegraph</c>, a field that was authored
        /// on barbol and knight_red and read by nothing but a label in the F5 panel.
        /// </summary>
        public void SpawnTelegraph(Vector2 direction, float rangeMultiplier = 1f)
        {
            if (!showSlashVfx) return;

            float swingRange = Mathf.Max(0.01f, range * Mathf.Max(0.01f, rangeMultiplier));
            var color = slashVfxColor;
            color.a *= TelegraphAlphaScale;

            Vector2 origin = transform.position;
            Spells.SlashAttack.SpawnVisual(transform, origin, direction.normalized,
                                           swingRange, arcDegrees, color);
        }

        /// <summary>
        /// How much dimmer the telegraph is than the swing itself. It has to read as a
        /// warning rather than as the hit — if the two look alike, a player learns to
        /// dodge the wrong one.
        /// </summary>
        private const float TelegraphAlphaScale = 0.35f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
