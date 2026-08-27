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
        private readonly string _returnStateClass;
        private float _timer;

        /// <param name="returnStateClass">
        /// C# class name of the state to resume when the flinch ends. Null falls back to
        /// <c>ChaseState</c>, which is what this state used to do UNCONDITIONALLY — so any
        /// entity hit by a stray area spell started chasing, including a neutral vendor
        /// standing in a shop doorway. It was also a latent deadlock: for a set whose
        /// vocabulary omits ChaseState, the allowed-state guard silently refuses that
        /// transition and the entity loops in DamageState with zero velocity forever.
        /// </param>
        /// <summary>
        /// What this flinch interrupted, by class name. Exposed because a SECOND hit that
        /// wins the flinch roll mid-flinch must carry THIS forward: capturing the current
        /// state's own name at that moment records "DamageState", and resuming into
        /// DamageState is unconstructable (three-parameter constructor), so the resume
        /// threw MissingMethodException and silently fell back to ChaseState — a monster
        /// hit twice in quick succession forgot it was patrolling.
        /// </summary>
        public string ReturnStateClass => _returnStateClass;

        public DamageState(float duration = 0.25f, bool fromLeft = false,
                           string returnStateClass = null)
        {
            _duration = duration;
            _fromLeft = fromLeft;
            _returnStateClass = returnStateClass;
        }

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();

            // Match Python parity: face the incoming hit (left/right).
            if (c?.Animator != null)
            {
                var dir = _fromLeft
                    ? DirectionalAnimator.Direction.West
                    : DirectionalAnimator.Direction.East;
                c.Animator.SetState(DirectionalAnimator.AnimState.Damage, dir);
            }
        }

        public void Execute(StateMachine fsm, float dt)
        {
            _timer += dt;
            if (_timer < _duration) return;

            // Resume what the hit interrupted. A fresh instance, not the captured one: a
            // state carries per-visit data (ChaseState's waypoint list, AttackState's swing
            // timers) and re-entering the old object would replay a half-finished swing.
            IState resume = _returnStateClass != null
                ? Enemies.FSM.FSMRuntimeFactory.CreateState(_returnStateClass)
                : null;

            fsm.ChangeState(resume ?? new ChaseState());
        }

        public void Exit(StateMachine fsm) { }
    }
}
