using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat.Death;
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
        // Resolved through FSMTuning, which owns the keys and the defaults — these three
        // were duplicated from ChaseState and would have drifted apart on the first edit.

        private List<Vector2> _waypoints = new List<Vector2>();
        private int _waypointIndex;
        private float _repathTimer;

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
            _waypoints.Clear();
            _waypointIndex = 0;
            _repathTimer = float.MaxValue; // force immediate repath on first frame
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
            if (_timer >= FSMTuning.AlertDuration(fsm))
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            var player = FactionTargeting.EnemyOf(fsm.Owner);
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

            // Spirit-form players are invisible to NPC perception.
            var playerSpirit = player.GetComponent<PlayerSpiritState>();
            if (playerSpirit != null && playerSpirit.IsSpirit)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 playerPos = player.transform.position;
            Vector2 delta = playerPos - myPos;

            // Reaching the target has to mean the same thing here as it does in ChaseState.
            // Without this branch an alerted monster sprinted to the player and then stood
            // inside melee range doing nothing for the whole alert window, because the only
            // exits from this state were death, the timer and losing the target — the
            // `distSq <= meleeRange * meleeRange -> AttackState` test simply was not here.
            // That made the highest-priority authored edge in the shipped data
            // (t_any_alert, priority 200) lead to a state that could not fight, so being
            // shot from out of range turned a monster passive for five seconds instead of
            // provoking it. Tested BEFORE the movement block for the same reason ChaseState
            // tests it before its own: a monster already in reach must not take another step.
            float meleeRange = fsm.GetContextFloat("melee_range", 1.5f);
            if (delta.sqrMagnitude <= meleeRange * meleeRange)
            {
                fsm.ChangeState(new AttackState());
                return;
            }

            // Same contract as ChaseState: chasing_speed is the speed, not a base to
            // scale. See the note there on the removed hidden 1.5 multiplier.
            float chaseSpeed = fsm.GetContextFloat("chasing_speed", 4.5f);

            // Repath periodically
            _repathTimer += dt;
            if (_repathTimer >= FSMTuning.RepathInterval(fsm))
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
                float reach = FSMTuning.WaypointReachDistance(fsm);
                if (toTarget.sqrMagnitude < reach * reach)
                    _waypointIndex++;
                moveDir = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : delta.normalized;
            }
            else
            {
                moveDir = delta.normalized;
            }

            if (c?.Rb != null)
                c.SetVelocity(moveDir * chaseSpeed);

            if (c?.Animator != null && moveDir.sqrMagnitude > 0.0001f)
            {
                var dir = c.Animator.ResolveDirectionFromVector(moveDir);
                c.Animator.SetState(DirectionalAnimator.AnimState.Chase, dir);
            }
        }

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();
        }
    }
}
