using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Boot;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// The boot log's files and the trend read back from them. Every file here is written to a
    /// temp folder; the real <c>Diagnostics/Boot</c> is never touched, and
    /// <see cref="BootRunLog.TryWrite"/> refuses outside Play Mode by design.
    /// </summary>
    [TestFixture]
    public class BootRunLogTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "valkur_bootlog_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void OutsidePlayMode_NothingIsWritten()
        {
            Assert.IsNull(BootRunLog.TryWrite(new BootRunRecord { runId = "x" }, "r"),
                "Un fixture que completa un arranque sintetico no puede ensuciar el historial real.");
        }

        [Test]
        public void CsvFields_RoundTripCommasAndQuotes()
        {
            string row = BootRunLog.Escape("a,b") + "," + BootRunLog.Escape("dice \"hola\"") + "," + BootRunLog.Escape("plano");
            var f = BootRunLog.SplitCsv(row);
            Assert.AreEqual(3, f.Count);
            Assert.AreEqual("a,b", f[0]);
            Assert.AreEqual("dice \"hola\"", f[1]);
            Assert.AreEqual("plano", f[2]);
        }

        private static BootRunRecord Run(string id, float mundo, float partida)
        {
            var r = new BootRunRecord { runId = id, utc = "t", editor = true };
            r.phases.Add(new BootPhaseRecord { name = "Mundo", steps = 3, predictedMs = 1000f, actualMs = mundo });
            r.phases.Add(new BootPhaseRecord { name = "Partida", steps = 1, predictedMs = 100f, actualMs = partida });
            r.stepRows.Add(new BootStepRecord { index = 0, phase = "Mundo", label = "Cargando el mundo", cpuMs = mundo * 0.5f, wallMs = mundo });
            r.stepRows.Add(new BootStepRecord { index = 1, phase = "Partida", label = "Restaurando la partida", cpuMs = partida, wallMs = partida });
            return r;
        }

        private void WriteHistory(params BootRunRecord[] runs)
        {
            var phases = new List<string> { "header" };
            var steps = new List<string> { "header" };
            foreach (var r in runs)
            {
                phases.AddRange(BootRunLog.PhaseRows(r));
                steps.AddRange(BootRunLog.StepRows(r));
            }
            File.WriteAllLines(Path.Combine(_dir, BootRunLog.PhasesCsv), phases);
            File.WriteAllLines(Path.Combine(_dir, BootRunLog.StepsCsv), steps);
        }

        [Test]
        public void TheTrend_ComparesAgainstTheMedian_AndNamesWhatGrew()
        {
            // The first boot is the cold outlier a median exists to ignore.
            WriteHistory(Run("r1", 3000f, 100f), Run("r2", 1000f, 100f), Run("r3", 1020f, 100f), Run("r4", 1500f, 60f));
            var rows = BootRunLog.ReadPhaseRows(Path.Combine(_dir, BootRunLog.PhasesCsv));
            Assert.AreEqual(8, rows.Count);

            var trend = BootTrend.AnalyzePhases(rows, editor: true);
            Assert.AreEqual(2, trend.Count);
            Assert.AreEqual("Mundo", trend[0].Phase);
            Assert.AreEqual(1020f, trend[0].BaselineMs, 1e-3f);
            Assert.AreEqual(BootTrend.Verdict.Growing, trend[0].Verdict);
            Assert.AreEqual(BootTrend.Verdict.Shrinking, trend[1].Verdict);

            var steps = BootRunLog.ReadStepRows(Path.Combine(_dir, BootRunLog.StepsCsv));
            var hot = BootTrend.Hotspots(steps);
            Assert.AreEqual("Cargando el mundo", hot[0].Step);

            string report = BootTrend.Report(rows, steps, editor: true);
            StringAssert.Contains("SUBE", report);
            StringAssert.Contains("BAJA", report);
        }

        [Test]
        public void SmallWiggles_AreStable()
        {
            Assert.AreEqual(BootTrend.Verdict.Stable, BootTrend.Classify(3f, 2f, 5), "+50 % de 2 ms es ruido.");
            Assert.AreEqual(BootTrend.Verdict.Stable, BootTrend.Classify(1040f, 1000f, 5));
            Assert.AreEqual(BootTrend.Verdict.New, BootTrend.Classify(1000f, 0f, 0));
        }

        [Test]
        public void EditorAndBuildBoots_AreSeparateSeries()
        {
            var ed = Run("e1", 1000f, 100f);
            var build = Run("b1", 400f, 50f);
            build.editor = false;
            WriteHistory(ed, build);
            var rows = BootRunLog.ReadPhaseRows(Path.Combine(_dir, BootRunLog.PhasesCsv));
            Assert.AreEqual(1, BootTrend.RunIds(rows, true).Count);
            Assert.AreEqual(1, BootTrend.RunIds(rows, false).Count);
        }
    }
}
