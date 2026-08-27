using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat.Death;
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
        // Every feel knob this state used to hold as a private const now resolves through
        // FSMTuning, which owns the key AND the default. Two of them — the repath interval
        // and the waypoint reach distance — were also written out verbatim in
        // AlertChaseState, free to drift apart the moment anyone edited one and not the
        // other. Read once per Enter/Execute rather than cached in a field, because a
        // live `reconfig` re-publishes the context and the state should pick that up.

        private List<Vector2> _waypoints = new List<Vector2>();
        private int _waypointIndex;
        private float _repathTimer;

        public void Enter(StateMachine fsm)
        {
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
            float exitRange = aggroRange * FSMTuning.AggroExitHysteresis(fsm);
            if (distSq > exitRange * exitRange)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            // Leash. This class has documented "leash support" since it was written and
            // never had any: the only exit was player distance, so a monster would follow
            // across the whole map as long as the player stayed inside its aggro ring, and
            // barbol_gigante's ring is 30 units wide. Breaking off returns it to
            // PatrolState, whose waypoints are anchored at the spawn point, so it walks
            // home on its own without needing a new state class.
            //
            // The range is authorable per monster (MonsterDefinition.aiTuning.leashRange);
            // unset, it derives from this monster's own aggro range, so a wide-ranging
            // monster gets a correspondingly long tether without anyone having to keep two
            // numbers in agreement.
            if (fsm.Context.ContainsKey(FSMHomeAnchor.KeyX))
            {
                float leash = FSMTuning.LeashRange(fsm, aggroRange);
                var home = new Vector2(fsm.GetContextFloat(FSMHomeAnchor.KeyX),
                                       fsm.GetContextFloat(FSMHomeAnchor.KeyY));
                if ((myPos - home).sqrMagnitude > leash * leash)
                {
                    fsm.ChangeState(new PatrolState());
                    return;
                }
            }

            // chasing_speed IS the chase speed. It used to be multiplied by a hidden
            // 1.5 here and in AlertChaseState, so every authored chasingSpeed in every
            // monster asset understated the real value by a third and the two states
            // would drift apart the moment one was edited. The assets were rebaselined
            // (x1.5) when the multiplier was removed, so behaviour is unchanged.
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
            c?.StopMovement();
        }
    }
}
