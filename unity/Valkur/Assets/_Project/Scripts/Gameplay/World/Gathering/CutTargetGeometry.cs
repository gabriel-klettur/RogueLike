using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Where things sit on the cut target drawn over the trunk: the radius of each grade's band, the
    /// radius the closing ring has reached, and where a cut leaves its mark.
    ///
    /// <para><b>TIME IS DRAWN AS DISTANCE FROM THE CENTRE.</b> The target's centre is the beat. A
    /// cut <c>d</c> seconds off it sits <c>d</c> seconds out — in half-widths of the hit window,
    /// scaled so the outer edge of the Awful band is <see cref="OuterRadius"/>. So the ring that
    /// closes on the centre as the beat approaches, the band it is crossing and the grade a tap
    /// would get right now are one number, and the bands are drawn from the very reaches
    /// <see cref="SkillDefinition.GradeCut"/> grades with.</para>
    ///
    /// <para><b>EARLY IS LEFT, LATE IS RIGHT.</b> A cut's mark is placed on the side it missed on,
    /// so a player who is always a little late sees a cluster of marks on the right of the centre and
    /// knows which way to correct without reading a word.</para>
    ///
    /// <para>Pure and scene-free, so all of it is provable in Edit Mode, where the rig builds nothing.</para>
    /// </summary>
    public static class CutTargetGeometry
    {
        /// <summary>World radius of the target's outer edge — the end of the Awful band.</summary>
        public const float OuterRadius = 0.6f;

        /// <summary>How far past the edge a soquete's mark is drawn, as a multiple of the outer radius.</summary>
        public const float SoqueteRadius = 1.28f;

        /// <summary>How far out the closing ring starts, as a multiple of the outer radius.</summary>
        public const float ApproachStartRadius = 1.6f;

        /// <summary>Vertical spacing between the lanes marks are dealt into, so a run of cuts does not stack.</summary>
        public const float MarkLaneSpacing = 0.075f;

        /// <summary>The outer radius of <paramref name="grade"/>'s band. Soquete answers the target's edge.</summary>
        public static float BandRadius(SkillDefinition skill, CutGrade grade)
        {
            if (skill == null) return OuterRadius;
            float edge = skill.CutReachOfWindow(CutGrade.Awful);
            return OuterRadius * skill.CutReachOfWindow(grade) / Mathf.Max(0.0001f, edge);
        }

        /// <summary>
        /// The radius a cut <paramref name="offsetSeconds"/> from the beat sits at, capped at
        /// <paramref name="capOfOuter"/> times the outer radius.
        /// </summary>
        public static float RadiusFor(SkillDefinition skill, float offsetSeconds, float windowSeconds, float capOfOuter)
        {
            if (skill == null || windowSeconds <= 0f) return OuterRadius * capOfOuter;
            float edge = Mathf.Max(0.0001f, skill.CutReachOfWindow(CutGrade.Awful));
            float d = Mathf.Abs(offsetSeconds) / windowSeconds / edge;
            return OuterRadius * Mathf.Min(d, capOfOuter);
        }

        /// <summary>
        /// Where a judged cut leaves its mark, relative to the target's centre: on the left when
        /// early, on the right when late, and dealt into one of three lanes by <paramref name="index"/>.
        /// A soquete sits just outside the edge, where it missed.
        /// </summary>
        public static Vector2 MarkPosition(SkillDefinition skill, CutGrade grade, float offsetSeconds,
            float windowSeconds, int index)
        {
            float r = grade == CutGrade.Soquete
                ? OuterRadius * SoqueteRadius
                : RadiusFor(skill, offsetSeconds, windowSeconds, 1f);
            float side = offsetSeconds < 0f ? -1f : 1f;
            int lane = ((index % 3) + 3) % 3 - 1;
            return new Vector2(side * r, lane * MarkLaneSpacing);
        }

        /// <summary>
        /// Band edges as fractions of the outer radius, centre first: Perfect, Good, Ok, Bad, Awful
        /// (1). What the target texture is painted from.
        /// </summary>
        public static void BandFractions(SkillDefinition skill, float[] into)
        {
            for (int i = 0; i < into.Length && i < 5; i++)
                into[i] = BandRadius(skill, CutGrade.Perfect + i) / OuterRadius;
        }
    }
}
