using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Chase state: pursues player with A* pathfinding, transitions to Attack when in melee range.
    /// Maps to Python's ChaseState with aggro exit hysteresis and leash support.
    /// Falls back to direct movement when PathFinder is unavailable.
    /// </summary>
    public class ChaseState : IState
    {
        private const float AGGRO_EXIT_HYSTERESIS = 1.15f;
        private const float CHASE_SPEED_MULTIPLIER = 1.5f;
        private const float REPATH_INTERVAL = 0.5f;
        private const float WAYPOINT_REACH_DIST = 0.25f;

        private List<Vector2> _waypoints = new List<Vector2>();
        private int _waypointIndex;
        private float _repathTimer;

        public void Enter(StateMachine fsm)
        {
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

            // Drive 8-direction animator each frame so the sprite faces the
            // movement direction. flipX would corrupt directional sprites.
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
