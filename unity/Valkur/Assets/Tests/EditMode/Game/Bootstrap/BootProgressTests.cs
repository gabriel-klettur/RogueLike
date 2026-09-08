using NUnit.Framework;
using Valkur.Core.Boot;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// The arithmetic behind the loading bar.
    ///
    /// <para>These exist because of a measured, shipped defect: the bar's denominator
    /// was a hand-maintained constant (<c>SetupStepTotal = 53</c>) describing a
    /// sequence that had grown to 70 steps, so it reached 100 % at 75.7 % of the work
    /// and the last seventeen stages — the heaviest ones, 301 buildings included —
    /// ran with the bar pinned full. Nothing failed, because the overflow was
    /// silenced by a <c>Mathf.Clamp01</c>.</para>
    ///
    /// <para>The property that makes that class of bug impossible rather than merely
    /// fixed is <see cref="ProgressCannotReachOne_UntilComplete"/>: 100 % is not a
    /// number the bar can arrive at by counting, it is a statement that the game is
    /// ready, and only <see cref="BootProgress.Complete"/> can make it.</para>
    /// </summary>
    [TestFixture]
    public class BootProgressTests
    {
        private static BootProgress WithUniformSteps(int count)
        {
            var p = new BootProgress();
            for (int i = 0; i < count; i++) p.Add(1f);
            return p;
        }

        [Test]
        public void EmptyProgress_ReadsZero_AndDoesNotDivideByZero()
        {
            var p = new BootProgress();
            Assert.AreEqual(0f, p.Fraction, 1e-6f);
            Assert.AreEqual(0, p.StepCount);
        }

        [Test]
        public void TotalIsDerivedFromTheStepsQueued_NotDeclared()
        {
            var p = new BootProgress();
            p.Add(2f); p.Add(3f); p.Add(5f);
            Assert.AreEqual(3, p.StepCount);
            Assert.AreEqual(10f, p.TotalWeight, 1e-4f);
        }

        [Test]
        public void ProgressIsMonotonic_AcrossAWholeRun()
        {
            var p = WithUniformSteps(70);
            float previous = -1f;
            for (int i = 0; i < 70; i++)
            {
                float f = p.Fraction;
                Assert.GreaterOrEqual(f, previous, $"La barra retrocedio en el paso {i}.");
                previous = f;
                p.CompleteStep(i);
            }
        }

        /// <summary>
        /// The guarantee that replaces the drifted constant. Every step of a
        /// seventy-step run may finish and the bar still refuses to say "ready".
        /// </summary>
        [Test]
        public void ProgressCannotReachOne_UntilComplete()
        {
            var p = WithUniformSteps(70);
            for (int i = 0; i < 70; i++) p.CompleteStep(i);

            Assert.Less(p.Fraction, 1f,
                "La barra llego al 100 % contando pasos. El 100 % solo puede venir de Complete().");
            Assert.AreEqual(BootProgress.MaxBeforeComplete, p.Fraction, 1e-4f);

            p.Complete();
            Assert.AreEqual(1f, p.Fraction, 1e-6f);
        }

        [Test]
        public void WeightsDecideHowFarEachStepMovesTheBar()
        {
            var p = new BootProgress();
            p.Add(1f);   // a registration
            p.Add(99f);  // the world load

            p.CompleteStep(0);
            Assert.AreEqual(0.01f, p.Fraction, 1e-4f,
                "Un paso barato movio la barra como si costase lo mismo que cargar el mundo.");

            p.CompleteStep(1);
            Assert.AreEqual(BootProgress.MaxBeforeComplete, p.Fraction, 1e-4f);
        }

        [Test]
        public void CompletingTheSameStepTwice_DoesNotOvershoot()
        {
            var p = WithUniformSteps(4);
            p.CompleteStep(0);
            p.CompleteStep(0);
            p.CompleteStep(0);
            Assert.LessOrEqual(p.Fraction, BootProgress.MaxBeforeComplete);
        }

        /// <summary>
        /// A runner that grew a step mid-run is a bug to be read in the timeline, not
        /// a reason to throw in the middle of somebody's boot.
        /// </summary>
        [Test]
        public void OutOfRangeSteps_AreIgnoredRatherThanThrown()
        {
            var p = WithUniformSteps(2);
            Assert.DoesNotThrow(() => p.CompleteStep(-1));
            Assert.DoesNotThrow(() => p.CompleteStep(99));
            Assert.AreEqual(0f, p.Fraction, 1e-6f);
        }

        [Test]
        public void CompleteWins_EvenWithStepsOutstanding()
        {
            var p = WithUniformSteps(10);
            p.CompleteStep(0);
            p.Complete();
            Assert.AreEqual(1f, p.Fraction, 1e-6f);
            Assert.IsTrue(p.IsComplete);
        }

        [Test]
        public void Reset_ReturnsAFreshModel()
        {
            var p = WithUniformSteps(3);
            p.CompleteStep(0);
            p.Complete();
            p.Reset();
            Assert.AreEqual(0, p.StepCount);
            Assert.AreEqual(0f, p.TotalWeight, 1e-6f);
            Assert.AreEqual(0f, p.Fraction, 1e-6f);
            Assert.IsFalse(p.IsComplete);
        }
    }
}
