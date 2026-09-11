using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Diagnostics;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The measurement half of the debug HUD: the frame ring, its percentiles, the hitch rule,
    /// the console tally and the monitor that ties them together. Pure maths driven by hand —
    /// Unity calls no Update on a component added in Edit Mode, so the monitor is fed through
    /// <see cref="PerformanceMonitor.Sample"/>.
    /// </summary>
    [TestFixture]
    public class DebugHudMeasurementTests
    {
        // -- FrameTimeHistory --------------------------------------------------------

        [Test]
        public void Percentiles_AreNearestRank_OverTheWholeWindow()
        {
            var h = new FrameTimeHistory(256);
            for (int i = 1; i <= 100; i++) h.Push(i, i * 0.01f);

            var s = h.Compute(windowSeconds: 10f, now: 1f);

            Assert.AreEqual(100, s.Frames);
            Assert.AreEqual(50.5f, s.AvgMs, 1e-3f);
            Assert.AreEqual(50f, s.P50Ms);
            Assert.AreEqual(95f, s.P95Ms);
            Assert.AreEqual(99f, s.P99Ms);
            Assert.AreEqual(100f, s.MaxMs);
        }

        [Test]
        public void TheWindow_IsInSeconds_NotInFrames()
        {
            // The old monitor sorted the last 300 FRAMES: 2.6 s at 114 FPS, 10 s at 30. The same
            // question has to cover the same stretch of time at any frame rate.
            var h = new FrameTimeHistory(256);
            for (int i = 0; i < 10; i++) h.Push(10f, i * 0.1f);
            for (int i = 0; i < 5; i++) h.Push(30f, 5f + i * 0.1f);

            var s = h.Compute(windowSeconds: 1f, now: 5.4f);

            Assert.AreEqual(5, s.Frames, "only the frames of the last second");
            Assert.AreEqual(30f, s.AvgMs, 1e-3f);
        }

        [Test]
        public void AFrameLongerThanTheWindow_IsStillReported()
        {
            var h = new FrameTimeHistory(16);
            h.Push(10f, 1f);
            h.Push(500f, 10f);

            var s = h.Compute(windowSeconds: 0.1f, now: 10f);

            Assert.AreEqual(1, s.Frames, "the newest frame is always in the window");
            Assert.AreEqual(500f, s.MaxMs);
        }

        [Test]
        public void TheRing_KeepsTheNewest_AndIndexesByAge()
        {
            var h = new FrameTimeHistory(4);
            for (int i = 1; i <= 6; i++) h.Push(i, i);

            Assert.AreEqual(4, h.Count);
            Assert.AreEqual(6f, h.MsAt(0), "age 0 is the newest");
            Assert.AreEqual(3f, h.MsAt(3));
            Assert.AreEqual(0f, h.MsAt(4), "out of range reads as nothing, not as garbage");
            Assert.AreEqual(6f, h.StampAt(0));
        }

        [Test]
        public void AnEmptyHistory_ReportsNoData_NotZeroMilliseconds()
        {
            var s = new FrameTimeHistory().Compute(3f, 1f);
            Assert.IsTrue(s.IsEmpty);
            Assert.AreEqual(0f, s.Fps, "no data is 0 FPS, never infinity");
        }

        // -- Hitches -------------------------------------------------------------------

        [TestCase(40f, 10f, true, TestName = "Hitch_LongAndFarAboveTypical")]
        [TestCase(20f, 5f, false, TestName = "Hitch_RatioAloneIsNotEnough_BelowTheFloor")]
        [TestCase(30f, 20f, false, TestName = "Hitch_FloorAloneIsNotEnough_OnASlowMachine")]
        [TestCase(30f, 0f, true, TestName = "Hitch_NoTypicalYet_FloorDecides")]
        public void TheHitchRule_NeedsBothHalves(float frame, float typical, bool expected)
        {
            Assert.AreEqual(expected, FrameHitchLog.IsHitch(frame, typical, 2f, 25f));
        }

        [Test]
        public void TheHitchLog_IsNewestFirst_AndCountsWhatFellOff()
        {
            var log = new FrameHitchLog(capacity: 2);
            log.Record(new FrameHitch(1f, 30f, false));
            log.Record(new FrameHitch(2f, 40f, true));
            log.Record(new FrameHitch(3f, 50f, false));

            Assert.AreEqual(2, log.Count);
            Assert.AreEqual(3, log.TotalRecorded);
            Assert.AreEqual(50f, log.Get(0).Ms);
            Assert.IsTrue(log.Get(1).DuringGc);
        }

        // -- Console tally -----------------------------------------------------------------

        [Test]
        public void TheConsoleTally_CountsErrorsAndWarnings_AndKeepsTheFirstLine()
        {
            var t = new ConsoleLogTally();
            t.Record(LogType.Log, "noise", 0f);
            t.Record(LogType.Warning, "careful", 1f);
            t.Record(LogType.Error, "boom\nat Foo.Bar()", 2f);
            t.Record(LogType.Exception, "NullReferenceException: x\r\nstack", 3f);

            Assert.AreEqual(2, t.Errors, "Error and Exception are errors; Log is not");
            Assert.AreEqual(1, t.Warnings);
            Assert.AreEqual("NullReferenceException: x", t.LastError, "the first line only, CRLF or LF");

            t.Clear();
            Assert.AreEqual(0, t.Errors);
            Assert.AreEqual(string.Empty, t.LastError);
        }

        // -- The monitor ---------------------------------------------------------------------

        [Test]
        public void TheMonitor_RecordsAHitch_WithTheCollectionThatCausedIt()
        {
            var go = new GameObject("PerfMonitorTest");
            try
            {
                var perf = go.AddComponent<PerformanceMonitor>();
                float t = 0f;
                for (int i = 0; i < 90; i++) { t += 0.01f; perf.Sample(10f, 7, t); }
                Assert.AreEqual(10f, perf.Stats.P50Ms, 0.01f);
                Assert.AreEqual(0, perf.Hitches.Count, "steady frames are not hitches");

                t += 0.06f;
                perf.Sample(60f, 8, t);

                Assert.AreEqual(1, perf.Hitches.Count);
                Assert.AreEqual(60f, perf.Hitches.Get(0).Ms);
                Assert.IsTrue(perf.Hitches.Get(0).DuringGc, "the collection ran in that frame");
                Assert.AreEqual(1, perf.SessionGcCollections,
                                "counted from the session's first sample, not from the process start");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheMonitor_ReportsEveryStatistic_OverTheSameWindow()
        {
            var go = new GameObject("PerfMonitorWindow");
            try
            {
                var perf = go.AddComponent<PerformanceMonitor>();
                float t = 0f;
                for (int i = 0; i < 600; i++) { t += 0.02f; perf.Sample(i < 300 ? 5f : 20f, 0, t); }

                // 600 frames over 12 s: the last 3 s are all 20 ms frames. Nothing older leaks in.
                Assert.AreEqual(20f, perf.AvgFrameTimeMs, 0.01f);
                Assert.AreEqual(20f, perf.P50FrameTimeMs, 0.01f);
                Assert.AreEqual(20f, perf.P95FrameTimeMs, 0.01f);
                Assert.AreEqual(50f, perf.AvgFps, 0.1f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
