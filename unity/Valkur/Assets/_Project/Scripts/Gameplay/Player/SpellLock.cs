using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>What a grimoire node is waiting on.</summary>
    public enum SpellLockKind
    {
        /// <summary>The node is broken — no asset, or no id to save it under.</summary>
        Malformed = 0,

        /// <summary>Already bought. Not a refusal the player can act on.</summary>
        AlreadyKnown = 1,

        /// <summary>The character is below <see cref="SpellLock.Required"/>.</summary>
        Level = 2,

        /// <summary>Another node in the school has to be bought first.</summary>
        Prerequisite = 3,

        /// <summary>Not enough arcane points.</summary>
        Points = 4,
    }

    /// <summary>
    /// One reason a spell cannot be learned, as DATA rather than as a sentence.
    ///
    /// <para>It carries the numbers because the view has to say them in Spanish and the console
    /// in English, and a pre-formatted string can only serve one of the two. It is also what
    /// lets the grimoire draw a level numeral on a locked socket without parsing its own
    /// message back out of a string.</para>
    ///
    /// <para>A struct, and returned in a caller-owned list, so asking "why is this locked" for
    /// all 71 nodes on every refresh allocates nothing.</para>
    /// </summary>
    public readonly struct SpellLock
    {
        public readonly SpellLockKind Kind;

        /// <summary>Level or point cost required. 0 for the other kinds.</summary>
        public readonly int Required;

        /// <summary>Level or points the character has. 0 for the other kinds.</summary>
        public readonly int Have;

        /// <summary>The unbought node, for <see cref="SpellLockKind.Prerequisite"/>.</summary>
        public readonly SpellNode Missing;

        /// <summary>
        /// True when the cost is the off-affinity surcharge rather than the node's own price.
        /// Kept separate from the number because "you need 2 points" and "you need 2 points
        /// BECAUSE this is not your school" are different things to tell a player, and the
        /// second one teaches a rule.
        /// </summary>
        public readonly bool OffAffinity;

        /// <summary>The school, for the off-affinity sentence. May be null.</summary>
        public readonly SpellTree School;

        private SpellLock(SpellLockKind kind, int required, int have,
                          SpellNode missing, bool offAffinity, SpellTree school)
        {
            Kind = kind;
            Required = required;
            Have = have;
            Missing = missing;
            OffAffinity = offAffinity;
            School = school;
        }

        public static SpellLock Malformed() =>
            new SpellLock(SpellLockKind.Malformed, 0, 0, null, false, null);

        public static SpellLock AlreadyKnown() =>
            new SpellLock(SpellLockKind.AlreadyKnown, 0, 0, null, false, null);

        public static SpellLock Level(int required, int have) =>
            new SpellLock(SpellLockKind.Level, required, have, null, false, null);

        public static SpellLock Prerequisite(SpellNode missing) =>
            new SpellLock(SpellLockKind.Prerequisite, 0, 0, missing, false, null);

        public static SpellLock Points(int required, int have, bool offAffinity, SpellTree school) =>
            new SpellLock(SpellLockKind.Points, required, have, null, offAffinity, school);

        /// <summary>
        /// The English one-liner, for the DevConsole and for <c>KnownSpells.CanLearn</c>'s
        /// legacy single-reason API. The player-facing Spanish lives in <c>GrimoireText</c>.
        /// </summary>
        public string Describe()
        {
            switch (Kind)
            {
                case SpellLockKind.AlreadyKnown: return "Already known.";
                case SpellLockKind.Level:        return $"Requires level {Required}.";
                case SpellLockKind.Prerequisite:
                    return $"Requires '{(Missing != null ? Missing.ResolveDisplayName() : "?")}'.";
                case SpellLockKind.Points:
                    return OffAffinity && School != null
                        ? $"Need {Required} arcane point(s) — {School.displayName} is not an affinity school."
                        : $"Need {Required} arcane point(s), have {Have}.";
                default: return "Malformed spell node.";
            }
        }
    }
}
