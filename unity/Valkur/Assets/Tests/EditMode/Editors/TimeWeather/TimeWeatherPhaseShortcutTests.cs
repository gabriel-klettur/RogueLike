using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TimeWeather;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TimeWeather
{
    /// <summary>
    /// Ties the F2 editor's Phases panel to the cycle it drives.
    ///
    /// The panel is two parallel arrays that nothing but a comment keeps aligned: the labels and
    /// hour texts in <c>TimeWeatherEditorUIBuilder.CYCLE_ROWS</c>, and the times in
    /// <c>TimeWeatherEditor.CYCLE_NORMALIZED_TIMES</c>. Insert a row in one and not the other and
    /// every button below it jumps to the wrong hour, with the UI still reading correctly.
    ///
    /// Worse, a button can stay internally consistent and still lie: "Dawn 05:30" is only true
    /// while 05:30 falls inside the cycle's dawn band. Move a band and the label becomes fiction
    /// with nothing to catch it. These tests assert the round trip — button, hour text, and the
    /// phase the cycle actually reports.
    /// </summary>
    [TestFixture]
    public class TimeWeatherPhaseShortcutTests
    {
        private GameObject    _go;
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
            DayNightCycle.OnPhaseChanged           = null;
            DayNightCycle.OnLightsEnabledChanged   = null;
            DayNightCycle.OnLightingEnabledChanged = null;
            DayNightCycle.OnDayChanged             = null;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        /// <summary>The times the phase buttons jump to, read off the editor itself.</summary>
        private static float[] ShortcutTimes()
        {
            var field = typeof(TimeWeatherEditor).GetField("CYCLE_NORMALIZED_TIMES",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field,
                "TimeWeatherEditor.CYCLE_NORMALIZED_TIMES is gone — it is what the Phases panel " +
                "actually jumps to.");
            var times = field.GetValue(null) as float[];
            Assert.IsNotNull(times);
            return times;
        }

        private static TimeWeatherEditorUIBuilder.CycleRow[] Rows()
        {
            var rows = TimeWeatherEditorUIBuilder.CYCLE_ROWS;
            Assert.IsNotNull(rows);
            return rows;
        }

        private DayNightCycle.DayPhase PhaseAt(float t)
        {
            _cycle.SetTimeNormalized(t);
            var update = typeof(DayNightCycle).GetMethod("UpdateLighting",
                BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(_cycle, null);
            return _cycle.CurrentPhase;
        }

        // ── The two arrays that only a comment keeps aligned ───────────────────────

        [Test]
        public void EveryPanelRow_HasATimeToJumpTo()
        {
            Assert.AreEqual(Rows().Length, ShortcutTimes().Length,
                "The Phases panel draws one row per CYCLE_ROWS entry but jumps using " +
                "CYCLE_NORMALIZED_TIMES by the same index. Different lengths means a row either " +
                "throws on click or silently jumps to the wrong hour.");
        }

        [Test]
        public void EveryRowsHourText_MatchesTheTimeItJumpsTo()
        {
            var rows  = Rows();
            var times = ShortcutTimes();

            for (int i = 0; i < rows.Length; i++)
            {
                int minutes = Mathf.RoundToInt(times[i] * 1440f) % 1440;
                string expected = $"{minutes / 60:00}:{minutes % 60:00}";
                Assert.AreEqual(expected, rows[i].HourText,
                    $"Row '{rows[i].Label}' shows {rows[i].HourText} but jumps to {expected}. " +
                    "The label is what the player trusts.");
            }
        }

        [Test]
        public void ShortcutTimes_AreDistinct()
        {
            var times = ShortcutTimes();
            for (int i = 0; i < times.Length; i++)
            for (int j = i + 1; j < times.Length; j++)
                Assert.AreNotEqual(times[i], times[j],
                    $"Rows {i} and {j} jump to the same moment, so one of them does nothing.");
        }

        // ── The labels must describe the phase the cycle really reports ────────────

        [Test]
        public void DawnButton_LandsInTheDawnBand()
            => AssertRowLandsIn("Dawn", DayNightCycle.DayPhase.Dawn);

        [Test]
        public void MorningButton_LandsInTheDayBand()
            => AssertRowLandsIn("Morning", DayNightCycle.DayPhase.Day);

        [Test]
        public void NoonButton_LandsInTheDayBand()
            => AssertRowLandsIn("Noon", DayNightCycle.DayPhase.Day);

        [Test]
        public void DuskButton_LandsInTheDuskBand()
            => AssertRowLandsIn("Dusk", DayNightCycle.DayPhase.Dusk);

        [Test]
        public void MidnightButton_LandsInTheNightBand()
            => AssertRowLandsIn("Midnight", DayNightCycle.DayPhase.Night);

        private void AssertRowLandsIn(string label, DayNightCycle.DayPhase expected)
        {
            var rows  = Rows();
            var times = ShortcutTimes();

            int idx = -1;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].Label == label) { idx = i; break; }

            Assert.GreaterOrEqual(idx, 0,
                $"The Phases panel no longer has a '{label}' row. If it was renamed on purpose, " +
                "rename it here too — this test exists so the button and the band cannot drift apart.");
            Assert.Less(idx, times.Length, $"Row '{label}' has no matching time.");

            var actual = PhaseAt(times[idx]);
            Assert.AreEqual(expected, actual,
                $"'{label}' jumps to {rows[idx].HourText} (t={times[idx]:F4}), but the cycle " +
                $"reports {actual} there, not {expected}. Either the button's hour or the phase " +
                "band moved, and the panel is now mislabelling the world.");
        }

        // ── The band boundaries the labels depend on ──────────────────────────────

        [Test]
        public void PhaseBands_KeepTheirOrderAndCoverTheWholeDay()
        {
            Assert.Less(DayNightCycle.DAWN_START, DayNightCycle.DAY_START,   "Dawn precedes Day.");
            Assert.Less(DayNightCycle.DAY_START,  DayNightCycle.DUSK_START,  "Day precedes Dusk.");
            Assert.Less(DayNightCycle.DUSK_START, DayNightCycle.NIGHT_START, "Dusk precedes Night.");

            // Night is the wrap-around remainder, so covering the day is the same as every
            // sampled moment resolving to one of the four.
            for (int i = 0; i < 500; i++)
            {
                float t = i / 500f;
                var phase = PhaseAt(t);
                Assert.That(phase, Is.EqualTo(DayNightCycle.DayPhase.Day)
                                  .Or.EqualTo(DayNightCycle.DayPhase.Dawn)
                                  .Or.EqualTo(DayNightCycle.DayPhase.Dusk)
                                  .Or.EqualTo(DayNightCycle.DayPhase.Night),
                    $"t={t:F3} classified as {phase}, which the four-band model never produces.");
            }
        }

        [Test]
        public void TorchesAreLitThroughDuskAndNight_AndDarkThroughTheDay()
        {
            // The lights-on window used to be Python's 08:45-20:45 literals while the bands sat at
            // 07:12 and 20:10. The mismatch left roughly 35 in-game minutes of full night with
            // every torch still switched off — the darkest, least readable window of the cycle.
            Assert.AreEqual(DayNightCycle.DAY_START,  DayNightCycle.DefaultLightsOffStartNormalized, 1e-4f,
                "Lights must go out exactly when the flat day band begins.");
            Assert.AreEqual(DayNightCycle.DUSK_START, DayNightCycle.DefaultLightsOffEndNormalized, 1e-4f,
                "Lights must come back exactly when dusk begins — a torch is for dusk.");

            AssertLightsOn(DayNightCycle.DUSK_START  + 0.01f, true,  "just into dusk");
            AssertLightsOn(DayNightCycle.NIGHT_START + 0.01f, true,  "deep night");
            AssertLightsOn(0.02f,                             true,  "after midnight");
            AssertLightsOn(DayNightCycle.DAY_START   + 0.05f, false, "mid-morning");
            AssertLightsOn(0.5f,                              false, "noon");
        }

        private void AssertLightsOn(float t, bool expected, string when)
        {
            PhaseAt(t);
            Assert.AreEqual(expected, _cycle.LightsEnabledNow,
                $"Placed lights should be {(expected ? "ON" : "OFF")} at {when} (t={t:F3}).");
        }
    }
}
