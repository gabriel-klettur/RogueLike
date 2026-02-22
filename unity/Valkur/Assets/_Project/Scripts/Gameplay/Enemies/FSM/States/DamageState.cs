using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Damage stun state: brief pause after taking a hit, then returns to chase.
    /// Maps to Python's DamageState with configurable duration and from_left direction.
    /// </summary>
    public class DamageState : IState
    {
        private readonly float _duration;
        private readonly bool _fromLeft;
        private float _timer;

        public DamageState(float duration = 0.25f, bool fromLeft = false)
        {
            _duration = duration;
            _fromLeft = fromLeft;
        }

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Rb != null) c.Rb.velocity = Vector2.zero;
        }

        public void Execute(StateMachine fsm, float dt)
        {
            _timer += dt;
            if (_timer >= _duration)
            {
                fsm.ChangeState(new ChaseState());
            }
        }

        public void Exit(StateMachine fsm) { }
    }
}
