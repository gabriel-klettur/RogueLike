using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Patrol state: follows waypoints, checks aggro.
    /// Maps to Python's PatrolState with dwell support.
    /// </summary>
    public class PatrolState : IState
    {
        private Vector2[] _waypoints;
        private int _currentIndex;
        private float _dwellTimer;
        private bool _waiting;

        public void Enter(StateMachine fsm)
        {
            _currentIndex = 0;
            _waiting = false;
            _dwellTimer = 0f;

            // Try to get waypoints from context
            _waypoints = fsm.GetContext<Vector2[]>("patrol_waypoints");
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            // A neighbour's shout, before perception: a patrolling monster facing away from
            // the fight still joins it, which is the whole point of the shout.
            if (FSMAlert.IsPending(fsm) && fsm.IsStateAllowed(nameof(AlertChaseState)))
            {
                fsm.ChangeState(new AlertChaseState());
                return;
            }

            // Exactly the acquisition rule IdleState uses, because it IS that rule now —
            // range, field of view, line of sight, target alive and solid. The two used to be
            // separate spellings of the same intent and had already drifted.
            if (FSMPerception.TryAcquire(fsm, out var spotted))
            {
                IdleState.OnAcquired(fsm, spotted);
                fsm.ChangeState(new ChaseState());
                return;
            }

            // No waypoints: stay idle
            if (_waypoints == null || _waypoints.Length == 0)
            {
                c?.StopMovement();
                return;
            }

            // Dwell at waypoint
            if (_waiting)
            {
                _dwellTimer -= dt;
                if (_dwellTimer <= 0f)
                {
                    _waiting = false;
                    _currentIndex = (_currentIndex + 1) % _waypoints.Length;
                }
                return;
            }

            // Move towards current waypoint
            Vector2 target = _waypoints[_currentIndex];
            Vector2 pos = fsm.Owner.transform.position;
            Vector2 dir = target - pos;
            float speed = fsm.GetContextFloat("speed", 2f);

            if (dir.sqrMagnitude <= (speed * dt) * (speed * dt))
            {
                float dwellTime = fsm.GetContextFloat("patrol_dwell", 0f);
                if (dwellTime > 0f)
                {
                    _waiting = true;
                    _dwellTimer = dwellTime;
                    c?.StopMovement();
                }
                else
                {
                    _currentIndex = (_currentIndex + 1) % _waypoints.Length;
                }
            }
            else
            {
                Vector2 moveDir = dir.normalized;
                c?.SetVelocity(moveDir * speed);
                if (c?.Animator != null && moveDir.sqrMagnitude > 0.0001f)
                {
                    var animDir = c.Animator.ResolveDirectionFromVector(moveDir);
                    c.Animator.SetState(DirectionalAnimator.AnimState.Walk, animDir);
                }
            }
        }

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();
        }
    }
}
