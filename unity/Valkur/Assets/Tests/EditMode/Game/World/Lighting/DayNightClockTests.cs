using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Lighting
{
    /// <summary>
    /// Pins the CLOCK half of <see cref="DayNightCycle"/> — the fractional day accumulator that
    /// replaced a float that was nudged by ~0.0000046 every frame.
    ///
    /// <see cref="DayNightCycleTests"/> covers phase classification and events; this fixture covers
    /// elapsed time, the day counter, and the invariant that every mutator moves the accumulator
    /// rather than only its projection (setting the projection alone was silently undone by the
    /// next frame).
    /// </summary>
    [TestFixture]
    public class DayNightClockTests
    {
        private GameObject   _go;
        private DayNightCycle _cycle;

        [SetUp]
        public void SetUp()
        {
            _go    = new GameObject("DayNightCycle");
            _cycle = _go.AddComponent<DayNightCycle>();
        }

        [TearDown]
        public void TearDown()
        {
            DayNightCycle.OnPhaseChanged = null;
            DayNightCycle.OnDayChanged   = null;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static void Advance(DayNightCycle c, float normalizedDelta)
            => c.AdvanceNormalized(normalizedDelta);

        [Test]
        public void SetTimeNormalized_SurvivesTheProjectionBeingRecomputed()
        {
            _cycle.SetTimeNormalized(0.63f);

            // Re-derive from the accumulator exactly as Update does. If SetTimeNormalized had only
            // written TimeNormalized, this would snap the clock back.
            var sync = typeof(DayNightCycle).GetMethod("SyncClockFromElapsed",
                BindingFlags.NonPublic | BindingFlags.Instance);
            sync.Invoke(_cycle, null);

            Assert.AreEqual(0.63f, _cycle.TimeNormalized, 1e-4f,
                "The accumulator is the clock; a setter that touches only its projection is a " +
                "value that lasts exactly one frame.");
        }

        [Test]
        public void DayCount_StartsAtZero()
        {
            Assert.AreEqual(0, _cycle.DayCount, "The first day is day 0.");
        }

        [Test]
        public void AdvanceNormalized_PastMidnight_IncrementsTheDay()
        {
            _cycle.SetTimeNormalized(0.9f);
            Assert.AreEqual(0, _cycle.DayCount);

            Advance(_cycle, 0.2f);   // 0.9 -> 1.1

            Assert.AreEqual(1, _cycle.DayCount, "Crossing midnight must roll the day counter.");
            Assert.AreEqual(0.1f, _cycle.TimeNormalized, 1e-4f,
                "…and the time of day must wrap, not saturate.");
        }

        [Test]
        public void OnDayChanged_FiresOncePerMidnight()
        {
            int fired = 0, last = -1;
            DayNightCycle.OnDayChanged = d => { fired++; last = d; };

            _cycle.SetTimeNormalized(0.5f);   // day 0, midday
            Advance(_cycle, 0.6f);            // 1.1 -> day 1
            Advance(_cycle, 1.0f);            // 2.1 -> day 2
            Advance(_cycle, 0.1f);            // 2.2 -> still day 2

            Assert.AreEqual(2, fired, "Two midnights crossed, two events.");
            Assert.AreEqual(2, last, "The event must carry the new day number.");
        }

        [Test]
        public void SetTimeNormalized_DoesNotChangeTheDay()
        {
            Advance(_cycle, 1.5f);              // day 1, midday
            Assert.AreEqual(1, _cycle.DayCount);

            _cycle.SetTimeNormalized(0.1f);     // scrub the clock inside the same day

            Assert.AreEqual(1, _cycle.DayCount,
                "Scrubbing the time of day is not time travel — the F2 phase buttons must not " +
                "silently advance or rewind the calendar.");
            Assert.AreEqual(0.1f, _cycle.TimeNormalized, 1e-4f);
        }

        [Test]
        public void SetElapsedDays_RestoresBothHalvesOfTheClock()
        {
            _cycle.SetElapsedDays(4.25d);

            Assert.AreEqual(4, _cycle.DayCount, "Day 4…");
            Assert.AreEqual(0.25f, _cycle.TimeNormalized, 1e-4f, "…at 06:00.");
            Assert.AreEqual(4.25d, _cycle.ElapsedDays, 1e-6d, "Round-trip must be exact.");
        }

        [Test]
        public void SetElapsedDays_RefusesNegativeTime()
        {
            _cycle.SetElapsedDays(-3d);

            Assert.AreEqual(0d, _cycle.ElapsedDays, 1e-9d,
                "A corrupt save must not put the clock before the run started, which would make " +
                "DayCount negative and every phase comparison meaningless.");
        }

        [Test]
        public void Accumulator_KeepsItsPrecisionOverALongRun()
        {
            // The reason the accumulator is a double. Ten in-game days at 60 fps and one hour per
            // day is 2.16 million increments of ~4.6e-6 each; a float loses its low bits long
            // before that and the clock stops being the same clock.
            const int steps = 200000;
            const float perStep = 10.5f / steps;    // ten and a half days, split evenly

            for (int i = 0; i < steps; i++) Advance(_cycle, perStep);

            Assert.AreEqual(10.5d, _cycle.ElapsedDays, 1e-5d,
                $"After {steps} increments the clock drifted to {_cycle.ElapsedDays:F9} days.");
            // Deliberately half a day past the boundary: landing exactly ON midnight is a
            // coin-flip between day N and N-1 for any accumulator, and pinning that would be
            // pinning float rounding rather than behaviour.
            Assert.AreEqual(10, _cycle.DayCount);
        }

        [Test]
        public void HourOfDay_TracksTheAccumulator()
        {
            _cycle.SetElapsedDays(2d + 18f / 24f);
            Assert.AreEqual(18, _cycle.HourOfDay, "Day 2, 18:00.");
            Assert.AreEqual(2,  _cycle.DayCount);
        }
    }
}
