using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Someone who lives at a spot and drifts around it: a few breaths standing still, one
    /// short step somewhere, a few more breaths. Gatita's state.
    ///
    /// <para>WHY IT IS NOT IDLE-PLUS-PATROL. That is what it used to be, and the reason it
    /// read as PATROLLING is that it was one: <c>NPC_Stroller</c> authored Idle for exactly
    /// 240 frames and Patrol for exactly 300, and <c>PatrolWaypointGenerator</c>'s
    /// <c>stroll</c> handed <see cref="PatrolState"/> exactly TWO waypoints on one horizontal
    /// rail. Every bout also began at index 0, so she set off WEST every single time. Fixed
    /// track, fixed durations, fixed opening move — three separate things a player reads as a
    /// guard walking a beat, and none of them expressible away inside the authored FSM,
    /// because a transition's <c>cooldown_frames</c> is one constant and there is nowhere to
    /// say "between one and five".</para>
    ///
    /// <para>The rhythm is measured in ANIMATION CYCLES rather than seconds, which is what
    /// makes it look deliberate instead of merely random: the walk bout is exactly one
    /// complete walk cycle, so the feet land where they started rather than being cut
    /// mid-stride, and the idle hold is a whole number of breaths. Both lengths come from
    /// <c>DirectionalAnimator.GetStateLength</c>, so retiming the art retimes the stroll and
    /// the two cannot drift — Gatita's idle is authored at 0.40x and measures 2.25 s against
    /// a 1.20 s walk, which no constant in this file knows.</para>
    ///
    /// <para>PEACEFUL BY CONSTRUCTION. This state never looks for a target, so it needs no
    /// help from the set's allowed-state whitelist to stay harmless — unlike
    /// <see cref="IdleState"/>, which tries <see cref="ChaseState"/> and is refused (and logs
    /// a warning for it) in any set that omits Chase.</para>
    /// </summary>
    public class StrollState : IState
    {
        /// <summary>Fewest whole idle cycles held between two walks.</summary>
        private const int IDLE_CYCLES_MIN = 1;

        /// <summary>Most whole idle cycles held between two walks, inclusive.</summary>
        private const int IDLE_CYCLES_MAX = 5;

        /// <summary>
        /// How far from home she is allowed to drift before a bout is aimed back at it.
        /// One bout covers speed x walk cycle — 0.96 units for Gatita — so this is about two
        /// bouts of rope: enough that the walk reads as wandering rather than as a tether,
        /// and short enough that a player who left her at her stall finds her there.
        /// </summary>
        private const float WANDER_RADIUS = 2f;

        /// <summary>
        /// Spread either side of the homeward bearing, in radians (+/- 50 degrees), so the
        /// way back is a drift rather than a beeline.
        /// </summary>
        private const float HOMEWARD_JITTER = 0.87f;

        /// <summary>
        /// What survives of a NORTHWARD heading. Art drawn as a single front-facing view has
        /// no back to show and <c>DirectionalAnimator</c> never flips, so walking up the
        /// screen while facing the camera reads as moon-walking. This is the honest version
        /// of the constraint that used to be enforced by allowing only two directions: every
        /// bearing is available, and the ones that would be read wrong are flattened towards
        /// lateral rather than forbidden.
        ///
        /// <para>A BIAS, not a ban, and the difference is measurable: over 400 draws all eight
        /// octants were populated and the steeply-northward pair fell from a uniform 25 % to
        /// 17 %. Due north is a FIXED POINT of the flatten — scaling the y of (0, 1) and
        /// renormalising gives (0, 1) back — so it still comes up, just rarely. That is the
        /// intended trade: rejecting it outright would leave a character hemmed in on three
        /// sides with nowhere legal to go.</para>
        /// </summary>
        private const float NORTH_FLATTEN = 0.45f;

        /// <summary>
        /// Headings tried before a bout gives up and is spent standing still. She lives among
        /// buildings and market stalls, so "the way I picked is into a wall" is the normal
        /// case, not the edge — and standing still for one bout is invisible, while walking
        /// into a wall for a full cycle is not.
        /// </summary>
        private const int HEADING_ATTEMPTS = 6;

        /// <summary>Used when the entity has no idle art to measure.</summary>
        private const float IDLE_FALLBACK_SECONDS = 2.5f;

        /// <summary>Used when the entity has no walk art to measure.</summary>
        private const float WALK_FALLBACK_SECONDS = 1.2f;

        /// <summary>
        /// Where the stroll is centred. Held in the FSM CONTEXT rather than in this object
        /// because a state instance does not survive a detour through <c>DamageState</c>, and
        /// a home re-captured on the way back would be wherever the blow left her — which
        /// over a fight walks the whole stroll across the map.
        /// </summary>
        private const string HOME_KEY = "stroll_home";

        private Vector2 _home;
        private bool _walking;
        private float _phaseRemaining;
        private Vector2 _heading;

        public void Enter(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            _home = ResolveHome(fsm);
            BeginIdle(c);
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            // Re-asserted every tick, like every other state: SetState early-returns when
            // nothing changed, so this costs nothing and survives anything else that writes
            // the animator between two of our own frames.
            if (_walking) DriveWalk(fsm, c);
            else c?.StopMovement();

            _phaseRemaining -= dt;
            if (_phaseRemaining > 0f) return;

            if (_walking) BeginIdle(c);
            else BeginWalk(fsm, c);
        }

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();
        }

        // Phases ---------------------------------------------------------------------

        private void BeginIdle(FSMComponents c)
        {
            _walking = false;
            _heading = Vector2.zero;
            c?.StopMovement();

            float cycle = CycleLength(c, DirectionalAnimator.AnimState.Idle, IDLE_FALLBACK_SECONDS);
            _phaseRemaining = cycle * Random.Range(IDLE_CYCLES_MIN, IDLE_CYCLES_MAX + 1);

            PlayFromFrameZero(c, DirectionalAnimator.AnimState.Idle,
                              c != null && c.Animator != null
                                  ? c.Animator.CurrentDirection
                                  : DirectionalAnimator.Direction.South);
        }

        private void BeginWalk(StateMachine fsm, FSMComponents c)
        {
            float cycle = CycleLength(c, DirectionalAnimator.AnimState.Walk, WALK_FALLBACK_SECONDS);
            float speed = fsm.GetContextFloat("speed", 2f);
            Vector2 pos = fsm.Owner.transform.position;

            _heading = PickHeading(pos, speed * cycle);

            // A bout with nowhere to go is spent standing still rather than skipped: skipping
            // would run the picker again on the very next frame, and in a corner that is a
            // busy loop that never produces a step.
            if (_heading == Vector2.zero)
            {
                BeginIdle(c);
                return;
            }

            _walking = true;
            _phaseRemaining = cycle;

            var dir = c != null && c.Animator != null
                ? c.Animator.ResolveDirectionFromVector(_heading)
                : DirectionalAnimator.Direction.South;
            PlayFromFrameZero(c, DirectionalAnimator.AnimState.Walk, dir);
        }

        private void DriveWalk(StateMachine fsm, FSMComponents c)
        {
            float speed = fsm.GetContextFloat("speed", 2f);
            c?.SetVelocity(_heading * speed);
            if (c?.Animator != null)
                c.Animator.SetState(DirectionalAnimator.AnimState.Walk,
                                    c.Animator.ResolveDirectionFromVector(_heading));
        }

        // Geometry -------------------------------------------------------------------

        /// <summary>
        /// A bearing whose whole bout is clear of world geometry, or zero when several tries
        /// all ran into something.
        /// </summary>
        private Vector2 PickHeading(Vector2 pos, float distance)
        {
            Vector2 toHome = _home - pos;
            bool outside = toHome.sqrMagnitude > WANDER_RADIUS * WANDER_RADIUS;
            float homeward = outside ? Mathf.Atan2(toHome.y, toHome.x) : 0f;

            for (int attempt = 0; attempt < HEADING_ATTEMPTS; attempt++)
            {
                float angle = outside
                    ? homeward + Random.Range(-HOMEWARD_JITTER, HOMEWARD_JITTER)
                    : Random.Range(0f, Mathf.PI * 2f);

                Vector2 dir = Flatten(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
                if (World.LineOfSight.IsClear(pos, pos + dir * distance)) return dir;
            }
            return Vector2.zero;
        }

        /// <summary>Compresses the upward half of the circle. See <see cref="NORTH_FLATTEN"/>.</summary>
        private static Vector2 Flatten(Vector2 dir)
        {
            if (dir.y > 0f) dir.y *= NORTH_FLATTEN;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        }

        private Vector2 ResolveHome(StateMachine fsm)
        {
            var stored = fsm.GetContext<Vector2[]>(HOME_KEY, null);
            if (stored != null && stored.Length == 1) return stored[0];

            // The stroll's centre is the SPAWN, and the waypoints are the only thing that
            // still remembers it by the time this runs: `stroll` builds them symmetrically
            // around the origin, so their midpoint is that origin exactly. Falling back to
            // the current position would centre the walk on wherever she happens to be.
            var waypoints = fsm.GetContext<Vector2[]>("patrol_waypoints", null);
            Vector2 home = fsm.Owner.transform.position;
            if (waypoints != null && waypoints.Length > 0)
            {
                Vector2 sum = Vector2.zero;
                for (int i = 0; i < waypoints.Length; i++) sum += waypoints[i];
                home = sum / waypoints.Length;
            }

            fsm.SetContext(HOME_KEY, new[] { home });
            return home;
        }

        private static float CycleLength(FSMComponents c, DirectionalAnimator.AnimState state,
                                         float fallbackSeconds)
        {
            float measured = c?.Animator != null ? c.Animator.GetStateLength(state) : 0f;
            return measured > 0f ? measured : fallbackSeconds;
        }

        /// <summary>
        /// Start <paramref name="state"/> at frame 0. <c>SetState</c> alone early-returns when
        /// neither state nor direction changed, so a bout that happens to reuse the previous
        /// heading would otherwise begin wherever the last one left the cursor — and the whole
        /// point of sizing a bout in cycles is that it starts and ends on a whole one.
        /// </summary>
        private static void PlayFromFrameZero(FSMComponents c, DirectionalAnimator.AnimState state,
                                              DirectionalAnimator.Direction direction)
        {
            if (c?.Animator == null) return;
            c.Animator.SetState(state, direction);
            c.Animator.RestartCurrentState();
        }
    }
}
