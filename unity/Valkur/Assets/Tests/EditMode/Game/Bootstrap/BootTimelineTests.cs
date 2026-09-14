using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Boot;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// The boot's instrument: per-stage timings, the failure log, and the self-calibration
    /// that keeps the bar honest without anybody maintaining a number.
    ///
    /// <para><b>PlayerPrefs is MACHINE state, not fixture state.</b> The weight profile
    /// survives the run, the Editor and the reboot, so a fixture that leaves one behind
    /// makes an unrelated assertion fail forever on that computer only. Cleared in both
    /// SetUp and TearDown, which is the rule this repository already writes down for
    /// every PlayerPrefs-backed default.</para>
    /// </summary>
    [TestFixture]
    public class BootTimelineTests
    {
        private const string PrefsKey = "valkur.boot.weights.v1";

        /// <summary>
        /// The author's REAL calibration, put back after the fixture. Deleting it (as this fixture
        /// used to, and still must per test) cost the next boot its prediction: measured, the boot
        /// right after a test run logged calibrated=0 and drew its etapas with no time estimate.
        /// </summary>
        private string _machineProfile;

        [OneTimeSetUp]
        public void StashMachineProfile() => _machineProfile = PlayerPrefs.GetString(PrefsKey, string.Empty);

        [OneTimeTearDown]
        public void RestoreMachineProfile()
        {
            if (string.IsNullOrEmpty(_machineProfile)) PlayerPrefs.DeleteKey(PrefsKey);
            else PlayerPrefs.SetString(PrefsKey, _machineProfile);
            PlayerPrefs.Save();
        }

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            BootTimeline.ClearWeights();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            BootTimeline.BeginRun(new List<BootStep>());
        }

        private static List<BootStep> Sequence(params BootStep[] steps) => new List<BootStep>(steps);

        private static BootStep Noop(string label, float weight = 1f, int subStages = 0)
            => subStages > 0
                ? BootStep.Coroutine(label, () => null, weight, subStages)
                : BootStep.Of(label, () => { }, weight);

        // ── The run ──────────────────────────────────────────────────────────

        [Test]
        public void ABootThatHasNotRun_SaysSoRatherThanReportingZeroes()
        {
            BootTimeline.BeginRun(new List<BootStep>());
            var report = BootTimeline.Report();
            Assert.IsNotNull(report);
            Assert.IsNotEmpty(report);
        }

        [Test]
        public void TheTotalIsTheStepsQueued()
        {
            BootTimeline.BeginRun(Sequence(Noop("a"), Noop("b"), Noop("c")));
            Assert.AreEqual(3, BootTimeline.StepCount);
            Assert.AreEqual(0f, BootTimeline.Fraction, 1e-4f);
        }

        [Test]
        public void CompletingEveryStep_StillDoesNotClaimReady()
        {
            var steps = Sequence(Noop("a"), Noop("b"));
            BootTimeline.BeginRun(steps);
            for (int i = 0; i < steps.Count; i++)
            {
                BootTimeline.BeginStep(steps[i]);
                BootTimeline.EndStep(i);
            }
            Assert.Less(BootTimeline.Fraction, 1f,
                "La barra llego al 100 % antes de que nadie dijese que el juego esta listo.");

            BootTimeline.CompleteRun();
            Assert.AreEqual(1f, BootTimeline.Fraction, 1e-4f);
        }

        // ── Sub-stages ───────────────────────────────────────────────────────

        /// <summary>
        /// A coroutine that narrates itself has to walk its OWN share of the bar. Before
        /// this the world load changed its label four times while the bar stood
        /// perfectly still — which is the freeze those coroutines were written to remove.
        /// </summary>
        [Test]
        public void SubStages_WalkTheirParentsShare_AndNeverBorrowFromTheNextStep()
        {
            var world = Noop("mundo", weight: 90f, subStages: 3);
            var after = Noop("despues", weight: 10f);
            var steps = Sequence(world, after);

            BootTimeline.BeginRun(steps);
            BootTimeline.BeginStep(world);

            float a = BootTimeline.NextSubStageFraction();
            float b = BootTimeline.NextSubStageFraction();
            float c = BootTimeline.NextSubStageFraction();

            Assert.Less(a, b, "Las sub-etapas no avanzan.");
            Assert.Less(b, c, "Las sub-etapas no avanzan.");
            Assert.AreEqual(0.30f, a, 0.02f);
            Assert.AreEqual(0.60f, b, 0.02f);
            Assert.AreEqual(0.90f, c, 0.02f);
        }

        [Test]
        public void ExtraSubStages_AreClampedToTheParentsShare()
        {
            var world = Noop("mundo", weight: 50f, subStages: 2);
            var steps = Sequence(world, Noop("despues", weight: 50f));

            BootTimeline.BeginRun(steps);
            BootTimeline.BeginStep(world);

            BootTimeline.NextSubStageFraction();
            BootTimeline.NextSubStageFraction();
            float overflow = BootTimeline.NextSubStageFraction();

            Assert.LessOrEqual(overflow, 0.5f + 1e-3f,
                "Una sub-etapa no declarada se comio parte del paso siguiente.");
        }

        [Test]
        public void AStepWithNoSubStages_ReportsTheRunningFraction()
        {
            var plain = Noop("simple");
            BootTimeline.BeginRun(Sequence(plain, Noop("otro")));
            BootTimeline.BeginStep(plain);
            Assert.AreEqual(BootTimeline.Fraction, BootTimeline.NextSubStageFraction(), 1e-4f);
        }

        // ── Failures ─────────────────────────────────────────────────────────

        [Test]
        public void FailuresAreCollected_AndNamedInTheReport()
        {
            var step = Noop("paso roto");
            BootTimeline.BeginRun(Sequence(step));
            BootTimeline.BeginStep(step);
            BootTimeline.RecordFailure("paso roto", new System.InvalidOperationException("boom"));
            BootTimeline.EndStep(0, failed: true);
            BootTimeline.CompleteRun();

            Assert.IsTrue(BootTimeline.AnyFailed);
            Assert.AreEqual(1, BootTimeline.Failures.Count);
            StringAssert.Contains("paso roto", BootTimeline.Failures[0]);
            StringAssert.Contains("boom", BootTimeline.Failures[0]);
            StringAssert.Contains("paso roto", BootTimeline.Report());
        }

        [Test]
        public void ACleanRun_ReportsNoFailures()
        {
            var step = Noop("paso");
            BootTimeline.BeginRun(Sequence(step));
            BootTimeline.BeginStep(step);
            BootTimeline.EndStep(0);
            BootTimeline.CompleteRun();

            Assert.IsFalse(BootTimeline.AnyFailed);
            Assert.AreEqual(0, BootTimeline.Failures.Count);
        }

        // ── Self-calibration ─────────────────────────────────────────────────

        /// <summary>
        /// The half that makes the bar honest on the machine it runs on: this boot's
        /// measurements become the next boot's weights, so a newly added stage
        /// self-corrects on the second launch instead of waiting for somebody to
        /// update a constant — which is precisely what nobody did for seventeen steps.
        /// </summary>
        [Test]
        public void MeasurementsFromOneRun_CalibrateTheNext()
        {
            Assert.IsFalse(BootTimeline.IsCalibrated, "Deberia empezar sin perfil.");

            var step = Noop("etapa medida");
            BootTimeline.BeginRun(Sequence(step));
            BootTimeline.BeginStep(step);
            BootTimeline.EndStep(0);
            BootTimeline.CompleteRun();

            BootTimeline.BeginRun(Sequence(step));
            Assert.IsTrue(BootTimeline.IsCalibrated,
                "El segundo arranque no leyo lo que midio el primero.");

            BootTimeline.ClearWeights();
            BootTimeline.BeginRun(Sequence(step));
            Assert.IsFalse(BootTimeline.IsCalibrated, "'boot recalibrar' no borro el perfil.");
        }
    }
}
