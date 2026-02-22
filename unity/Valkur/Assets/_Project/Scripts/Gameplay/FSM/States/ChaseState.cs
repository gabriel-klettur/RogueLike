using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Chase state: pursues player with increased speed, transitions to Attack when in melee range.
    /// Maps to Python's ChaseState with aggro exit hysteresis and leash support.
    /// </summary>
    public class ChaseState : IState
    {
        private const float AGGRO_EXIT_HYSTERESIS = 1.15f;
        private const float CHASE_SPEED_MULTIPLIER = 1.5f;

        public void Enter(StateMachine fsm) { }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            var player = EntityRegistry.Player;
            if (player == null)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            var playerHealth = player.GetComponent<Health>();
            if (playerHealth != null && playerHealth.IsDead)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 playerPos = player.transform.position;
            Vector2 delta = playerPos - myPos;
            float distSq = delta.sqrMagnitude;

            // Check melee range
            float meleeRange = fsm.GetContextFloat("melee_range", 1.5f);
            if (distSq <= meleeRange * meleeRange)
            {
                fsm.ChangeState(new AttackState());
                return;
            }

            // Check aggro exit
            float aggroRange = fsm.GetContextFloat("aggro_range", 5f);
            float exitRange = aggroRange * AGGRO_EXIT_HYSTERESIS;
            if (distSq > exitRange * exitRange)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            // Move towards player
            float baseSpeed = fsm.GetContextFloat("chasing_speed", 3f);
            float chaseSpeed = baseSpeed * CHASE_SPEED_MULTIPLIER;
            if (c?.Rb != null)
                c.Rb.velocity = delta.normalized * chaseSpeed;

            // Flip sprite
            if (c?.Sprite != null)
                c.Sprite.flipX = delta.x < 0;
        }

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Rb != null) c.Rb.velocity = Vector2.zero;
        }
    }
}
