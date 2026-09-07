using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// "Walk to there, around whatever is in the way" — the one implementation.
    ///
    /// <para><see cref="ChaseState"/> and <see cref="AlertChaseState"/> each carried their
    /// own copy of the repath timer, the waypoint list, the index, the reach test and the
    /// fall-back-to-direct branch: forty lines, duplicated verbatim, already documented in
    /// this project as having drifted once (the hidden 1.5 chase multiplier, and the two
    /// tuning constants that became <see cref="FSMTuning"/>). Every state that wants to walk
    /// somewhere now shares this, so a fix to pathing reaches all of them.</para>
    ///
    /// <para>It also owns the two things a per-state copy could not: the waypoint list is
    /// reused rather than reallocated per repath, and a repath REFUSED by
    /// <see cref="PathFinder"/>'s frame budget keeps the path it already has instead of
    /// silently degrading to a straight line into a wall.</para>
    /// </summary>
    public sealed class FSMPathFollower
    {
        private readonly List<Vector2> _waypoints = new List<Vector2>(32);
        private int _index;
        private float _repathTimer;

        /// <summary>Waypoints not yet consumed, for the debug overlay and for tests.</summary>
        public IReadOnlyList<Vector2> Waypoints => _waypoints;

        /// <summary>Index of the waypoint currently being walked toward.</summary>
        public int Index => _index;

        /// <summary>True while a solved path is still being followed.</summary>
        public bool HasPath => _index < _waypoints.Count;

        /// <summary>
        /// Forget the current path and force a repath on the next <see cref="Steer"/>.
        /// Call from a state's <c>Enter</c>: a path solved for the previous visit aims at
        /// where the target used to be.
        /// </summary>
        public void Reset()
        {
            _waypoints.Clear();
            _index = 0;
            _repathTimer = float.MaxValue;
        }

        /// <summary>
        /// The direction to move this tick to get from <paramref name="from"/> toward
        /// <paramref name="goal"/>. Normalised, or zero when already there.
        /// </summary>
        public Vector2 Steer(StateMachine fsm, Vector2 from, Vector2 goal, float dt)
        {
            _repathTimer += dt;
            if (_repathTimer >= FSMTuning.RepathInterval(fsm) && PathFinder.HasInstance)
            {
                // A refused search leaves the timer AT the threshold rather than resetting
                // it, so the request is retried on the very next tick instead of waiting a
                // whole repath interval for a budget that was momentarily full.
                if (PathFinder.Instance.TryFindPath(from, goal, _waypoints))
                {
                    _repathTimer = 0f;
                    _index = 0;
                }
            }

            Vector2 toGoal = goal - from;

            if (_index < _waypoints.Count)
            {
                Vector2 target = _waypoints[_index];
                Vector2 toTarget = target - from;
                float reach = FSMTuning.WaypointReachDistance(fsm);
                if (toTarget.sqrMagnitude < reach * reach)
                {
                    _index++;
                    if (_index < _waypoints.Count)
                        toTarget = _waypoints[_index] - from;
                }

                if (toTarget.sqrMagnitude > 0.0001f) return toTarget.normalized;
            }

            return toGoal.sqrMagnitude > 0.0001f ? toGoal.normalized : Vector2.zero;
        }
    }
}
