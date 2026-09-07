using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// The DECISION half of dodging: may this monster sidestep right now, and which way.
    ///
    /// <para>Split from <see cref="DodgeState"/> (which executes the burst) and from
    /// <see cref="FSMThreatSense"/> (which only looks) so the three questions stay
    /// separable — but deliberately NOT holding the <c>ChangeState</c> call itself. That
    /// stays inside <c>ChaseState</c> and <c>AttackState</c>, because
    /// <c>FSMBuiltInTransitionRegistryTests</c> takes its census by reading the state class
    /// files, and a transition moved one level down is a coded edge no test can see. CLAUDE.md
    /// records the same lesson for <c>CastOriginContractTests</c>.</para>
    ///
    /// <para>THE ROLL IS SPENT WHETHER OR NOT IT PASSES, and that is the whole reason the
    /// behaviour reads as a decision instead of as a reflex. Sensing runs every frame, so a
    /// roll that only started the cooldown on success would re-roll sixty times a second and
    /// a 35 % chance would fire within two frames — i.e. every projectile would be dodged,
    /// and the dial would do nothing but change how MANY frames it took. Committing the
    /// cooldown on the decision makes a failed roll mean "this shot gets through", which is
    /// what the number is supposed to say.</para>
    ///
    /// <para>It is also gated on the SET: a machine whose allowed-state list has no
    /// <c>DodgeState</c> can never dodge, however this is tuned. That is the same structural
    /// guarantee the whitelist already gives vendors against ever chasing anybody — behaviour
    /// belongs to the authored set, not to a knob somebody could set on the wrong monster.</para>
    /// </summary>
    public static class FSMDodge
    {
        /// <summary>State class name, matched against the set's allowed-state whitelist.</summary>
        public const string StateName = "DodgeState";

        /// <summary>Runtime context key holding <c>Time.time</c> of the last dodge DECISION.</summary>
        public const string KeyLastDodgeTime = "last_dodge_time";

        /// <summary>
        /// True when this monster should break off what it is doing and sidestep, with
        /// <paramref name="heading"/> already probed clear.
        /// </summary>
        public static bool ShouldDodgeProjectile(StateMachine fsm, out Vector2 heading)
        {
            heading = Vector2.zero;
            if (fsm?.Owner == null) return false;

            float chance = FSMTuning.DodgeChance(fsm);
            if (chance <= 0f) return false;
            if (!fsm.IsStateAllowed(StateName)) return false;

            float cooldown = FSMTuning.DodgeCooldownSeconds(fsm);
            float last = fsm.GetContextFloat(KeyLastDodgeTime, float.NegativeInfinity);
            if (Time.time - last < cooldown) return false;

            float radius = FSMTuning.DodgeThreatRadius(fsm);
            if (!FSMThreatSense.TryFindIncomingProjectile(fsm.Owner, radius, out var travel))
                return false;

            // Spent here, before the roll is read: see the class remarks.
            MarkDecided(fsm);

            if (Random.value > chance) return false;

            heading = SidestepHeading(fsm.Owner.transform.position, travel,
                                      FSMTuning.DodgeDistance(fsm));
            return heading != Vector2.zero;
        }

        /// <summary>Record that a dodge decision was taken this instant.</summary>
        public static void MarkDecided(StateMachine fsm) => fsm?.SetContext(KeyLastDodgeTime, Time.time);

        /// <summary>
        /// Perpendicular to the shot, on whichever side is open.
        ///
        /// <para>Away-from-the-shooter would be the obvious heading and it is the wrong one:
        /// a projectile travels far faster than a monster runs, so backing up along the line
        /// of fire keeps the body inside the shot's own corridor and simply delays the hit.
        /// Leaving the corridor is the only thing that works, and it is also what a dodge
        /// LOOKS like.</para>
        ///
        /// <para>Both perpendiculars are probed with the same <see cref="LineOfSight"/> every
        /// other steering decision uses, so a monster with its back to a wall dives into the
        /// open side instead of into the wall. Returning zero when both are blocked is the
        /// caller's signal that there is nowhere to go — it stands and takes it, exactly as
        /// <see cref="FSMRetreat"/> lets a cornered monster turn and swing.</para>
        /// </summary>
        public static Vector2 SidestepHeading(Vector2 from, Vector2 travelDirection, float distance)
        {
            if (travelDirection == Vector2.zero) return Vector2.zero;
            if (distance <= 0f) distance = FSMTuning.DefaultDodgeDistance;

            travelDirection.Normalize();
            var left = new Vector2(-travelDirection.y, travelDirection.x);
            var right = -left;

            bool leftClear = LineOfSight.IsClear(from, from + left * distance);
            bool rightClear = LineOfSight.IsClear(from, from + right * distance);

            if (leftClear && rightClear) return Random.value < 0.5f ? left : right;
            if (leftClear) return left;
            if (rightClear) return right;
            return Vector2.zero;
        }
    }
}
