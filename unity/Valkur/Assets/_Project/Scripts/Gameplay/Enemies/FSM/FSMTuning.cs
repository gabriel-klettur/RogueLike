namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// The single owner of every FSM feel knob: its context key, its default, and the one
    /// accessor the states call.
    ///
    /// Before this, each state carried its own <c>private const float</c>. Eleven of them,
    /// with <c>REPATH_INTERVAL</c> and <c>WAYPOINT_REACH_DIST</c> written out twice —
    /// identically today, and free to drift the moment anyone edited one and not the other.
    /// A designer could not reach any of them: tuning how a monster feels meant editing C#.
    ///
    /// A value reaches the context from <c>MonsterDefinition.aiTuning</c> via
    /// <c>FSMMonsterBrain</c>, and only when the author set it — an unset knob publishes
    /// nothing, so the default below is what runs. That is what keeps every shipped monster
    /// behaving exactly as it did.
    /// </summary>
    public static class FSMTuning
    {
        // ── Context keys ────────────────────────────────────────────────────────
        public const string KeyAggroExitHysteresis  = "aggro_exit_hysteresis";
        public const string KeyLeashRange           = "leash_range";
        public const string KeyRepathInterval       = "repath_interval";
        public const string KeyWaypointReachDist    = "waypoint_reach_distance";
        public const string KeyAlertDuration        = "alert_duration";
        public const string KeyFleeDuration         = "flee_duration";
        public const string KeyFleeSpeedMultiplier  = "flee_speed_multiplier";
        public const string KeyReswingRangeFactor   = "reswing_range_factor";
        public const string KeyDesiredRange         = "desired_range";
        public const string KeyFovDegrees           = "fov_degrees";
        public const string KeySightMemory          = "sight_memory_seconds";
        public const string KeySearchDuration       = "search_duration";
        public const string KeyAggroShareRadius     = "aggro_share_radius";
        public const string KeyRegroupSeconds       = "regroup_seconds";
        public const string KeyDodgeChance          = "dodge_chance";
        public const string KeyDodgeCooldownSeconds = "dodge_cooldown_seconds";
        public const string KeyDodgeThreatRadius    = "dodge_threat_radius";
        public const string KeyDodgeDistance        = "dodge_distance";
        public const string KeyDodgeSpeedMultiplier = "dodge_speed_multiplier";

        // ── Defaults ────────────────────────────────────────────────────────────
        //
        // Each is the exact constant the state class used to hold, so this refactor is
        // behaviour-preserving by construction. Changing one of these numbers changes every
        // monster that has not authored an override — which is the point.

        /// <summary>Chase breaks off past aggro_range x this. Stops edge-of-ring oscillation.</summary>
        public const float DefaultAggroExitHysteresis = 1.15f;

        /// <summary>Leash as a multiple of aggro range when none is authored. Deliberately
        /// generous: this is the guard that stops a monster crossing the map, not a tether.</summary>
        public const float DefaultLeashRangeFactor = 3f;

        /// <summary>Seconds between A* repaths while chasing.</summary>
        public const float DefaultRepathInterval = 0.5f;

        /// <summary>World units that count as having arrived at a waypoint.</summary>
        public const float DefaultWaypointReachDistance = 0.25f;

        /// <summary>Seconds an alerted monster investigates before returning to patrol.</summary>
        public const float DefaultAlertDuration = 5f;

        /// <summary>Seconds a fleeing monster runs before turning back.</summary>
        public const float DefaultFleeDuration = 3f;

        /// <summary>Flee speed as a multiple of walk speed. There is no authored fleeSpeed.</summary>
        public const float DefaultFleeSpeedMultiplier = 1.5f;

        /// <summary>Re-swing while the target is within melee_range x this.</summary>
        public const float DefaultReswingRangeFactor = 1.5f;

        /// <summary>
        /// The distance a monster WANTS between itself and its target. Zero means "melee",
        /// which is the historical behaviour and what every melee monster keeps: close all
        /// the way and swing. A positive value makes the monster hold a standoff band and
        /// back out of it when crowded, which is the whole difference between a caster and
        /// a melee monster wearing a robe — before it existed, <c>ChaseState</c> closed to
        /// <c>melee_range</c> unconditionally, so a caster walked into the player's face and
        /// cast from there while its own <c>NPCAutoCast.minDistance</c> gate refused to fire.
        /// </summary>
        public const float DefaultDesiredRange = 0f;

        /// <summary>
        /// Field of view in degrees, centred on the facing. 360 is the historical
        /// behaviour — omniscient in every direction — and is the default so a monster that
        /// nobody has authored a cone for cannot silently go blind. See
        /// <c>FSMPerception</c> for the rear-arc exemption that keeps a monster stabbed in
        /// the back from being unable to turn around.
        /// </summary>
        public const float DefaultFovDegrees = 360f;

        /// <summary>Seconds a chaser keeps going after losing sight of its target before it
        /// falls back to searching the last place it saw them.</summary>
        public const float DefaultSightMemorySeconds = 3f;

        /// <summary>Seconds spent looking around at the last known position before giving up.</summary>
        public const float DefaultSearchDuration = 6f;

        /// <summary>
        /// World units within which acquiring a target alerts other monsters. Zero disables
        /// sharing entirely. A pack that does not share is twenty monsters that happen to
        /// agree; this is the cheapest thing that makes them read as a pack.
        /// </summary>
        public const float DefaultAggroShareRadius = 8f;

        /// <summary>
        /// Seconds after a flee ends during which the monster refuses to re-acquire. Without
        /// it <c>FleeState</c> hands control to <c>PatrolState</c>, which re-aggros on the
        /// very next tick, and a monster below its flee threshold oscillates on the authored
        /// transition cooldown forever.
        /// </summary>
        public const float DefaultRegroupSeconds = 2.5f;

        /// <summary>
        /// Probability that an inbound projectile is answered with a sidestep. ZERO is the
        /// default and it is what makes this whole layer invisible to every monster shipped
        /// before it: an unset knob publishes nothing, <c>FSMDodge</c> returns on its first
        /// line, and the sweep that would look for a projectile never runs. Dodging is opt-in
        /// per monster, twice over — this number AND a set whose allowed-state list declares
        /// DodgeState.
        /// </summary>
        public const float DefaultDodgeChance = 0f;

        /// <summary>
        /// Seconds between dodge DECISIONS — spent whether the roll passed or failed, which
        /// is what stops a per-frame sense turning any non-zero chance into certainty. It is
        /// also the player's counterplay: a second shot inside this window cannot be dodged,
        /// so baiting one out is a real answer to an evasive enemy.
        /// </summary>
        public const float DefaultDodgeCooldownSeconds = 1.6f;

        /// <summary>
        /// How far out a projectile is noticed. Wide enough to leave time to move at the
        /// speeds this project's bolts travel; narrow enough that a monster is not reacting
        /// to a fight happening somewhere else.
        /// </summary>
        public const float DefaultDodgeThreatRadius = 6f;

        /// <summary>World units the sidestep covers. The one number an author tunes by eye.</summary>
        public const float DefaultDodgeDistance = 2.2f;

        /// <summary>Sidestep speed as a multiple of chase speed. A dodge is a burst, not a walk.</summary>
        public const float DefaultDodgeSpeedMultiplier = 2.2f;

        // ── Accessors ───────────────────────────────────────────────────────────

        public static float AggroExitHysteresis(StateMachine fsm)
            => fsm.GetContextFloat(KeyAggroExitHysteresis, DefaultAggroExitHysteresis);

        public static float RepathInterval(StateMachine fsm)
            => fsm.GetContextFloat(KeyRepathInterval, DefaultRepathInterval);

        public static float WaypointReachDistance(StateMachine fsm)
            => fsm.GetContextFloat(KeyWaypointReachDist, DefaultWaypointReachDistance);

        public static float AlertDuration(StateMachine fsm)
            => fsm.GetContextFloat(KeyAlertDuration, DefaultAlertDuration);

        public static float FleeDuration(StateMachine fsm)
            => fsm.GetContextFloat(KeyFleeDuration, DefaultFleeDuration);

        public static float FleeSpeedMultiplier(StateMachine fsm)
            => fsm.GetContextFloat(KeyFleeSpeedMultiplier, DefaultFleeSpeedMultiplier);

        public static float ReswingRangeFactor(StateMachine fsm)
            => fsm.GetContextFloat(KeyReswingRangeFactor, DefaultReswingRangeFactor);

        public static float DesiredRange(StateMachine fsm)
            => fsm.GetContextFloat(KeyDesiredRange, DefaultDesiredRange);

        public static float FovDegrees(StateMachine fsm)
            => fsm.GetContextFloat(KeyFovDegrees, DefaultFovDegrees);

        public static float SightMemorySeconds(StateMachine fsm)
            => fsm.GetContextFloat(KeySightMemory, DefaultSightMemorySeconds);

        public static float SearchDuration(StateMachine fsm)
            => fsm.GetContextFloat(KeySearchDuration, DefaultSearchDuration);

        public static float AggroShareRadius(StateMachine fsm)
            => fsm.GetContextFloat(KeyAggroShareRadius, DefaultAggroShareRadius);

        public static float RegroupSeconds(StateMachine fsm)
            => fsm.GetContextFloat(KeyRegroupSeconds, DefaultRegroupSeconds);

        public static float DodgeChance(StateMachine fsm)
            => fsm.GetContextFloat(KeyDodgeChance, DefaultDodgeChance);

        public static float DodgeCooldownSeconds(StateMachine fsm)
            => fsm.GetContextFloat(KeyDodgeCooldownSeconds, DefaultDodgeCooldownSeconds);

        public static float DodgeThreatRadius(StateMachine fsm)
            => fsm.GetContextFloat(KeyDodgeThreatRadius, DefaultDodgeThreatRadius);

        public static float DodgeDistance(StateMachine fsm)
            => fsm.GetContextFloat(KeyDodgeDistance, DefaultDodgeDistance);

        public static float DodgeSpeedMultiplier(StateMachine fsm)
            => fsm.GetContextFloat(KeyDodgeSpeedMultiplier, DefaultDodgeSpeedMultiplier);

        /// <summary>
        /// Leash distance in world units. An authored <c>leashRange</c> wins; otherwise it
        /// is derived from this monster's own aggro range, so a wide-ranging monster gets a
        /// correspondingly long tether without anyone authoring two numbers that have to
        /// agree.
        /// </summary>
        public static float LeashRange(StateMachine fsm, float aggroRange)
        {
            float authored = fsm.GetContextFloat(KeyLeashRange, 0f);
            return authored > 0f ? authored : aggroRange * DefaultLeashRangeFactor;
        }
    }
}
