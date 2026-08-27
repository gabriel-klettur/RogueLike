using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// NPC idle state. Checks aggro range to transition to Chase.
    /// Maps to Python's IdleState.
    /// </summary>
    public class IdleState : IState
    {
        public void Enter(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();
            if (c?.Animator != null)
                c.Animator.SetState(DirectionalAnimator.AnimState.Idle, c.Animator.CurrentDirection);
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            float aggroRange = fsm.GetContextFloat("aggro_range", 5f);
            var player = EntityRegistry.Player;
            if (player == null) return;

            // Spirit-form players are invisible to NPC perception.
            var spirit = player.GetComponent<PlayerSpiritState>();
            if (spirit != null && spirit.IsSpirit) return;

            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 playerPos = player.transform.position;
            float dist = Vector2.Distance(myPos, playerPos);
            if (dist > aggroRange) return;

            // Aggro requires an unobstructed line. Without it this was a naked
            // distance test and everything behind a wall woke up when the player
            // walked past it. Line of sight is checked on ACQUISITION only —
            // ChaseState keeps its distance-based exit, so a monster that has already
            // committed does not give up the instant you round a corner.
            if (World.LineOfSight.IsBlocked(myPos, playerPos)) return;

            fsm.ChangeState(new ChaseState());
        }

        public void Exit(StateMachine fsm) { }
    }
}
