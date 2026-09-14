using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// What the work bar reads: how far the job has come, where each payout falls along it, how
    /// long is left, and where the next blow is in its rhythm.
    ///
    /// <para><b>THE BAR FILLS.</b> It used to DRAIN — "how much of the tree is left" — which is the
    /// right number for an enemy's health and the wrong one for work: the player is building
    /// toward something, and a bar that empties as they succeed reads as losing. Progress now runs
    /// 0 to 1 toward the tree coming down, and the payouts are marked on the way.</para>
    ///
    /// <para><b>THE NOTCHES ARE THE PAYOUT RULE, NOT A DECORATION.</b> A skilled Destroy node pays
    /// one yield every <c>workPerYield</c> points of durability removed, so the marks sit at exactly
    /// those fractions of the maximum — the fill crossing a notch IS the frame a log goes into the
    /// bag. A Deplete seam marks each charge.</para>
    /// </summary>
    public partial class HarvestNode : IWorkSegments
    {
        /// <summary>Progress toward finishing the job, 0..1. What the bar draws.</summary>
        public float WorkDone01 => Mathf.Clamp01(1f - RemainingFraction);

        public void GetSegmentMarks(List<float> into)
        {
            into.Clear();
            if (_profile == null) return;

            if (_profile.harvestMode == HarvestMode.Deplete)
            {
                int charges = Mathf.Max(1, _profile.charges);
                for (int i = 1; i < charges; i++) into.Add(i / (float)charges);
                return;
            }

            if (_durability == null || !_profile.UsesSkillYield) return;

            float max = _durability.MaxDurability;
            int per = Mathf.Max(1, _profile.workPerYield);
            for (int work = per; work < max; work += per) into.Add(work / max);
        }

        /// <summary>
        /// Blows still needed at the worker's current worth, times the clock. Resolved through the
        /// SAME resolver a real blow uses, so the countdown shortens the moment an axe is equipped
        /// or the skill ticks up. Unknown without a worker.
        /// </summary>
        public float SecondsRemaining
        {
            get
            {
                if (_profile == null) return -1f;
                var worker = _worker != null ? _worker : LastWorker;
                if (worker == null) return -1f;

                var blow = HarvestBlowResolver.Resolve(_profile, worker, element: null);
                int perBlow = HarvestBlowResolver.Scale(_profile.blowDamage, blow.Multiplier);
                if (perBlow <= 0) return -1f;

                int left;
                if (_profile.harvestMode == HarvestMode.Deplete)
                    left = _chargesRemaining * WorkPerCharge - _chargeProgress;
                else
                    left = _durability != null ? _durability.CurrentDurability : 0;

                if (left <= 0) return 0f;
                int blows = Mathf.CeilToInt(left / (float)perBlow);

                // The first remaining blow is already partly waited out. While tapping the pace is the
                // beat, which is exactly the speed-up a player sees the countdown reward.
                float period = _rhythmMode && _sessionActive ? BeatSeconds : Mathf.Max(0.05f, _profile.secondsPerBlow);
                float next = _rhythmMode ? _nextBeatAt : _nextBlowAt;
                float untilNext = _sessionActive ? Mathf.Max(0f, next - Now) : period;
                return untilNext + (blows - 1) * period;
            }
        }

        public float BlowCadence01
        {
            get
            {
                if (!_sessionActive || _profile == null) return -1f;
                if (_rhythmMode)
                    return Mathf.Clamp01(1f - (_nextBeatAt - Now) / Mathf.Max(0.05f, BeatSeconds));
                float period = Mathf.Max(0.05f, _profile.secondsPerBlow);
                return Mathf.Clamp01(1f - (_nextBlowAt - Now) / period);
            }
        }
    }
}
