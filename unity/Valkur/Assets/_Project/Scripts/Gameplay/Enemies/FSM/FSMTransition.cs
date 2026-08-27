using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// One authored edge of an FSM set, made executable.
    ///
    /// Until this existed the F12 graph editor was a drawing: <c>FSMRuntimeFactory</c>
    /// read exactly two things out of <c>sets.json</c> — the initial state name and the
    /// list of state ids — and the word <c>transitions</c> appeared nowhere in the
    /// runtime. A designer could wire Chase to Flee with a condition and a cooldown, save
    /// it, and get byte-identical gameplay.
    ///
    /// Authored transitions are <b>additive</b>. They are evaluated by
    /// <see cref="StateMachine.Update"/> BEFORE the current state's <c>Execute</c>, and
    /// the first one whose guard passes wins. If none fires, the state's own hard-coded
    /// exits run exactly as they always have — so a set with no transitions (which is
    /// every set shipped today) behaves identically to before.
    /// </summary>
    public sealed class FSMTransition
    {
        /// <summary>State class this edge leaves. <c>"*"</c> = any state (a global edge).</summary>
        public string From { get; }

        /// <summary>State class this edge enters.</summary>
        public string To { get; }

        /// <summary>Parsed guard. Null = unconditional.</summary>
        public FSMCondition Condition { get; }

        /// <summary>Higher priority is tested first. Ties keep authored order.</summary>
        public int Priority { get; }

        /// <summary>Seconds this edge must rest after firing. 0 = no cooldown.</summary>
        public float CooldownSeconds { get; }

        /// <summary>Raw guard text, kept for diagnostics.</summary>
        public string RawCondition { get; }

        public FSMTransition(string from, string to, FSMCondition condition,
                             int priority, float cooldownSeconds, string rawCondition)
        {
            From = from;
            To = to;
            Condition = condition;
            Priority = priority;
            CooldownSeconds = cooldownSeconds;
            RawCondition = rawCondition;
        }

        public bool IsGlobal => From == "*";

        public bool AppliesTo(string currentStateName)
            => IsGlobal || string.Equals(From, currentStateName, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Context keys for the spawn anchor, so the leash and the
    /// <c>distance_from_home</c> guard signal cannot drift apart on a typo.
    /// </summary>
    public static class FSMHomeAnchor
    {
        public const string KeyX = "home_x";
        public const string KeyY = "home_y";
    }

    /// <summary>
    /// A guard on an authored transition: a conjunction of comparisons over named runtime
    /// signals, e.g. <c>hp_pct &lt; 0.3</c> or
    /// <c>distance_to_player &gt; aggro_range &amp;&amp; state_time &gt; 2</c>.
    ///
    /// The grammar is deliberately tiny — a designer typing into a text field needs
    /// something they can hold in their head, and anything richer wants a real expression
    /// editor rather than a string. Clauses are ANDed with <c>&amp;&amp;</c>; each clause is
    /// <c>&lt;signal&gt; &lt;op&gt; &lt;value&gt;</c> with op in
    /// <c>&lt; &lt;= &gt; &gt;= == !=</c>.
    ///
    /// A term that is not a literal and not a built-in signal is looked up in the state
    /// machine's context, so every value <c>FSMMonsterBrain</c> publishes from the
    /// MonsterDefinition — <c>aggro_range</c>, <c>melee_range</c>, <c>speed</c>,
    /// <c>chasing_speed</c>, <c>attack_windup_s</c> — is usable on either side.
    /// </summary>
    public sealed class FSMCondition
    {
        // ── Built-in signals ────────────────────────────────────────────────────
        public const string HpPct            = "hp_pct";
        public const string DistanceToPlayer = "distance_to_player";
        public const string StateTime        = "state_time";
        public const string IsStunned        = "is_stunned";
        public const string HasTarget        = "has_target";
        public const string TimeSinceHit     = "time_since_hit";
        public const string DistanceFromHome = "distance_from_home";

        private enum Op { Less, LessEqual, Greater, GreaterEqual, Equal, NotEqual }

        private readonly struct Clause
        {
            public readonly string Left;
            public readonly Op Operator;
            public readonly string Right;

            public Clause(string left, Op op, string right)
            {
                Left = left;
                Operator = op;
                Right = right;
            }
        }

        private readonly Clause[] _clauses;

        private FSMCondition(Clause[] clauses) => _clauses = clauses;

        /// <summary>
        /// Parses a guard. Returns null for empty input (an unconditional edge) and null
        /// WITH <paramref name="error"/> set for malformed input — the caller logs it once
        /// at load rather than silently treating a typo as "always true", which would make
        /// a mistyped guard fire constantly instead of never.
        /// </summary>
        public static FSMCondition Parse(string text, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text)) return null;

            var parts = text.Split(new[] { "&&" }, System.StringSplitOptions.RemoveEmptyEntries);
            var clauses = new List<Clause>(parts.Length);

            foreach (var partRaw in parts)
            {
                string part = partRaw.Trim();
                if (part.Length == 0) continue;

                if (!TrySplitOperator(part, out string left, out Op op, out string right))
                {
                    error = $"clause '{part}' is not '<signal> <op> <value>' " +
                            "(operators: < <= > >= == !=)";
                    return null;
                }

                clauses.Add(new Clause(left, op, right));
            }

            if (clauses.Count == 0)
            {
                error = "no clauses parsed";
                return null;
            }
            return new FSMCondition(clauses.ToArray());
        }

        private static bool TrySplitOperator(string part, out string left, out Op op, out string right)
        {
            left = right = null;
            op = Op.Equal;

            // Two-character operators first: '<' would otherwise swallow '<='.
            string[] twoChar = { "<=", ">=", "==", "!=" };
            Op[] twoOps = { Op.LessEqual, Op.GreaterEqual, Op.Equal, Op.NotEqual };
            for (int i = 0; i < twoChar.Length; i++)
            {
                int idx = part.IndexOf(twoChar[i], System.StringComparison.Ordinal);
                if (idx <= 0) continue;
                left = part.Substring(0, idx).Trim();
                right = part.Substring(idx + 2).Trim();
                op = twoOps[i];
                return left.Length > 0 && right.Length > 0;
            }

            int lt = part.IndexOf('<');
            if (lt > 0)
            {
                left = part.Substring(0, lt).Trim();
                right = part.Substring(lt + 1).Trim();
                op = Op.Less;
                return left.Length > 0 && right.Length > 0;
            }

            int gt = part.IndexOf('>');
            if (gt > 0)
            {
                left = part.Substring(0, gt).Trim();
                right = part.Substring(gt + 1).Trim();
                op = Op.Greater;
                return left.Length > 0 && right.Length > 0;
            }

            return false;
        }

        public bool Evaluate(StateMachine fsm)
        {
            if (_clauses == null) return true;

            for (int i = 0; i < _clauses.Length; i++)
            {
                float l = ResolveTerm(fsm, _clauses[i].Left);
                float r = ResolveTerm(fsm, _clauses[i].Right);
                if (!Compare(l, _clauses[i].Operator, r)) return false;
            }
            return true;
        }

        private static bool Compare(float l, Op op, float r)
        {
            const float Epsilon = 0.0001f;
            switch (op)
            {
                case Op.Less:         return l <  r;
                case Op.LessEqual:    return l <= r;
                case Op.Greater:      return l >  r;
                case Op.GreaterEqual: return l >= r;
                case Op.Equal:        return Mathf.Abs(l - r) <= Epsilon;
                case Op.NotEqual:     return Mathf.Abs(l - r) >  Epsilon;
                default:              return false;
            }
        }

        /// <summary>
        /// Numbers and booleans resolve to themselves; built-in signals are measured from
        /// the live entity; anything else falls through to the FSM context, so authored
        /// MonsterDefinition values can appear on either side of a comparison.
        /// </summary>
        private static float ResolveTerm(StateMachine fsm, string term)
        {
            if (float.TryParse(term, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float literal))
                return literal;

            if (term == "true")  return 1f;
            if (term == "false") return 0f;

            switch (term)
            {
                case HpPct:            return ResolveHpPct(fsm);
                case DistanceToPlayer: return ResolveDistanceToPlayer(fsm);
                case StateTime:        return fsm.TimeInCurrentState;
                case IsStunned:        return ResolveComponents(fsm)?.IsStunned == true ? 1f : 0f;
                case HasTarget:        return ResolveHasTarget(fsm) ? 1f : 0f;
                case TimeSinceHit:     return fsm.TimeSinceLastHit;
                case DistanceFromHome: return ResolveDistanceFromHome(fsm);
            }

            return fsm.GetContextFloat(term, 0f);
        }

        private static FSMComponents ResolveComponents(StateMachine fsm)
            => fsm.GetContext<FSMComponents>(FSMComponents.KEY);

        private static float ResolveHpPct(StateMachine fsm)
        {
            var health = ResolveComponents(fsm)?.Health;
            if (health == null || health.MaxHp <= 0) return 1f;
            return Mathf.Clamp01(health.CurrentHp / (float)health.MaxHp);
        }

        private static float ResolveDistanceToPlayer(StateMachine fsm)
        {
            var player = EntityRegistry.Player;
            if (player == null || fsm.Owner == null) return float.MaxValue;
            return Vector2.Distance(fsm.Owner.transform.position, player.transform.position);
        }

        /// <summary>
        /// How far the entity has wandered from where it spawned. The anchor is published
        /// by <c>FSMMonsterBrain</c> as <c>home_x</c>/<c>home_y</c>; without it this
        /// returns 0 so a guard on it simply never fires rather than firing constantly.
        /// </summary>
        private static float ResolveDistanceFromHome(StateMachine fsm)
        {
            if (fsm.Owner == null) return 0f;
            if (!fsm.Context.ContainsKey(FSMHomeAnchor.KeyX)) return 0f;

            var home = new Vector2(fsm.GetContextFloat(FSMHomeAnchor.KeyX),
                                   fsm.GetContextFloat(FSMHomeAnchor.KeyY));
            return Vector2.Distance(fsm.Owner.transform.position, home);
        }

        private static bool ResolveHasTarget(StateMachine fsm)
        {
            var player = EntityRegistry.Player;
            if (player == null) return false;

            var health = player.GetComponent<Health>();
            if (health != null && health.IsDead) return false;

            // Spirit-form players are invisible to NPC perception — the same rule every
            // hand-written state applies.
            var spirit = player.GetComponent<PlayerSpiritState>();
            return spirit == null || !spirit.IsSpirit;
        }
    }
}
