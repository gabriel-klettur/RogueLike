using System.Collections.Generic;
using NUnit.Framework;
using Valkur.Core.Boot;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// The loading bar divided into etapas: segment shares, the scene share, and the mappings
    /// that turn scene and boot progress into bar fractions. All pure arithmetic.
    /// </summary>
    [TestFixture]
    public class BootPlanTests
    {
        private static List<BootStep> Phased(params (string phase, string label)[] steps)
        {
            var b = new BootSequenceBuilder();
            foreach (var (phase, label) in steps)
            {
                b.Phase(phase);
                b.Add(BootStep.Of(label, () => { }));
            }
            return b.Steps;
        }

        [Test]
        public void ConsecutiveStepsOfOnePhase_AreOneSegment_SizedByTheirWeights()
        {
            var steps = Phased(("A", "a1"), ("A", "a2"), ("B", "b1"));
            var plan = BootPlan.FromSteps(steps, new List<float> { 10f, 30f, 60f }, new List<float> { 10f, 30f, 60f });

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual("A", plan.Segments[0].Name);
            Assert.AreEqual(2, plan.Segments[0].StepCount);
            Assert.AreEqual(0.4f, plan.Segments[0].End, 1e-4f, "El divisor no cae donde se detiene la barra.");
            Assert.AreEqual(1f, plan.Segments[1].End, 1e-4f);
            Assert.IsTrue(plan.IsTimed);
            Assert.AreEqual(100f, plan.TotalPredictedMs, 1e-3f);
            Assert.AreEqual(1, plan.IndexOfStep(2));
            Assert.AreEqual(1, plan.IndexAt(0.4f), "Un limite pertenece a la etapa siguiente.");
        }

        [Test]
        public void AMostlyUnmeasuredSequence_PromisesNoTime()
        {
            var steps = Phased(("A", "a"), ("B", "b"), ("C", "c"));
            var plan = BootPlan.FromSteps(steps, new List<float> { 1f, 1f, 1f }, new List<float> { 5f, 0f, 0f });
            Assert.IsFalse(plan.IsTimed, "Sin medidas, la pantalla prometeria un tiempo que nadie ha medido.");
        }

        [Test]
        public void OneNewStep_DoesNotCostTheWholeEstimate()
        {
            var list = new List<(string, string)>();
            var ms = new List<float>();
            for (int i = 0; i < 40; i++) { list.Add(("P" + (i / 10), "s" + i)); ms.Add(i == 7 ? 0f : 20f); }
            var steps = Phased(list.ToArray());
            var plan = BootPlan.FromSteps(steps, ms, ms);
            Assert.IsTrue(plan.IsTimed);
        }

        [Test]
        public void WithoutHistory_TheSceneKeepsItsLegacyShare()
        {
            var screen = BootScreenPlan.Compose(-1f, -1f, BootPlan.Empty);
            Assert.AreEqual(BootScreenPlan.FallbackSceneShare, screen.SceneShare, 1e-4f);
            Assert.AreEqual(1, screen.SegmentCount);
        }

        [Test]
        public void TheSceneShare_IsItsPredictedTime_OverTheWholeLoad()
        {
            var boot = BootPlan.FromPhases(new List<string> { "A", "B" }, new List<float> { 1000f, 2000f });
            var screen = BootScreenPlan.Compose(800f, 200f, boot);

            Assert.AreEqual(0.25f, screen.SceneShare, 1e-3f);
            Assert.AreEqual(0.8f, screen.LoadShare, 1e-3f);
            Assert.AreEqual(0.20f, screen.MapSceneLoad(1f), 1e-3f);
            Assert.AreEqual(0.25f, screen.MapActivation(1f), 1e-3f);
            Assert.AreEqual(0.25f, screen.MapBoot(0f), 1e-3f);
            Assert.AreEqual(1f, screen.MapBoot(1f), 1e-3f);
            Assert.AreEqual(4000f, screen.TotalPredictedMs, 1e-2f);

            var starts = new List<float>();
            screen.SegmentStarts(starts);
            Assert.AreEqual(3, starts.Count);
            Assert.AreEqual(0.5f, starts[2], 1e-3f);
            Assert.AreEqual(BootPlan.ScenePhase, screen.SegmentName(0));
            Assert.AreEqual(2, screen.SegmentIndexAt(0.6f));
        }

        [Test]
        public void ReplacingTheBootPlan_NeverMovesTheSceneShare()
        {
            var predicted = BootScreenPlan.Compose(800f, 200f,
                BootPlan.FromPhases(new List<string> { "A" }, new List<float> { 3000f }));
            var live = predicted.WithBoot(BootPlan.FromPhases(new List<string> { "A", "B" }, new List<float> { 100f, 100f }));
            Assert.AreEqual(predicted.SceneShare, live.SceneShare, 1e-5f,
                "Mover la parte de la escena al llegar el plan real movería lo ya rellenado.");
        }

        [Test]
        public void ThePredictionIsAMovingAverage_NotTheLastBoot()
        {
            Assert.AreEqual(500f, BootTimeline.Blend(0f, 0, 500f), 1e-3f, "Una primera muestra entra tal cual.");
            float blended = BootTimeline.Blend(1000f, 3, 2000f);
            Assert.Greater(blended, 1000f);
            Assert.Less(blended, 1500f, "Un arranque atipico no debe arrastrar la prevision entera.");
        }
    }
}
