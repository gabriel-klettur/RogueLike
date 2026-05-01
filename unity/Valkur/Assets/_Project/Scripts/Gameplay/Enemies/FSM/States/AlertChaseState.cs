using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Alert chase state: 5-second chase after taking ranged damage, ignores aggro range.
    /// Maps to Python's AlertChaseState. Uses A* pathfinding with direct fallback.
    /// </summary>
    public class AlertChaseState : IState
    {
        private float _timer;
        private const float ALERT_DURATION = 5f;
        private const float CHASE_SPEED_MULTIPLIER = 1.5f;
        private const float REPATH_INTERVAL = 0.5f;
        private const float WAYPOINT_REACH_DIST = 0.25f;

        private List<Vector2> _waypoints = new List<Vector2>();
        private int _waypointIndex;
        private float _repathTimer;

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
            _waypoints.Clear();
            _waypointIndex = 0;
            _repathTimer = REPATH_INTERVAL; // force immediate repath on first frame
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
            if (_timer >= ALERT_DURATION)
            {
                fsm.ChangeState(new PatrolState());
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

            float baseSpeed = fsm.GetContextFloat("chasing_speed", 3f);
            float chaseSpeed = baseSpeed * CHASE_SPEED_MULTIPLIER;

            // Repath periodically
            _repathTimer += dt;
            if (_repathTimer >= REPATH_INTERVAL)
            {
                _repathTimer = 0f;
                if (PathFinder.Instance != null)
                {
                    _waypoints = PathFinder.Instance.FindPath(myPos, playerPos);
                    _waypointIndex = 0;
                }
            }

            // Follow waypoints or fall back to direct movement
            Vector2 moveDir;
            if (_waypoints != null && _waypointIndex < _waypoints.Count)
            {
                Vector2 target = _waypoints[_waypointIndex];
                Vector2 toTarget = target - myPos;
                if (toTarget.sqrMagnitude < WAYPOINT_REACH_DIST * WAYPOINT_REACH_DIST)
                    _waypointIndex++;
                moveDir = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : delta.normalized;
            }
            else
            {
                moveDir = delta.normalized;
            }

            if (c?.Rb != null)
                c.Rb.velocity = moveDir * chaseSpeed;

            if (c?.Animator != null && moveDir.sqrMagnitude > 0.0001f)
            {
                var dir = c.Animator.ResolveDirectionFromVector(moveDir);
                c.Animator.SetState(DirectionalAnimator.AnimState.Chase, dir);
            }
        }

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Rb != null) c.Rb.velocity = Vector2.zero;
        }
    }
}
