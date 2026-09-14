using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The words and colours of the rhythm gear — the "PERFECT / GREAT / MISS" of a dance game, as
    /// six grades of cut: CORTE PERFECTO, BUENO, OK, MALO, PÉSIMO and SOQUETE.
    ///
    /// <para><b>PURE, SO THE WORDING IS TESTABLE WITHOUT A SCENE.</b> The rigs that animate these
    /// build nothing in Edit Mode; everything a player reads is decided here.</para>
    ///
    /// <para><b>ONE PALETTE FOR THE WORD, THE TARGET AND THE BAR.</b> The band a cut lands in on the
    /// trunk's target, the band on the work bar and the word over the trunk are the same colour
    /// (<see cref="CutColour"/>), so "I landed in the orange ring" and "CORTE MALO" are one fact.
    /// Warm gold at the centre, cooling through cyan and green, then heating back through orange to
    /// red at the edge — the centre and the edge are both vivid, and nobody confuses them.</para>
    ///
    /// <para><b>A WEAK CUT POINTS.</b> "« CORTE MALO" leans back toward the beat the tap came
    /// before; "CORTE MALO »" leans past it. That is the whole of what a weak cut can teach, so it
    /// has to say it. A good cut does not point: the player has nothing to correct.</para>
    ///
    /// <para><b>A COMBO IS ONLY NAMED FROM TWO</b>, and breaking one is only announced once it was
    /// worth something (3+).</para>
    /// </summary>
    public static class RhythmCallouts
    {
        public static readonly Color Perfect = new Color(1f, 0.84f, 0.28f, 1f);
        public static readonly Color Good = new Color(0.45f, 0.90f, 1f, 1f);
        public static readonly Color Ok = new Color(0.62f, 0.95f, 0.45f, 1f);
        public static readonly Color Bad = new Color(1f, 0.60f, 0.22f, 1f);
        public static readonly Color Awful = new Color(1f, 0.36f, 0.20f, 1f);
        public static readonly Color Soquete = new Color(1f, 0.20f, 0.30f, 1f);
        public static readonly Color Cue = new Color(0.72f, 0.98f, 1f, 1f);

        /// <summary>A retry: the axe was held back. Not a grade colour — nothing landed.</summary>
        public static readonly Color Retry = new Color(1f, 0.78f, 0.45f, 1f);

        /// <summary>Streaks worth a shout. Past the last one, every further hundred.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of literal streak counts, never written after type initialisation.")]
        public static readonly int[] Milestones = { 5, 10, 25, 50, 100 };

        public const int ComboShownFrom = 2;
        public const int BreakAnnouncedFrom = 3;

        /// <summary>The colour of a grade, shared by the word, the target band and the bar band.</summary>
        public static Color CutColour(CutGrade grade)
        {
            switch (grade)
            {
                case CutGrade.Perfect: return Perfect;
                case CutGrade.Good:    return Good;
                case CutGrade.Ok:      return Ok;
                case CutGrade.Bad:     return Bad;
                case CutGrade.Awful:   return Awful;
                case CutGrade.Soquete: return Soquete;
                default:               return Cue;
            }
        }

        public static RhythmCallout ForTap(RhythmTap tap)
        {
            switch (tap.Outcome)
            {
                case RhythmTapOutcome.Started:
                    return new RhythmCallout("¡AL RITMO!", Cue, 0.8f, RhythmCalloutMotion.Rise);

                case RhythmTapOutcome.Retry:
                {
                    string more = tap.TriesLeft == 1 ? " (otra)" : " (" + tap.TriesLeft + " más)";
                    return new RhythmCallout("« PRONTO" + more, Retry, 0.85f, RhythmCalloutMotion.Shake);
                }

                case RhythmTapOutcome.Lost:
                    return new RhythmCallout("¡" + SkillDefinition.CutLabel(CutGrade.Soquete) + "!", Soquete, 1.25f,
                        RhythmCalloutMotion.Shake);

                case RhythmTapOutcome.Landed:
                    return ForLandedCut(tap);

                default:
                    return default;
            }
        }

        private static RhythmCallout ForLandedCut(RhythmTap tap)
        {
            var grade = tap.Grade;
            string label = SkillDefinition.CutLabel(grade);
            bool milestone = SkillDefinition.CutKeepsStreak(grade) && IsMilestone(tap.StreakAfter);
            float shout = milestone ? 1.45f : 1f;

            switch (grade)
            {
                case CutGrade.Perfect:
                    return new RhythmCallout("¡" + label + "!", Perfect, 1.15f * shout, RhythmCalloutMotion.Rise);
                case CutGrade.Good:
                    return new RhythmCallout("¡" + label + "!", Good, 1f * shout, RhythmCalloutMotion.Rise);
                case CutGrade.Ok:
                    return new RhythmCallout(label, Ok, 0.9f * shout, RhythmCalloutMotion.Rise);
                case CutGrade.Bad:
                    return new RhythmCallout(Pointed(label, tap.IsEarly), Bad, 0.95f, RhythmCalloutMotion.Sag);
                default:
                    return new RhythmCallout(Pointed(label, tap.IsEarly), Awful, 1f, RhythmCalloutMotion.Sag);
            }
        }

        /// <summary>"« CORTE MALO" when early, "CORTE MALO »" when late.</summary>
        public static string Pointed(string label, bool early) => early ? "« " + label : label + " »";

        public static bool IsMilestone(int streak)
        {
            if (streak <= 0) return false;
            for (int i = 0; i < Milestones.Length; i++)
                if (streak == Milestones[i]) return true;
            int last = Milestones[Milestones.Length - 1];
            return streak > last && streak % last == 0;
        }

        /// <summary>"COMBO x7", or "¡RACHA x10!" on a milestone, or empty below <see cref="ComboShownFrom"/>.</summary>
        public static string ComboText(int streak)
        {
            if (streak < ComboShownFrom) return string.Empty;
            return IsMilestone(streak) ? "¡RACHA x" + streak + "!" : "COMBO x" + streak;
        }

        /// <summary>
        /// The combo's colour climbs with it: white, then cyan from 5, gold from 10, hot orange
        /// from 25 — so a long streak is visible at a glance without reading the number.
        /// </summary>
        public static Color ComboColour(int streak)
        {
            if (streak >= 25) return new Color(1f, 0.55f, 0.20f, 1f);
            if (streak >= 10) return Perfect;
            if (streak >= 5) return Good;
            return Color.white;
        }

        /// <summary>
        /// Whether this tap broke a combo worth announcing: a landed weak cut or a soquete, on a
        /// streak of <see cref="BreakAnnouncedFrom"/> or more. A retry keeps the combo.
        /// </summary>
        public static bool AnnouncesBreak(RhythmTap tap)
        {
            bool breaks = tap.Outcome == RhythmTapOutcome.Lost
                          || (tap.Outcome == RhythmTapOutcome.Landed && !SkillDefinition.CutKeepsStreak(tap.Grade));
            return breaks && tap.StreakBefore >= BreakAnnouncedFrom;
        }
    }
}
