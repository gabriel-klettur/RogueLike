using UnityEngine;
using Valkur.Core;

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

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
            _attacked = false;
            _windupDuration = fsm.GetContextFloat("attack_windup_s", 0.2f);
            _attackDuration = _windupDuration + 0.3f;

            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Rb != null) c.Rb.velocity = Vector2.zero;

            FacePlayer(fsm, c);
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            _timer += dt;

            // Keep facing the player throughout the swing.
            FacePlayer(fsm, c);

            // Windup phase
            if (!_attacked && _timer >= _windupDuration)
            {
                _attacked = true;
                if (c?.Combat != null)
                {
                    var player = EntityRegistry.Player;
                    if (player != null)
                    {
                        Vector2 dir = ((Vector2)player.transform.position - (Vector2)fsm.Owner.transform.position).normalized;
                        c.Combat.TryAttack(dir);
                    }
                }
            }

            // Attack complete
            if (_timer >= _attackDuration)
            {
                // Check if player still in range
                var player2 = EntityRegistry.Player;
                if (player2 != null)
                {
                    float meleeRange = fsm.GetContextFloat("melee_range", 1.5f);
                    float dist = Vector2.Distance(fsm.Owner.transform.position, player2.transform.position);
                    if (dist <= meleeRange * 1.5f)
                    {
                        // Stay in attack range, reset
                        _timer = 0f;
                        _attacked = false;
                        return;
                    }
                }
                fsm.ChangeState(new ChaseState());
            }
        }

        public void Exit(StateMachine fsm) { }

        private static void FacePlayer(StateMachine fsm, FSMComponents c)
        {
            if (c?.Animator == null) return;
            var player = EntityRegistry.Player;
            if (player == null) return;
            Vector2 toPlayer = (Vector2)player.transform.position - (Vector2)fsm.Owner.transform.position;
            if (toPlayer.sqrMagnitude < 0.0001f) return;
            var dir = c.Animator.ResolveDirectionFromVector(toPlayer);
            c.Animator.SetState(DirectionalAnimator.AnimState.Attack, dir);
        }
    }
}
