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
