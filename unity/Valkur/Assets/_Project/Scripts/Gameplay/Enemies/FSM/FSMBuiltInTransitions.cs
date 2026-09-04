using System.Collections.Generic;
using Valkur.Core;

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>
    /// One edge of the monster state machine that lives in C#, not in
    /// <c>StreamingAssets/FSM/sets.json</c>.
    ///
    /// These are NOT authored, NOT editable and NOT removable from F12 — they are a
    /// read-only picture of what the state classes already do. The registry exists so the
    /// graph can show them; nothing here is consulted at runtime.
    /// </summary>
    public readonly struct FSMBuiltInEdge
    {
        /// <summary>Source state class name, or <see cref="FSMBuiltInTransitions.AnyState"/>
        /// for an edge the machine can take from anywhere.</summary>
        public readonly string From;

        /// <summary>Destination state class name.</summary>
        public readonly string To;

        /// <summary>Short label drawn on the edge. Keep it under ~28 characters.</summary>
        public readonly string Label;

        /// <summary>The full condition, for the properties panel.</summary>
        public readonly string Detail;

        /// <summary>Repo-relative file that owns this edge. The sync test reads it.</summary>
        public readonly string SourceFile;

        /// <summary>
        /// True when the destination is decided at runtime rather than written as
        /// <c>new SomeState()</c> — only <c>DamageState</c>'s resume path. The sync test
        /// cannot see these in the source text, so it exempts them from the census match
        /// instead of reporting the registry as out of date forever.
        /// </summary>
        public readonly bool DynamicTarget;

        public FSMBuiltInEdge(string from, string to, string label, string detail,
                              string sourceFile, bool dynamicTarget = false)
        {
            From          = from;
            To            = to;
            Label         = label;
            Detail        = detail;
            SourceFile    = sourceFile;
            DynamicTarget = dynamicTarget;
        }

        public string Key => From + ">" + To;
    }

    /// <summary>
    /// The transitions the FSM actually takes that no designer authored.
    ///
    /// WHY THIS EXISTS. Monster behaviour is split across two owners: a handful of edges
    /// authored into <c>sets.json</c> through F12, and the rest written as
    /// <c>fsm.ChangeState(new SomeState())</c> inside the state classes. Only the authored
    /// half was ever drawn, so the graph showed three edges of a machine that has more than
    /// twenty — and the two halves do not even overlap: the coded half owns every edge and
    /// cannot reach <c>FleeState</c> or <c>AlertChaseState</c> at all (grep: zero
    /// <c>new FleeState(</c> sites), while the authored half is the only way into those two
    /// states and describes none of the rest. A designer opening F12 was not reading a
    /// summary of the behaviour; they were reading the list of exceptions bolted on top of a
    /// machine they could not see.
    ///
    /// This registry is the missing half, declared. <c>FSMRuntimeEditor</c> draws it dimmed
    /// and locked beneath the authored edges, so the graph shows the whole machine and says
    /// which parts answer to the editor.
    ///
    /// KEEPING IT HONEST. A hand-written mirror of code rots the moment someone adds a
    /// <c>ChangeState</c> call. <c>FSMBuiltInTransitionRegistryTests</c> scans the state
    /// classes for every <c>ChangeState(new X())</c> and fails when the census and this
    /// table disagree in either direction — a new coded edge that nobody declared, or a
    /// declared edge whose code was deleted. That test is what makes this table a fact
    /// rather than a comment.
    /// </summary>
    public static class FSMBuiltInTransitions
    {
        /// <summary>Wildcard source, matching the graph's existing "Any State" pseudo-node.</summary>
        public const string AnyState = "*";

        private const string DirStates = "unity/Valkur/Assets/_Project/Scripts/Gameplay/Enemies/FSM/States/";
        private const string FileMachine = "unity/Valkur/Assets/_Project/Scripts/Gameplay/Enemies/FSM/StateMachine.cs";
        private const string FileAutoCast = "unity/Valkur/Assets/_Project/Scripts/Gameplay/Enemies/NPCAutoCast.cs";

        [SelfHealingStatic("Immutable declaration table of readonly structs holding only " +
                           "strings. Nothing writes to it after init and it references no " +
                           "Unity object, so it cannot carry a destroyed reference or a stale " +
                           "registration into the next Play session.")]
        private static readonly FSMBuiltInEdge[] _all =
        {
            // ── Machine-level: raised by the event queue, from whatever state is current ──
            new FSMBuiltInEdge(AnyState, "DamageState", "hit + flinch roll",
                "An OnHit event whose flinch roll passes (stats.damageStopProbability) interrupts " +
                "the current state for stats.damageDuration seconds. Events are processed at the " +
                "top of Update, before authored transitions and before the state's own Execute, " +
                "so a flinch beats everything. DamageState does not need to be in the set's " +
                "allowed-state list: ChangeState exempts Damage, Death and Unconscious from the guard.",
                FileMachine),

            new FSMBuiltInEdge(AnyState, "UnconsciousState", "hp reached 0",
                "Raised by the death event. Every state also re-checks Health.IsDead at the top of " +
                "its own Execute and goes here directly, so this edge exists nine times over.",
                FileMachine),

            new FSMBuiltInEdge(AnyState, "NPCCastState", "auto-cast picked a spell",
                "NPCAutoCast drives this, not the FSM. It is only attached when the monster's " +
                "MonsterDefinition sets autoCast — EntitySetup.ConfigureMonsterAutoCast returns " +
                "before adding the component otherwise, so on a melee-only monster this edge can " +
                "never fire however the graph is authored.",
                FileAutoCast),

            // ── IdleState ────────────────────────────────────────────────────────────────
            new FSMBuiltInEdge("IdleState", "UnconsciousState", "dead",
                "Health.IsDead, checked first thing in Execute.", DirStates + "IdleState.cs"),

            new FSMBuiltInEdge("IdleState", "ChaseState", "player in range + in sight",
                "distance <= aggro_range AND LineOfSight is clear. Sight is checked on ACQUISITION " +
                "only; ChaseState keeps a distance-based exit so a committed monster does not give " +
                "up the instant you round a corner. A player in spirit form is invisible here.",
                DirStates + "IdleState.cs"),

            // ── StrollState ───────────────────────────────────────────────────────────────
            // Its ONLY coded edge. A stroller never acquires a target, which is what makes
            // it peaceful without help from the set's allowed-state whitelist.
            new FSMBuiltInEdge("StrollState", "UnconsciousState", "dead",
                "Health.IsDead, checked first thing in Execute.", DirStates + "StrollState.cs"),

            // ── PatrolState ──────────────────────────────────────────────────────────────
            new FSMBuiltInEdge("PatrolState", "UnconsciousState", "dead",
                "Health.IsDead, checked first thing in Execute.", DirStates + "PatrolState.cs"),

            new FSMBuiltInEdge("PatrolState", "ChaseState", "player in range + in sight",
                "The same acquisition rule as IdleState. This is the edge that re-aggros after " +
                "every de-aggro, because every hard-coded exit in the machine targets PatrolState.",
                DirStates + "PatrolState.cs"),

            // ── ChaseState ───────────────────────────────────────────────────────────────
            new FSMBuiltInEdge("ChaseState", "UnconsciousState", "dead",
                "Health.IsDead, checked first thing in Execute.", DirStates + "ChaseState.cs"),

            new FSMBuiltInEdge("ChaseState", "AttackState", "within melee range",
                "distance <= melee_range. Tested BEFORE both range exits, so a monster that is " +
                "simultaneously in melee range and past its leash attacks rather than going home.",
                DirStates + "ChaseState.cs"),

            new FSMBuiltInEdge("ChaseState", "PatrolState", "lost target / de-aggro / leash",
                "Five independent reasons collapse onto this one edge: no player at all, player " +
                "dead, player in spirit form, distance > aggro_range x aggroExitHysteresis, or " +
                "distance from the spawn anchor > leashRange.",
                DirStates + "ChaseState.cs"),

            // ── AlertChaseState ──────────────────────────────────────────────────────────
            new FSMBuiltInEdge("AlertChaseState", "UnconsciousState", "dead",
                "Health.IsDead, checked first thing in Execute.", DirStates + "AlertChaseState.cs"),

            new FSMBuiltInEdge("AlertChaseState", "AttackState", "within melee range",
                "The same test ChaseState carries, and for the same reason. Until this edge " +
                "existed an alerted monster closed to zero distance and stood there for the " +
                "whole alert window, which made t_any_alert — the highest-priority " +
                "authored edge in the shipped data — lead to a state that could not fight.",
                DirStates + "AlertChaseState.cs"),

            new FSMBuiltInEdge("AlertChaseState", "PatrolState", "alert expired / lost target",
                "alertDuration elapsed, or no player, or player dead, or player in spirit form. " +
                "Reaching the player exits through AttackState instead, so the alert window is " +
                "a deadline for making contact rather than a window of guaranteed passivity.",
                DirStates + "AlertChaseState.cs"),

            // ── AttackState ──────────────────────────────────────────────────────────────
            new FSMBuiltInEdge("AttackState", "UnconsciousState", "dead",
                "Health.IsDead, checked first thing in Execute.", DirStates + "AttackState.cs"),

            new FSMBuiltInEdge("AttackState", "PatrolState", "player went spirit",
                "The only non-death, non-range exit. A spirit-form player is unperceivable, so the " +
                "swing is abandoned outright rather than tracked.",
                DirStates + "AttackState.cs"),

            new FSMBuiltInEdge("AttackState", "ChaseState", "swing over, target out of reach",
                "At the end of a swing the target is re-measured against " +
                "melee_range x reswingRangeFactor. Inside it the state re-swings in place through " +
                "BeginSwing without leaving; outside it, this edge fires.",
                DirStates + "AttackState.cs"),

            // ── FleeState ────────────────────────────────────────────────────────────────
            new FSMBuiltInEdge("FleeState", "UnconsciousState", "dead",
                "Health.IsDead, checked first thing in Execute.", DirStates + "FleeState.cs"),

            new FSMBuiltInEdge("FleeState", "PatrolState", "flee timer expired",
                "fleeDuration elapsed, or no player to flee from. PatrolState re-aggros on the very " +
                "next frame if the player is still in range and in sight.",
                DirStates + "FleeState.cs"),

            // ── DamageState ──────────────────────────────────────────────────────────────
            new FSMBuiltInEdge("DamageState", AnyState, "resume interrupted state",
                "The flinch remembers which state it interrupted by CLASS NAME and rebuilds a fresh " +
                "instance of it, because a state carries per-visit data (a waypoint list, a swing " +
                "timer) and re-entering the old object would replay a half-finished swing.",
                DirStates + "DamageState.cs", dynamicTarget: true),

            new FSMBuiltInEdge("DamageState", "ChaseState", "flinch over, nothing to resume",
                "The fallback when no interrupted state was recorded.",
                DirStates + "DamageState.cs"),

            // ── UnconsciousState ─────────────────────────────────────────────────────────
            new FSMBuiltInEdge("UnconsciousState", "DeathState", "corpse timer expired",
                "stats.deathDisappearTime elapsed. This is the ONLY construction of DeathState " +
                "anywhere; no authored transition targets it.",
                DirStates + "UnconsciousState.cs"),

            // ── NPCCastState ─────────────────────────────────────────────────────────────
            new FSMBuiltInEdge("NPCCastState", "UnconsciousState", "dead",
                "Health.IsDead, checked first thing in Execute.", DirStates + "NPCCastState.cs"),

            new FSMBuiltInEdge("NPCCastState", "AttackState", "cast done, target in melee",
                "distance <= melee_range when the cast finishes.", DirStates + "NPCCastState.cs"),

            new FSMBuiltInEdge("NPCCastState", "ChaseState", "cast done, target out of melee",
                "The other half of the same test, and the path taken when there is no player.",
                DirStates + "NPCCastState.cs"),
        };

        /// <summary>Every code-owned edge, in declaration order.</summary>
        public static IReadOnlyList<FSMBuiltInEdge> All => _all;

        /// <summary>
        /// The edges worth drawing for a set that declares <paramref name="stateIds"/>.
        /// An edge whose endpoints are not both present is skipped — a set that omits
        /// NPCCastState should not grow a dangling arrow to it.
        /// <see cref="AnyState"/> always counts as present: the graph has a permanent
        /// "Any State" pseudo-node.
        /// </summary>
        public static List<FSMBuiltInEdge> ForStates(ICollection<string> stateIds)
        {
            var result = new List<FSMBuiltInEdge>();
            if (stateIds == null) return result;

            foreach (var edge in _all)
            {
                bool fromOk = edge.From == AnyState || stateIds.Contains(edge.From);
                bool toOk   = edge.To   == AnyState || stateIds.Contains(edge.To);
                if (fromOk && toOk) result.Add(edge);
            }
            return result;
        }
    }
}
