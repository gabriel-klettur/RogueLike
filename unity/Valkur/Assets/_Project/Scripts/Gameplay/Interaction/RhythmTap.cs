using Valkur.Data;

namespace Valkur.Gameplay.Interaction
{
    /// <summary>
    /// One tap, judged: the GRADE of the cut (how close to the centre of the beat) and the OUTCOME
    /// (what it did). They are two answers because they come apart — an early Bad cut with chances
    /// left is graded Bad and does nothing but hold the axe back, while a late Bad cut lands.
    /// </summary>
    public readonly struct RhythmTap
    {
        public readonly CutGrade Grade;
        public readonly RhythmTapOutcome Outcome;

        /// <summary>Seconds from the beat, negative = early. 0 when nothing was graded.</summary>
        public readonly float OffsetSeconds;

        /// <summary>The hit window's half-width the cut was graded against.</summary>
        public readonly float WindowSeconds;

        /// <summary>Chances left on this beat after the tap. Only meaningful for a retry.</summary>
        public readonly int TriesLeft;

        /// <summary>The combo the tap was made on, and the combo it left.</summary>
        public readonly int StreakBefore, StreakAfter;

        public RhythmTap(CutGrade grade, RhythmTapOutcome outcome, float offsetSeconds, float windowSeconds,
            int triesLeft, int streakBefore, int streakAfter)
        {
            Grade = grade;
            Outcome = outcome;
            OffsetSeconds = offsetSeconds;
            WindowSeconds = windowSeconds;
            TriesLeft = triesLeft;
            StreakBefore = streakBefore;
            StreakAfter = streakAfter;
        }

        public static RhythmTap Ignored => new RhythmTap(CutGrade.None, RhythmTapOutcome.Ignored, 0f, 0f, 0, 0, 0);

        public bool IsEarly => OffsetSeconds < 0f;
        public bool Landed => Outcome == RhythmTapOutcome.Landed;

        /// <summary>Whether the tap was judged at all (anything but Ignored).</summary>
        public bool Counted => Outcome != RhythmTapOutcome.Ignored;

        public override string ToString() => Outcome + "/" + Grade + " @" + OffsetSeconds.ToString("0.000");
    }
}
