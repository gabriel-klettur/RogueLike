using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Unconscious state: entity is at 0 HP, plays death animation, then transitions to DeathState.
    /// Maps to Python's UnconsciousState with configurable disappear timer.
    /// </summary>
    public class UnconsciousState : IState
    {
        private float _timer;
        private float _disappearTime;

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
            _disappearTime = fsm.GetContextFloat("death_disappear_time", 10f);

            var rb = fsm.Owner.GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = Vector2.zero;
        }

        public void Execute(StateMachine fsm, float dt)
        {
            _timer += dt;
            if (_timer >= _disappearTime)
            {
                fsm.ChangeState(new DeathState());
            }
        }

        public void Exit(StateMachine fsm) { }
    }
}
