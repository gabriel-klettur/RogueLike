using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>What a talent node is waiting on.</summary>
    public enum SkillLockKind
    {
        /// <summary>The node is broken — no asset, or no id to save it under.</summary>
        Malformed = 0,

        /// <summary>Every rank is bought. Not a refusal the player can act on.</summary>
        Maxed = 1,

        /// <summary>The character is below <see cref="SkillLock.Required"/>.</summary>
        Level = 2,

        /// <summary>Another node has to reach its MAX rank first.</summary>
        Prerequisite = 3,

        /// <summary>Not enough skill points.</summary>
        Points = 4,
    }

    /// <summary>
    /// One reason a talent rank cannot be bought, as DATA rather than as a sentence.
    ///
    /// <para>The twin of <see cref="SpellLock"/>, and deliberately the same shape: the two
    /// panels are tabs of one window, and a player who learns what a locked socket means in
    /// the grimoire must not have to learn it again in the talents.</para>
    ///
    /// <para>It carries the numbers because the view says them in Spanish and the console in
    /// English, and a pre-formatted string can only serve one of the two. It is also what lets
    /// the board draw a level numeral on a locked node, and underline the exact prerequisite
    /// that is missing, without parsing its own message back out of a string.</para>
    ///
    /// <para>A struct, returned into a caller-owned list, so asking "why is this locked" for
    /// every node on every refresh allocates nothing.</para>
    /// </summary>
    public readonly struct SkillLock
    {
        public readonly SkillLockKind Kind;

        /// <summary>Level or point cost required. 0 for the other kinds.</summary>
        public readonly int Required;

        /// <summary>Level or points the character has. 0 for the other kinds.</summary>
        public readonly int Have;

        /// <summary>The unfinished node, for <see cref="SkillLockKind.Prerequisite"/>.</summary>
        public readonly SkillNode Missing;

        /// <summary>
        /// Ranks still owed on <see cref="Missing"/>. The board draws it because "finish
        /// Stoneflesh" and "finish Stoneflesh, three ranks to go" are different amounts of
        /// commitment and the player is deciding whether to make it.
        /// </summary>
        public readonly int MissingRanksLeft;

        private SkillLock(SkillLockKind kind, int required, int have,
                          SkillNode missing, int missingRanksLeft)
        {
            Kind = kind;
            Required = required;
            Have = have;
            Missing = missing;
            MissingRanksLeft = missingRanksLeft;
        }

        public static SkillLock Malformed() =>
            new SkillLock(SkillLockKind.Malformed, 0, 0, null, 0);

        public static SkillLock Maxed(int rank) =>
            new SkillLock(SkillLockKind.Maxed, rank, rank, null, 0);

        public static SkillLock Level(int required, int have) =>
            new SkillLock(SkillLockKind.Level, required, have, null, 0);

        public static SkillLock Prerequisite(SkillNode missing, int ranksLeft) =>
            new SkillLock(SkillLockKind.Prerequisite, 0, 0, missing, ranksLeft);

        public static SkillLock Points(int required, int have) =>
            new SkillLock(SkillLockKind.Points, required, have, null, 0);

        /// <summary>
        /// The English one-liner, for the DevConsole and for <c>LearnedSkills.CanLearn</c>'s
        /// legacy single-reason API. The player-facing Spanish lives in <c>SkillText</c>.
        /// </summary>
        public string Describe()
        {
            switch (Kind)
            {
                case SkillLockKind.Maxed:  return "Already at max rank.";
                case SkillLockKind.Level:  return $"Requires level {Required}.";
                case SkillLockKind.Prerequisite:
                    return $"Requires '{(Missing != null ? Missing.displayName : "?")}' at max rank.";
                case SkillLockKind.Points:
                    return $"Need {Required} skill point(s), have {Have}.";
                default: return "Malformed skill node.";
            }
        }
    }
}
