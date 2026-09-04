using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Attack state: performs melee attack, then transitions back to Chase.
    /// Maps to Python's AttackState with windup and cooldown.
    /// </summary>
    public class AttackState : IState
    {
        private float _timer;
        private bool _attacked;
        private float _windupDuration;
        private float _attackDuration;
        private int _variant = -1;

        public void Enter(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();

            _windupDuration = fsm.GetContextFloat("attack_windup_s", 0.2f);
            BeginSwing(fsm, c);
        }

        /// <summary>
        /// Starts one swing: picks the attack animation, sizes the state to it, and
        /// resets the damage gate. Shared by <see cref="Enter"/> and the re-swing branch in
        /// <see cref="Execute"/> — a knight the player never backs away from stays in this
        /// state indefinitely, so without this the second swing would reuse the first one's
        /// animation and start wherever its sprite loop happened to be.
        /// </summary>
        private void BeginSwing(StateMachine fsm, FSMComponents c)
        {
            _timer = 0f;
            _attacked = false;
            _variant = PickVariant(fsm, c);

            // Turn FIRST, then measure. GetStateLength reports the frame count of the
            // animator's CURRENT direction, so measuring before the turn sizes this swing
            // against whichever way the entity happened to be facing when it entered —
            // measured, that made the very first swing 0.5 s instead of 1.2 s on a set whose
            // direction buckets differ in length.
            FacePlayer(fsm, c, _variant);

            // windup + 0.3 s is the historical swing length and stays the FLOOR, so the 18
            // monsters with a one-frame attack pose are paced exactly as before. An entity
            // whose attack is genuinely animated gets the length its frames need: the
            // knight's eight frames run 1.2 s against that 0.75 s floor and were being cut
            // mid-arc.
            //
            // This DOES move the damage rate for such an entity, and the melee cooldown does
            // not hold it steady — it only bounds it. One TryAttack is attempted per swing,
            // at the windup, so the realised interval is the swing period rounded up to the
            // next multiple that clears the cooldown. knight_red measured: a 0.75 s swing
            // against a 1.1 s cooldown lands a hit every 1.5 s (every other attempt refused);
            // a 1.2 s swing lands one every 1.2 s. Retiming an attack animation retimes its
            // damage, so re-check meleeCooldown when you do.
            _attackDuration = _windupDuration + 0.3f;
            if (c?.Animator != null)
            {
                float animLength = c.Animator.GetStateLength(
                    DirectionalAnimator.AnimState.Attack, _variant);
                if (animLength > _attackDuration) _attackDuration = animLength;
            }

            c?.Animator?.RestartCurrentState();

            TelegraphSwing(fsm, c);
        }

        /// <summary>
        /// Shows where this swing is going to land, at the moment it starts winding up.
        ///
        /// <c>MonsterDefinition.useAttackTelegraph</c> is authored on barbol and knight_red
        /// and, until now, was read by exactly one thing: a row in the F5 properties panel.
        /// It read as a promise that the monster tells you before it hits, and no telegraph
        /// implementation existed anywhere in the project.
        ///
        /// Skipped when the entity has no windup to telegraph — a tell that appears and
        /// resolves in the same instant is noise, not information.
        /// </summary>
        private void TelegraphSwing(StateMachine fsm, FSMComponents c)
        {
            if (c?.Combat == null) return;
            if (!fsm.GetContextBool("use_attack_telegraph")) return;
            if (_windupDuration < MinWindupToTelegraph) return;

            var player = FactionTargeting.EnemyOf(fsm.Owner);
            if (player == null || fsm.Owner == null) return;

            Vector2 dir = ((Vector2)player.transform.position - (Vector2)fsm.Owner.transform.position).normalized;
            var variant = ResolveVariant(fsm, _variant);
            c.Combat.SpawnTelegraph(dir, variant != null ? variant.rangeMultiplier : 1f);
        }

        /// <summary>
        /// Below this windup the tell and the hit are indistinguishable in time, so drawing
        /// one only adds a flash. Nine of the eleven shipped hostiles wind up for 0 s.
        /// </summary>
        private const float MinWindupToTelegraph = 0.15f;

        /// <summary>
        /// Which attack animation this swing uses. -1 when the entity has none declared,
        /// which is every monster but the knight and resolves to the single attack set.
        /// Random for now; the knobs a smarter rule would need (range, windup) already
        /// arrive as FSM context floats right here.
        /// </summary>
        private static int PickVariant(StateMachine fsm, FSMComponents c)
        {
            int count = c?.Animator != null ? c.Animator.AttackVariantCount : 0;
            if (count <= 0) return -1;

            var variants = fsm.GetContext<AttackVariant[]>(AttackVariantContextKey);
            if (variants == null || variants.Length == 0)
                return Random.Range(0, count);   // animations only, no authored moveset

            float distance = DistanceToPlayer(fsm);

            // Weighted pick among the moves whose distance gate passes. This used to be a
            // bare Random.Range with a "Random for now" comment, which is why knight_red's
            // five authored moves — slash, shieldbash, punch, kick, jumpkick — were five
            // animations over one identical hit.
            int totalWeight = 0;
            int usable = Mathf.Min(count, variants.Length);
            for (int i = 0; i < usable; i++)
            {
                if (variants[i] == null) continue;
                if (!variants[i].AllowedAt(distance)) continue;
                totalWeight += Mathf.Max(0, variants[i].weight);
            }

            // Every move gated out at this distance: fall back to a uniform pick rather
            // than refusing to attack. A monster standing there doing nothing reads as
            // broken; an imperfect move reads as a monster.
            if (totalWeight <= 0) return Random.Range(0, count);

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < usable; i++)
            {
                if (variants[i] == null) continue;
                if (!variants[i].AllowedAt(distance)) continue;
                roll -= Mathf.Max(0, variants[i].weight);
                if (roll < 0) return i;
            }
            return 0;
        }

        /// <summary>Context key carrying the authored moveset, published by FSMMonsterBrain.</summary>
        public const string AttackVariantContextKey = "attack_variants";

        private static float DistanceToPlayer(StateMachine fsm)
        {
            var player = FactionTargeting.EnemyOf(fsm.Owner);
            if (player == null || fsm.Owner == null) return 0f;
            return Vector2.Distance(fsm.Owner.transform.position, player.transform.position);
        }

        /// <summary>The authored data for the variant currently being swung, or null.</summary>
        private static AttackVariant ResolveVariant(StateMachine fsm, int index)
        {
            if (index < 0) return null;
            var variants = fsm.GetContext<AttackVariant[]>(AttackVariantContextKey);
            if (variants == null || index >= variants.Length) return null;
            return variants[index];
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            // Spirit-form players are intangible — abandon the attack and
            // fall back to Patrol so the NPC stops swinging at empty air.
            var playerForSpiritCheck = FactionTargeting.EnemyOf(fsm.Owner);
            if (playerForSpiritCheck != null)
            {
                var spirit = playerForSpiritCheck.GetComponent<PlayerSpiritState>();
                if (spirit != null && spirit.IsSpirit)
                {
                    fsm.ChangeState(new PatrolState());
                    return;
                }
            }

            _timer += dt;

            // Keep facing the player throughout the swing.
            FacePlayer(fsm, c, _variant);

            // Windup phase
            if (!_attacked && _timer >= _windupDuration)
            {
                _attacked = true;
                // A stunned monster does not land its swing. The animation still
                // plays out — the entity is committed to the pose — but the damage
                // window is skipped, which is what makes the player's crowd control
                // mean anything on the monster side. StatusEffectManager.IsStunned
                // was previously read by the player controller and NPCAutoCast and by
                // nothing in the FSM at all.
                if (c != null && !c.IsStunned && c.Combat != null)
                {
                    var player = FactionTargeting.EnemyOf(fsm.Owner);
                    if (player != null)
                    {
                        Vector2 dir = ((Vector2)player.transform.position - (Vector2)fsm.Owner.transform.position).normalized;
                        var variant = ResolveVariant(fsm, _variant);
                        if (variant != null)
                            c.Combat.TryAttack(dir, variant.damageMultiplier,
                                               variant.rangeMultiplier, variant.cooldownMultiplier);
                        else
                            c.Combat.TryAttack(dir);
                    }
                }
            }

            // Attack complete
            if (_timer >= _attackDuration)
            {
                // Check if player still in range
                var player2 = FactionTargeting.EnemyOf(fsm.Owner);
                if (player2 != null)
                {
                    float meleeRange = fsm.GetContextFloat("melee_range", 1.5f);
                    float dist = Vector2.Distance(fsm.Owner.transform.position, player2.transform.position);
                    if (dist <= meleeRange * FSMTuning.ReswingRangeFactor(fsm))
                    {
                        // Stay in attack range and swing again — through BeginSwing, so the
                        // next swing re-rolls its animation and replays it from frame 0.
                        BeginSwing(fsm, c);
                        return;
                    }
                }
                fsm.ChangeState(new ChaseState());
            }
        }

        public void Exit(StateMachine fsm) { }

        private static void FacePlayer(StateMachine fsm, FSMComponents c, int variant)
        {
            if (c?.Animator == null) return;
            var player = FactionTargeting.EnemyOf(fsm.Owner);
            if (player == null) return;
            Vector2 toPlayer = (Vector2)player.transform.position - (Vector2)fsm.Owner.transform.position;
            if (toPlayer.sqrMagnitude < 0.0001f) return;
            var dir = c.Animator.ResolveDirectionFromVector(toPlayer);
            c.Animator.SetState(DirectionalAnimator.AnimState.Attack, dir, variant);
        }
    }
}
