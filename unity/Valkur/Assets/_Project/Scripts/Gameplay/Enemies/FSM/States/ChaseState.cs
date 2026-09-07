using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Chase state: closes on the target through <see cref="FSMPathFollower"/> and hands over
    /// to <see cref="AttackState"/> when it is in reach. Honours the aggro-exit hysteresis and
    /// the leash, both authorable per monster through <see cref="FSMTuning"/>.
    ///
    /// <para>Two things it now does that it did not. It holds a STANDOFF when the monster
    /// declares a <c>desired_range</c> — every caster in the game used to walk into the
    /// player's face because closing to <c>melee_range</c> was unconditional, which also made
    /// <c>NPCAutoCast</c>'s per-entry <c>minDistance</c> gate refuse to fire the very spells
    /// the standoff exists for. And it MEASURES SIGHT: the last position it could actually see
    /// the target at is recorded every tick, and losing that line for longer than
    /// <c>sight_memory_seconds</c> sends it to <see cref="SearchState"/> rather than letting
    /// it track a target through a building forever.</para>
    /// </summary>
    public class ChaseState : IState
    {
        // Every feel knob this state used to hold as a private const now resolves through
        // FSMTuning, which owns the key AND the default. Read once per Enter/Execute rather
        // than cached in a field, because a live `reconfig` re-publishes the context and the
        // state should pick that up.

        private readonly FSMPathFollower _path = new FSMPathFollower();
        private float _blindTime;

        /// <summary>
        /// Half-width of the standoff band, as a fraction of the desired range. Inside it the
        /// monster holds position: without a band it would alternate advance/retreat every
        /// frame around the exact distance, which reads as a stutter rather than as aiming.
        /// </summary>
        private const float StandoffBand = 0.15f;

        public void Enter(StateMachine fsm)
        {
            _path.Reset();
            _blindTime = 0f;
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            // One question instead of four: no target, a dead one, or a player in spirit form
            // all mean the same thing here, and the answer is cached for the frame so the
            // three states that ask it do not each pay a GetComponent.
            if (c == null || !c.HasViableTarget(fsm))
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            // The one thing in this state that is not a question about DISTANCE. Asked before
            // the sight, leash and reach arithmetic because a sidestep overrides all of it and
            // none of those answers changes in the fifth of a second it takes. Costs nothing
            // on a monster that does not dodge: FSMDodge returns on its first line when
            // dodge_chance is unset, which is every monster shipped before it existed.
            if (FSMDodge.ShouldDodgeProjectile(fsm, out var dodgeHeading))
            {
                fsm.ChangeState(new DodgeState(dodgeHeading));
                return;
            }

            var target = c.Target(fsm);
            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 targetPos = target.transform.position;
            Vector2 delta = targetPos - myPos;
            float distSq = delta.sqrMagnitude;

            // Sight memory. Seeing them refreshes both the remembered position and the clock;
            // losing them starts it. The chase is NOT abandoned on the first blocked frame —
            // a pillar, a corner or another monster crossing the line would otherwise reset
            // the pursuit — it is abandoned when the target has been out of sight for as long
            // as this monster is willing to guess.
            if (FSMPerception.HasLineOfSight(fsm.Owner, target))
            {
                _blindTime = 0f;
                FSMTargetMemory.Remember(fsm, targetPos);
            }
            else
            {
                _blindTime += dt;
                if (_blindTime >= FSMTuning.SightMemorySeconds(fsm))
                {
                    fsm.ChangeState(new SearchState());
                    return;
                }
            }

            float meleeRange = fsm.GetContextFloat("melee_range", 1.5f);
            float desiredRange = FSMTuning.DesiredRange(fsm);
            bool inMelee = distSq <= meleeRange * meleeRange;

            // A melee monster in reach swings. A standoff monster in reach swings only when
            // it has nowhere to give ground: a caster cornered against a wall that refused to
            // fight would be a free kill, and it would look like the AI had hung.
            Vector2 retreat = Vector2.zero;
            if (inMelee)
            {
                if (desiredRange <= 0f)
                {
                    fsm.ChangeState(new AttackState());
                    return;
                }

                retreat = FSMRetreat.Heading(myPos, targetPos, RetreatProbe(fsm));
                if (retreat == Vector2.zero)
                {
                    fsm.ChangeState(new AttackState());
                    return;
                }
            }

            // Check aggro exit
            float aggroRange = fsm.GetContextFloat("aggro_range", 5f);
            float exitRange = aggroRange * FSMTuning.AggroExitHysteresis(fsm);
            if (distSq > exitRange * exitRange)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            // Leash. This class had documented "leash support" since it was written and never
            // had any: the only exit was player distance, so a monster would follow across the
            // whole map as long as the player stayed inside its aggro ring, and
            // barbol_gigante's ring is 30 units wide. Breaking off returns it to PatrolState,
            // whose waypoints are anchored at the spawn point, so it walks home on its own
            // without needing a new state class.
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

            // chasing_speed IS the chase speed. It used to be multiplied by a hidden 1.5 here
            // and in AlertChaseState, so every authored chasingSpeed in every monster asset
            // understated the real value by a third and the two states would drift apart the
            // moment one was edited. The assets were rebaselined (x1.5) when the multiplier
            // was removed, so behaviour is unchanged.
            float chaseSpeed = fsm.GetContextFloat("chasing_speed", 4.5f);

            // Where on the ring around the target this monster belongs, so six chasers
            // surround it instead of arriving down one bearing and stacking. Falls back to the
            // target's own position for a lone attacker, so a solo monster does not walk a
            // circle before engaging.
            Vector2 approach = EngagementRing.ApproachPoint(
                fsm.Owner, target, desiredRange > 0f ? desiredRange : meleeRange);

            Vector2 moveDir = ResolveMove(fsm, c, myPos, approach, delta,
                                          desiredRange, retreat, dt);

            c.SetVelocity(moveDir * chaseSpeed);

            // Drive the 8-direction animator each frame so the sprite faces where the monster
            // is going — or, while holding a standoff, where it is aiming. flipX would corrupt
            // directional sprites.
            if (c.Animator != null)
            {
                Vector2 look = moveDir.sqrMagnitude > 0.0001f ? moveDir : delta;
                if (look.sqrMagnitude > 0.0001f)
                {
                    var dir = c.Animator.ResolveDirectionFromVector(look);
                    c.Animator.SetState(
                        moveDir.sqrMagnitude > 0.0001f
                            ? DirectionalAnimator.AnimState.Chase
                            : DirectionalAnimator.AnimState.Idle,
                        dir);
                }
            }
        }

        /// <summary>
        /// Advance, hold or give ground.
        ///
        /// A monster with no <c>desired_range</c> (every melee monster, and the historical
        /// behaviour of all of them) always advances, and this collapses to the path follower.
        /// </summary>
        private Vector2 ResolveMove(StateMachine fsm, FSMComponents c,
                                    Vector2 myPos, Vector2 targetPos, Vector2 delta,
                                    float desiredRange, Vector2 retreat, float dt)
        {
            if (desiredRange <= 0f)
                return _path.Steer(fsm, myPos, targetPos, dt);

            float dist = delta.magnitude;
            float near = desiredRange * (1f - StandoffBand);
            float far  = desiredRange * (1f + StandoffBand);

            if (dist > far)
                return _path.Steer(fsm, myPos, targetPos, dt);

            if (dist < near)
            {
                // Reuse the heading already probed for the melee test when there is one, so a
                // frame does not pay for two fans.
                if (retreat == Vector2.zero)
                    retreat = FSMRetreat.Heading(myPos, targetPos, RetreatProbe(fsm));

                // Cornered inside the band and out of melee: stand and let the spells fly
                // rather than grinding into the wall.
                if (retreat == Vector2.zero) return Vector2.zero;

                // The path is stale the moment the monster walks backwards down it.
                _path.Reset();
                return retreat;
            }

            // In the band: hold still. Standing is what lets NPCAutoCast's distance gates pass
            // and what gives the player a target that is doing something legible.
            _path.Reset();
            return Vector2.zero;
        }

        /// <summary>
        /// How far ahead a retreat heading is probed for obstacles: about a second of travel,
        /// so the monster commits only to ground it can actually cross before it re-decides.
        /// </summary>
        private static float RetreatProbe(StateMachine fsm)
            => Mathf.Max(1f, fsm.GetContextFloat("chasing_speed", 4.5f));

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();

            // Hand the ring slot back. It would time out on its own in a couple of seconds, but
            // until it did, the monsters still engaged would fan around a place nobody is
            // standing — a gap in the circle exactly where the one that just died used to be.
            EngagementRing.Release(fsm.Owner, c != null ? c.Target(fsm) : null);
        }
    }
}
