using System.Collections.Generic;
using Valkur.Data;

namespace Valkur.Gameplay.Interaction
{
    /// <summary>
    /// The optional richer half of <see cref="IWorkProgress"/>: where along the job the rewards
    /// fall, and how long is left.
    ///
    /// <para><b>WHY A SECOND INTERFACE.</b> <see cref="IWorkProgress"/> is deliberately tiny so a
    /// fishing cast or a lock can have a bar without inventing anything. A job that PAYS OUT along
    /// the way can say more, and the bar draws a notch at every payout and a countdown — which is
    /// what turns a bar that merely fills into one the player watches: the next log is visibly
    /// three blows away.</para>
    /// </summary>
    public interface IWorkSegments
    {
        /// <summary>
        /// Positions 0..1 along the bar where a reward is paid, in ascending order. Written into
        /// <paramref name="into"/> (cleared first) so the bar allocates nothing per frame.
        /// </summary>
        void GetSegmentMarks(List<float> into);

        /// <summary>Estimated seconds until the job completes at the current pace. Negative = unknown.</summary>
        float SecondsRemaining { get; }

        /// <summary>
        /// 0..1 through the current blow's cadence — how close the next strike is. Negative when
        /// the job is not paced by a clock.
        /// </summary>
        float BlowCadence01 { get; }

        /// <summary>
        /// How far out one grade's band of the cut target reaches, as a fraction of a beat — drawn
        /// as nested bands at the end of the cadence sweep, the centre (Perfect) narrowest.
        /// Negative when the job is not being tapped.
        /// </summary>
        float CutBandReach01(CutGrade grade);

        /// <summary>
        /// The grade a tap made right now would get, or <see cref="CutGrade.None"/> when the job is
        /// not being tapped. What lights the band the sweep is in.
        /// </summary>
        CutGrade GradeIfTappedNow { get; }
    }
}
