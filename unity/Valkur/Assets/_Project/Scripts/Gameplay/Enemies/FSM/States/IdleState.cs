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
            if (c?.Rb != null) c.Rb.velocity = Vector2.zero;
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

            float dist = Vector2.Distance(fsm.Owner.transform.position, player.transform.position);
            if (dist <= aggroRange)
            {
                fsm.ChangeState(new ChaseState());
            }
        }

        public void Exit(StateMachine fsm) { }
    }
}
