using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Lighting
{
    /// <summary>
    /// Pins the public state machine of <see cref="DayNightCycle"/>:
    /// SetTimeNormalized wraps modulo 1, the phase classification matches
    /// the documented bands, and OnPhaseChanged fires only on real
    /// transitions. The Light2D side-effect can't be exercised in EditMode
    /// (no URP Light2D in fixture scenes); this fixture covers the pure
    /// state and event semantics that gameplay observers rely on.
    /// </summary>
    [TestFixture]
    public class DayNightCycleTests
    {
        private GameObject _go;
        private DayNightCycle _cycle;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("DayNightCycle");
            _cycle = _go.AddComponent<DayNightCycle>();
        }

        [TearDown]
        public void TearDown()
        {
            DayNightCycle.OnPhaseChanged = null;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static void InvokeUpdateLighting(DayNightCycle c)
        {
            var m = typeof(DayNightCycle).GetMethod("UpdateLighting",
                BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(c, null);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void SetTimeNormalized_WrapsModuloOne()
        {
            _cycle.SetTimeNormalized(2.25f);
            Assert.AreEqual(0.25f, _cycle.TimeNormalized, 0.0001f,
                "Time outside [0,1) must wrap so callers can pass elapsed " +
                "in-game seconds without normalising first.");
        }

        [Test]
        public void HourOfDay_DerivesFromTimeNormalized()
        {
            _cycle.SetTimeNormalized(0.5f);
            Assert.AreEqual(12, _cycle.HourOfDay,
                "0.5 normalised time = noon = hour 12.");

            _cycle.SetTimeNormalized(0f);
            Assert.AreEqual(0, _cycle.HourOfDay, "0.0 = midnight.");

            _cycle.SetTimeNormalized(0.999f);
            Assert.AreEqual(23, _cycle.HourOfDay,
                "Just before midnight must still resolve to hour 23.");
        }

        [Test]
        public void Phase_AtMidday_IsDay()
        {
            _cycle.SetTimeNormalized(0.5f);
            InvokeUpdateLighting(_cycle);
            Assert.AreEqual(DayNightCycle.DayPhase.Day, _cycle.CurrentPhase);
        }

        [Test]
        public void Phase_AtMidnight_IsNight()
        {
            _cycle.SetTimeNormalized(0f);
            InvokeUpdateLighting(_cycle);
            Assert.AreEqual(DayNightCycle.DayPhase.Night, _cycle.CurrentPhase,
                "00:00 falls outside the [0.18, 0.84) lit window — must be Night.");
        }

        [Test]
        public void Phase_AtDawn_IsDawn()
        {
            // Dawn band is [0.18, 0.30); the entire band classifies as Dawn
            // in the Python-clean 4-phase model — the dawnColor overlay
            // peaks at the middle of the band and the phase identifier
            // never flips inside it.
            _cycle.SetTimeNormalized(0.24f);
            InvokeUpdateLighting(_cycle);
            Assert.AreEqual(DayNightCycle.DayPhase.Dawn, _cycle.CurrentPhase);
        }

        [Test]
        public void Phase_AtDusk_IsDusk()
        {
            // Dusk band is [0.70, 0.84); same single-phase contract as Dawn.
            _cycle.SetTimeNormalized(0.77f);
            InvokeUpdateLighting(_cycle);
            Assert.AreEqual(DayNightCycle.DayPhase.Dusk, _cycle.CurrentPhase);
        }

        [Test]
        public void OnPhaseChanged_FiresOnTransition_NotOnSamePhase()
        {
            int callCount = 0;
            DayNightCycle.DayPhase lastPhase = DayNightCycle.DayPhase.Day;
            DayNightCycle.OnPhaseChanged = p => { callCount++; lastPhase = p; };

            _cycle.SetTimeNormalized(0.5f); // Day — initial state is Day, no transition.
            InvokeUpdateLighting(_cycle);
            int afterFirstSet = callCount;

            _cycle.SetTimeNormalized(0f);   // Night
            InvokeUpdateLighting(_cycle);

            _cycle.SetTimeNormalized(0.05f); // Still night.
            InvokeUpdateLighting(_cycle);

            Assert.AreEqual(DayNightCycle.DayPhase.Night, lastPhase,
                "Last fired phase must reflect the latest transition.");
            Assert.AreEqual(afterFirstSet + 1, callCount,
                "OnPhaseChanged must fire exactly once when crossing Day→Night, " +
                "and not again while staying inside the Night band.");
        }

        [Test]
        public void OnPhaseChanged_FiresOnEveryDistinctTransition()
        {
            int events = 0;
            DayNightCycle.OnPhaseChanged = _ => events++;

            // Initial state is Day (default). Each SetTimeNormalized that
            // crosses into a new phase must produce one event.
            _cycle.SetTimeNormalized(0.0f);  InvokeUpdateLighting(_cycle); // Day → Night
            _cycle.SetTimeNormalized(0.24f); InvokeUpdateLighting(_cycle); // Night → Dawn
            _cycle.SetTimeNormalized(0.5f);  InvokeUpdateLighting(_cycle); // Dawn → Day
            _cycle.SetTimeNormalized(0.77f); InvokeUpdateLighting(_cycle); // Day → Dusk

            Assert.AreEqual(4, events,
                "Four distinct phase transitions must fire four events. " +
                "Less = the dispatcher is collapsing transitions; more = " +
                "intermediate updates are firing redundantly.");
        }
    }
}
