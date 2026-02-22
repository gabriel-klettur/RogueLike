using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Death state: final state. For NPCs, destroys the GameObject.
    /// For players, applies grayscale and allows revive logic.
    /// Maps to Python's DeathState.
    /// </summary>
    public class DeathState : IState
    {
        public void Enter(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Rb != null) c.Rb.velocity = Vector2.zero;

            bool isPlayer = fsm.Owner.CompareTag("Player");
            if (!isPlayer)
            {
                Object.Destroy(fsm.Owner);
            }
        }

        public void Execute(StateMachine fsm, float dt)
        {
            // Player revive logic will be added in zone management step
        }

        public void Exit(StateMachine fsm) { }
    }
}
