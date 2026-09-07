using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// A short lateral burst out of the line of fire, then straight back into the fight.
    ///
    /// <para>WHY THIS IS NOT <see cref="FleeState"/> WITH A SHORTER TIMER. A flee is a
    /// decision to STOP fighting: it runs away from the threat, suppresses re-acquisition on
    /// the way out (<c>regroup_seconds</c>) and hands control to <see cref="PatrolState"/>. A
    /// dodge is part of fighting — it moves ACROSS the threat, keeps the target, suppresses
    /// nothing, and returns to whatever it interrupted. Running the two through one state
    /// would mean a monster that dodges a bolt also forgets who shot it.</para>
    ///
    /// <para>THE BURST IS SIZED IN DISTANCE, NOT IN SECONDS, and that is deliberate. A
    /// duration authored beside a speed is two numbers that must agree: retune
    /// <c>chasingSpeed</c> and a 0.25 s dodge silently becomes twice as long a sidestep. The
    /// state derives its own length from <c>dodge_distance</c> and the speed it is actually
    /// travelling at, so the only thing an author sets is how far out of the way the monster
    /// gets — which is the thing they can see. <see cref="MaxDuration"/> is the backstop
    /// against a monster tuned so slowly that the division never terminates the state.</para>
    ///
    /// <para>NO INVULNERABILITY. A dodge that cannot fail is not a dodge, it is a damage
    /// immunity with an animation: the player would learn that shooting a Dark entity mid-
    /// sidestep is wasted mana rather than that it needs to be led. The monster genuinely
    /// leaves the corridor or genuinely does not, and a shot that was aimed where it went
    /// still hits.</para>
    ///
    /// <para>Reachable BOTH ways. <c>ChaseState</c> and <c>AttackState</c> enter it with a
    /// heading already probed clear (the projectile answer); an authored transition —
    /// <c>Monster_Dark</c> carries one on <c>time_since_hit</c> — enters it through the
    /// parameterless constructor <c>FSMRuntimeFactory</c> needs, and it picks its own
    /// heading off the target in <see cref="Enter"/>.</para>
    /// </summary>
    public class DodgeState : IState
    {
        /// <summary>
        /// Hard ceiling on one burst, whatever the arithmetic says. A monster authored at a
        /// crawl would otherwise divide its way into a sidestep several seconds long, during
        /// which it is neither chasing nor attacking — the AI reading as hung, which is the
        /// failure <see cref="FleeState"/>'s own history is a record of.
        /// </summary>
        private const float MaxDuration = 0.6f;

        /// <summary>
        /// Floor on one burst. Below this the state is entered and left inside a couple of
        /// frames: the body barely moves, the animation never reads, and all the player sees
        /// is a stutter in the chase.
        /// </summary>
        private const float MinDuration = 0.12f;

        private readonly Vector2 _requestedHeading;
        private Vector2 _heading;
        private float _timer;
        private float _duration;

        /// <summary>Authored entry: the heading is chosen on <see cref="Enter"/>.</summary>
        public DodgeState() : this(Vector2.zero) { }

        /// <summary>
        /// Coded entry from <c>ChaseState</c> / <c>AttackState</c>, which have already probed
        /// the sidestep clear through <see cref="FSMDodge.SidestepHeading"/>.
        /// </summary>
        public DodgeState(Vector2 heading) => _requestedHeading = heading;

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;

            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            float speed = DodgeSpeed(fsm);
            float distance = FSMTuning.DodgeDistance(fsm);

            _duration = Mathf.Clamp(speed > 0.01f ? distance / speed : MaxDuration,
                                    MinDuration, MaxDuration);

            _heading = _requestedHeading != Vector2.zero
                ? _requestedHeading.normalized
                : HeadingFromTarget(fsm, c, distance);

            // Entered on an authored edge with a wall on both flanks: there is nowhere to
            // dodge to. Spend the cooldown anyway so the transition does not re-fire on the
            // very next tick, and let the caller's next Execute put the monster back to work.
            FSMDodge.MarkDecided(fsm);

            if (_heading == Vector2.zero)
            {
                c?.StopMovement();
                return;
            }

            c?.SetVelocity(_heading * speed);
            FaceMovement(c, _heading);
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

            if (_heading != Vector2.zero && _timer < _duration)
            {
                // Rewritten every frame rather than set once: a knockback, a root or a
                // collision resolves against the same Rigidbody2D, and a dodge that gave up
                // its velocity the first time anything touched it would end wherever it was
                // interrupted instead of out of the way.
                if (c != null && !c.IsRooted && !c.KnockbackActive)
                    c.SetVelocity(_heading * DodgeSpeed(fsm));
                return;
            }

            c?.StopMovement();

            // Back into the fight, at whatever range the sidestep left. Straight to the swing
            // when the dodge happened to end inside reach — routing everything through Chase
            // would spend a frame walking nowhere before it decided the same thing.
            if (IsTargetInMeleeRange(fsm, c))
            {
                fsm.ChangeState(new AttackState());
                return;
            }

            fsm.ChangeState(new ChaseState());
        }

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();
        }

        /// <summary>
        /// How fast the sidestep is, as a multiple of this monster's CHASE speed rather than
        /// its walk. A dodge is an urgent movement and every entity that has one already
        /// authors "how fast do I move when it matters" — deriving it from the walk would
        /// make a fast chaser dodge like it strolls.
        /// </summary>
        private static float DodgeSpeed(StateMachine fsm)
        {
            float chase = fsm.GetContextFloat("chasing_speed", 0f);
            if (chase <= 0f) chase = fsm.GetContextFloat("speed", 2f);
            return Mathf.Max(0.1f, chase * FSMTuning.DodgeSpeedMultiplier(fsm));
        }

        /// <summary>
        /// The fallback heading for an authored entry: perpendicular to the line to the
        /// target, which is the same corridor-leaving move the projectile answer makes and
        /// is right for the case that authored edge exists for — having just been hit by
        /// something whose direction nobody recorded.
        /// </summary>
        private static Vector2 HeadingFromTarget(StateMachine fsm, FSMComponents c, float distance)
        {
            var target = c != null && c.HasViableTarget(fsm) ? c.Target(fsm) : null;
            if (target == null || fsm.Owner == null) return Vector2.zero;

            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 toMe = myPos - (Vector2)target.transform.position;
            if (toMe.sqrMagnitude < 0.0001f) return Vector2.zero;

            return FSMDodge.SidestepHeading(myPos, toMe.normalized, distance);
        }

        private static bool IsTargetInMeleeRange(StateMachine fsm, FSMComponents c)
        {
            var target = c != null && c.HasViableTarget(fsm) ? c.Target(fsm) : null;
            if (target == null || fsm.Owner == null) return false;

            float meleeRange = fsm.GetContextFloat("melee_range", 1.5f);
            float sqr = ((Vector2)target.transform.position -
                         (Vector2)fsm.Owner.transform.position).sqrMagnitude;
            return sqr <= meleeRange * meleeRange;
        }

        /// <summary>
        /// Chase, not Walk: the burst is a run, and every entity that ships a chase sheet
        /// draws it as one. An entity with no chase art falls back through
        /// <c>EntityAnimationBinder</c>'s chain to its walk, so this is safe on any monster.
        /// </summary>
        private static void FaceMovement(FSMComponents c, Vector2 heading)
        {
            if (c?.Animator == null) return;
            var dir = c.Animator.ResolveDirectionFromVector(heading);
            c.Animator.SetState(DirectionalAnimator.AnimState.Chase, dir);
        }
    }
}
