using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Panic: run away from the target for <c>flee_duration</c> seconds, then regroup.
    /// Reachable only from an authored transition (the shipped sets flee below 25 % HP) —
    /// there is no <c>new FleeState(</c> anywhere in the state classes.
    ///
    /// <para>TWO THINGS MADE THIS STATE WORSE THAN NOT FLEEING AT ALL, and both are fixed
    /// here. It ran in a STRAIGHT LINE away from the threat with no geometry test, so a
    /// monster cornered by the nearest building pressed itself into the wall for the whole
    /// window — the most visible way an AI can fail. And it handed control to
    /// <see cref="PatrolState"/>, which re-acquires on the very next tick, so a monster below
    /// its flee threshold bounced between chasing and fleeing on the authored transition's
    /// cooldown forever.</para>
    ///
    /// <para>The heading now comes from <see cref="FSMRetreat"/>, which probes a fan of
    /// escapes with the same line-of-sight test everything else uses, and the exit suppresses
    /// acquisition for <c>regroup_seconds</c> so the monster genuinely disengages before it
    /// can be pulled back in.</para>
    /// </summary>
    public class FleeState : IState
    {
        private float _fleeTimer;
        private float _headingTimer;
        private Vector2 _heading;

        /// <summary>
        /// Seconds between re-probing the escape heading. Re-picking every frame makes the
        /// body jitter between two equally open directions; holding one for a third of a
        /// second reads as a decision and still turns a corner in time.
        /// </summary>
        private const float HeadingInterval = 0.33f;

        /// <summary>
        /// Hard ceiling on the flee, as a multiple of the authored duration. The normal exit
        /// is the timer plus having got away; this is what stops a monster that cannot break
        /// contact from fleeing for the rest of the fight.
        /// </summary>
        private const float MaxDurationFactor = 2f;

        public void Enter(StateMachine fsm)
        {
            _fleeTimer = 0f;
            _headingTimer = float.MaxValue;   // probe on the first frame
            _heading = Vector2.zero;
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            _fleeTimer += dt;

            var threat = c != null && c.HasViableTarget(fsm) ? c.Target(fsm) : null;
            if (threat == null)
            {
                Regroup(fsm);
                fsm.ChangeState(new PatrolState());
                return;
            }

            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 threatPos = threat.transform.position;

            float duration = FSMTuning.FleeDuration(fsm);
            if (_fleeTimer >= duration && (IsClearOf(fsm, myPos, threatPos) ||
                                           _fleeTimer >= duration * MaxDurationFactor))
            {
                Regroup(fsm);
                fsm.ChangeState(new PatrolState());
                return;
            }

            float speed = fsm.GetContextFloat("speed", 2f) * FSMTuning.FleeSpeedMultiplier(fsm);

            _headingTimer += dt;
            if (_headingTimer >= HeadingInterval || _heading == Vector2.zero)
            {
                _headingTimer = 0f;
                _heading = FSMRetreat.Heading(myPos, threatPos, Mathf.Max(1f, speed));
            }

            // Cornered: every escape is blocked. Standing still is the honest answer — it is
            // what a cornered animal does, and it lets the player finish the kill instead of
            // watching a body grind along a wall.
            if (_heading == Vector2.zero)
            {
                c?.StopMovement();
                return;
            }

            c?.SetVelocity(_heading * speed);

            if (c?.Animator != null)
            {
                var dir = c.Animator.ResolveDirectionFromVector(_heading);
                c.Animator.SetState(DirectionalAnimator.AnimState.Walk, dir);
            }
        }

        /// <summary>Far enough away to stop running: past this monster's own aggro ring.</summary>
        private static bool IsClearOf(StateMachine fsm, Vector2 myPos, Vector2 threatPos)
        {
            float aggroRange = fsm.GetContextFloat("aggro_range", 5f);
            return (myPos - threatPos).sqrMagnitude > aggroRange * aggroRange;
        }

        /// <summary>
        /// Refuse acquisition for a moment on the way out. Without it PatrolState re-aggros
        /// on the next tick and the flee was a two-frame animation of turning round.
        /// </summary>
        private static void Regroup(StateMachine fsm)
            => FSMPerception.SuppressAggro(fsm, FSMTuning.RegroupSeconds(fsm));

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();
        }
    }
}
