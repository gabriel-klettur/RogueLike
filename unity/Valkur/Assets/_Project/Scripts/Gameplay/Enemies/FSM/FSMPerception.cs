using UnityEngine;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// The single answer to "can this entity perceive its enemy right now".
    ///
    /// <para>Acquisition used to be spelled out three times — <see cref="IdleState"/>,
    /// <see cref="PatrolState"/> and (as a negation) <see cref="ChaseState"/> — each with its
    /// own spelling of the same four checks, and the three had already drifted:
    /// <c>IdleState</c> asked <c>IsBlocked</c>, <c>PatrolState</c> asked <c>IsClear</c>, and
    /// only two of them tested whether the target was dead. Perception that lives in one
    /// place can also be EXTENDED in one place, which is what made a field of view and a
    /// sight memory a change of a few lines rather than a change to every hostile state.</para>
    ///
    /// <para><b>The rear arc is not a blind spot.</b> A cone that a monster cannot get out of
    /// makes being stabbed in the back unanswerable: the flinch resumes the state it
    /// interrupted, the authored alert edge only fires when the attacker is BEYOND aggro
    /// range, so a melee attacker standing behind a monster would be permanently
    /// unperceivable. <see cref="FovIgnoredAfterHitSeconds"/> drops the cone for a couple of
    /// seconds after any hit, which is exactly the "what was that?" turn a person makes.</para>
    /// </summary>
    public static class FSMPerception
    {
        /// <summary>Context key holding the <c>Time.time</c> before which acquisition is refused.</summary>
        public const string KeyNoAggroUntil = "no_aggro_until";

        /// <summary>
        /// A hit inside this window suspends the field-of-view test. Being shot tells you
        /// where the shooter is whether or not you were looking at them.
        /// </summary>
        public const float FovIgnoredAfterHitSeconds = 2f;

        /// <summary>
        /// True when this entity may not acquire a target yet — set by <see cref="FleeState"/>
        /// on its way out so a monster that just ran does not turn round and re-aggro on the
        /// very next tick.
        /// </summary>
        public static bool AggroSuppressed(StateMachine fsm)
            => fsm != null && Time.time < fsm.GetContextFloat(KeyNoAggroUntil, 0f);

        /// <summary>Refuse acquisition for <paramref name="seconds"/> from now.</summary>
        public static void SuppressAggro(StateMachine fsm, float seconds)
        {
            if (fsm == null || seconds <= 0f) return;
            fsm.SetContext(KeyNoAggroUntil, Time.time + seconds);
        }

        /// <summary>
        /// A target worth reacting to: alive, active, and not in spirit form. Spirit-form
        /// players are intangible, so treating them as targets makes a monster swing at
        /// something that cannot be hit and cannot hit back.
        /// </summary>
        public static bool IsPerceivable(GameObject target)
        {
            if (target == null || !target.activeInHierarchy) return false;

            var health = target.GetComponent<Health>();
            if (health != null && health.IsDead) return false;

            var spirit = target.GetComponent<PlayerSpiritState>();
            return spirit == null || !spirit.IsSpirit;
        }

        /// <summary>
        /// Acquisition: is there an enemy this entity should start chasing?
        ///
        /// Distance, then the cone, then geometry — cheapest test first, and the raycast
        /// last because it is the only one that touches the physics scene.
        /// </summary>
        public static bool TryAcquire(StateMachine fsm, out GameObject target)
        {
            target = null;
            if (fsm == null || fsm.Owner == null) return false;
            if (AggroSuppressed(fsm)) return false;

            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            var candidate = c != null ? c.Target(fsm) : FactionTargeting.EnemyOf(fsm.Owner);
            if (!IsPerceivable(candidate)) return false;

            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 theirPos = candidate.transform.position;

            float aggroRange = fsm.GetContextFloat("aggro_range", 5f);
            if ((theirPos - myPos).sqrMagnitude > aggroRange * aggroRange) return false;

            if (!InFieldOfView(fsm, c, myPos, theirPos)) return false;
            if (LineOfSight.IsBlocked(myPos, theirPos)) return false;

            target = candidate;
            return true;
        }

        /// <summary>
        /// Maintenance: can this entity still SEE a target it has already committed to?
        /// Deliberately has no distance term and no cone — a chase breaks off on distance
        /// (through the aggro hysteresis and the leash) and this answers only the geometry,
        /// which is what <see cref="ChaseState"/>'s sight memory measures.
        /// </summary>
        public static bool HasLineOfSight(GameObject seeker, GameObject target)
        {
            if (seeker == null || target == null) return false;
            return LineOfSight.IsClear(seeker.transform.position, target.transform.position);
        }

        /// <summary>
        /// The cone test. A <c>fov_degrees</c> of 360 (the default) passes unconditionally,
        /// and so does any test inside <see cref="FovIgnoredAfterHitSeconds"/> of a hit.
        /// </summary>
        public static bool InFieldOfView(StateMachine fsm, FSMComponents c,
                                         Vector2 myPos, Vector2 theirPos)
        {
            float fov = FSMTuning.FovDegrees(fsm);
            if (fov >= 360f) return true;
            if (fsm.TimeSinceLastHit < FovIgnoredAfterHitSeconds) return true;

            Vector2 toTarget = theirPos - myPos;
            if (toTarget.sqrMagnitude < 0.0001f) return true;

            Vector2 facing = FacingOf(c);
            if (facing.sqrMagnitude < 0.0001f) return true;

            return Vector2.Angle(facing, toTarget) <= fov * 0.5f;
        }

        /// <summary>
        /// Which way this entity is looking. The animator's direction is the authority —
        /// it is what the player SEES the monster facing, and a cone derived from anything
        /// else would refuse a target the sprite is staring at. Falls back to the body's
        /// velocity, then to east, so an entity with no animator keeps a defined facing
        /// rather than an all-refusing zero vector.
        /// </summary>
        public static Vector2 FacingOf(FSMComponents c)
        {
            if (c?.Animator != null) return DirectionVector(c.Animator.CurrentDirection);
            if (c?.Rb != null && c.Rb.velocity.sqrMagnitude > 0.0001f) return c.Rb.velocity.normalized;
            return Vector2.right;
        }

        private static readonly float Diag = Mathf.Sqrt(0.5f);

        /// <summary>Unit vector for one of the animator's eight facings.</summary>
        public static Vector2 DirectionVector(DirectionalAnimator.Direction direction)
        {
            switch (direction)
            {
                case DirectionalAnimator.Direction.South:     return new Vector2(0f, -1f);
                case DirectionalAnimator.Direction.SouthEast: return new Vector2(Diag, -Diag);
                case DirectionalAnimator.Direction.East:      return new Vector2(1f, 0f);
                case DirectionalAnimator.Direction.NorthEast: return new Vector2(Diag, Diag);
                case DirectionalAnimator.Direction.North:     return new Vector2(0f, 1f);
                case DirectionalAnimator.Direction.NorthWest: return new Vector2(-Diag, Diag);
                case DirectionalAnimator.Direction.West:      return new Vector2(-1f, 0f);
                case DirectionalAnimator.Direction.SouthWest: return new Vector2(-Diag, -Diag);
                default:                                      return new Vector2(0f, -1f);
            }
        }
    }

    /// <summary>
    /// Where the target was last actually SEEN, and when.
    ///
    /// <para>Kept in the machine's context beside the spawn anchor
    /// (<c>FSMHomeAnchor</c>) and for the same reason: it is a fact about this entity that
    /// outlives the state that recorded it. <see cref="ChaseState"/> writes it on every tick
    /// it has line of sight; <see cref="SearchState"/> is the only reader, and reading it is
    /// the entire difference between a monster that loses you and a monster that gives up
    /// the instant a wall interrupts the raycast.</para>
    ///
    /// <para>A monster with no memory has only two behaviours available on losing sight —
    /// keep chasing a position it cannot justify knowing, which is what shipped, or forget
    /// instantly — and both read as the AI not working.</para>
    /// </summary>
    public static class FSMTargetMemory
    {
        public const string KeyX    = "lkp_x";
        public const string KeyY    = "lkp_y";
        public const string KeyTime = "lkp_time";

        /// <summary>Record a sighting at <paramref name="position"/>.</summary>
        public static void Remember(StateMachine fsm, Vector2 position)
        {
            if (fsm == null) return;
            fsm.SetContext(KeyX, position.x);
            fsm.SetContext(KeyY, position.y);
            fsm.SetContext(KeyTime, Time.time);
        }

        /// <summary>True when a sighting has ever been recorded on this machine.</summary>
        public static bool Has(StateMachine fsm)
            => fsm != null && fsm.Context.ContainsKey(KeyTime);

        /// <summary>The remembered position. Falls back to the owner's own position, so a
        /// search with no memory investigates where it stands rather than the world origin.</summary>
        public static Vector2 Position(StateMachine fsm)
        {
            if (!Has(fsm)) return fsm != null && fsm.Owner != null
                ? (Vector2)fsm.Owner.transform.position
                : Vector2.zero;
            return new Vector2(fsm.GetContextFloat(KeyX), fsm.GetContextFloat(KeyY));
        }

        /// <summary>Seconds since the last sighting, or a large number when there is none.</summary>
        public static float Age(StateMachine fsm)
            => Has(fsm) ? Time.time - fsm.GetContextFloat(KeyTime, 0f) : float.MaxValue;
    }
}
