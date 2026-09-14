using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Player
{
    /// <summary>
    /// When running TEACHES: one gain roll for every <see cref="LocomotionTuning.gainEveryRunDistance"/>
    /// world units really covered at a run, and never more than
    /// <see cref="LocomotionTuning.maxGainRollsPerMinute"/> in any rolling minute.
    ///
    /// <para>Distance, not seconds, because distance is what running into a wall does not produce
    /// — <see cref="LocomotionGait.RunDistance"/> is the measured speed, and the gait refuses to
    /// run at all against a wall. The per-minute cap is the other half: a key held down in an open
    /// field would otherwise be the optimal way to master the skill, and a skill a macro masters is
    /// a skill nobody notices growing. Distance past the cap is dropped, not banked, or the cap
    /// would only delay the same total.</para>
    ///
    /// <para>Pure, with the clock passed in, so a test can run an hour of jogging in a loop.</para>
    /// </summary>
    public sealed class AthleticsTrainer
    {
        private readonly LocomotionTuning _tuning;
        private readonly float[] _rollTimes;
        private int _head;
        private int _count;
        private float _distance;

        public AthleticsTrainer(LocomotionTuning tuning)
        {
            _tuning = tuning;
            _rollTimes = new float[Mathf.Max(1, tuning.maxGainRollsPerMinute)];
        }

        /// <summary>Distance banked towards the next roll, world units.</summary>
        public float PendingDistance => _distance;

        /// <summary>Adds running distance and answers how many gain rolls it earned.</summary>
        public int Accumulate(float runDistance, float now)
        {
            if (runDistance <= 0f) return 0;
            _distance += runDistance;
            float every = Mathf.Max(0.5f, _tuning.gainEveryRunDistance);
            int rolls = 0;
            while (_distance >= every)
            {
                _distance -= every;
                if (!TryClaim(now)) continue;
                rolls++;
            }
            return rolls;
        }

        private bool TryClaim(float now)
        {
            // Expire rolls older than a minute from the ring's oldest end.
            while (_count > 0)
            {
                int oldest = (_head - _count + _rollTimes.Length) % _rollTimes.Length;
                if (now - _rollTimes[oldest] < 60f) break;
                _count--;
            }
            if (_count >= _rollTimes.Length) return false;
            _rollTimes[_head] = now;
            _head = (_head + 1) % _rollTimes.Length;
            _count++;
            return true;
        }
    }
}
