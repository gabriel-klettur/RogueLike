using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Valkur.Core.WorldContext;
using Valkur.Gameplay.Bootstrap;

namespace Valkur.Tests.EditMode.Gameplay.Bootstrap
{
    /// <summary>
    /// Pins the BootstrapPipeline contract:
    ///  • Steps execute in registration order.
    ///  • Each step receives the supplied <see cref="IWorldContext"/>.
    ///  • Progress is reported with monotonically non-decreasing fractions.
    ///  • An exception in step N halts the pipeline (steps &gt; N do not run)
    ///    and is rethrown so callers can surface it.
    ///  • Cancellation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    [TestFixture]
    public class BootstrapPipelineTests
    {
        private sealed class RecordingStep : IBootstrapStep
        {
            public string Name { get; }
            public List<string> Trace { get; }
            public IWorldContext SeenContext { get; private set; }
            public RecordingStep(string name, List<string> trace) { Name = name; Trace = trace; }
            public Task ExecuteAsync(IWorldContext ctx, BootstrapProgress p, CancellationToken ct)
            {
                SeenContext = ctx;
                Trace.Add(Name);
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingStep : IBootstrapStep
        {
            public string Name => "throws";
            public Task ExecuteAsync(IWorldContext ctx, BootstrapProgress p, CancellationToken ct)
                => throw new System.InvalidOperationException("boom");
        }

        // NUnit 3.5 in Unity 2022.3 silently rejects `async Task` test methods
        // with a "Method has non-void return value" runtime error. Block-on the
        // task synchronously instead. Steps are pure-CPU in these tests so
        // there is no deadlock risk.
        [Test]
        public void Steps_ExecuteInRegistrationOrder()
        {
            var trace = new List<string>();
            var pipeline = new BootstrapPipeline()
                .Add(new RecordingStep("a", trace))
                .Add(new RecordingStep("b", trace))
                .Add(new RecordingStep("c", trace));

            pipeline.ExecuteAsync(WorldContext.Global).GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, trace);
        }

        [Test]
        public void Steps_ReceiveTheSuppliedContext()
        {
            var step = new RecordingStep("only", new List<string>());
            var ctx = WorldContext.Scoped();
            new BootstrapPipeline().Add(step).ExecuteAsync(ctx).GetAwaiter().GetResult();
            Assert.AreSame(ctx, step.SeenContext);
        }

        // NUnit 3.5 (the version Unity ships) does not expose Assert.ThrowsAsync;
        // wait on the task synchronously and unwrap the AggregateException.
        private static System.Exception RunAndCaptureException(System.Func<Task> action)
        {
            try { action().GetAwaiter().GetResult(); return null; }
            catch (System.Exception ex) { return ex; }
        }

        [Test]
        public void FailingStep_HaltsPipelineAndRethrows()
        {
            var trace = new List<string>();
            var pipeline = new BootstrapPipeline()
                .Add(new RecordingStep("before", trace))
                .Add(new ThrowingStep())
                .Add(new RecordingStep("after", trace));

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var ex = RunAndCaptureException(() => pipeline.ExecuteAsync(WorldContext.Global));
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsInstanceOf<System.InvalidOperationException>(ex,
                "The originating exception must propagate, not be swallowed.");
            CollectionAssert.AreEqual(new[] { "before" }, trace,
                "Steps after the failing one must NOT execute.");
        }

        [Test]
        public void CancellationToken_StopsSubsequentSteps()
        {
            var trace = new List<string>();
            var pipeline = new BootstrapPipeline()
                .Add(new RecordingStep("first", trace))
                .Add(new RecordingStep("second", trace));

            var cts = new CancellationTokenSource();
            cts.Cancel();
            var ex = RunAndCaptureException(() => pipeline.ExecuteAsync(WorldContext.Global, ct: cts.Token));
            Assert.IsInstanceOf<System.OperationCanceledException>(ex,
                "A pre-cancelled token must throw OperationCanceledException.");
            CollectionAssert.IsEmpty(trace,
                "A pre-cancelled token must reject the pipeline before the first step runs.");
        }

        [Test]
        public void Progress_ReportsNonDecreasingFraction()
        {
            float lastFraction = -1f;
            int reports = 0;
            var progress = new BootstrapProgress((_, f) =>
            {
                Assert.GreaterOrEqual(f, lastFraction);
                lastFraction = f;
                reports++;
            });

            var trace = new List<string>();
            new BootstrapPipeline()
                .Add(new RecordingStep("a", trace))
                .Add(new RecordingStep("b", trace))
                .ExecuteAsync(WorldContext.Global, progress)
                .GetAwaiter().GetResult();

            Assert.GreaterOrEqual(reports, 2,
                "Progress must report at least once per step plus a final 'done'.");
            Assert.AreEqual(1f, lastFraction, 0.001f,
                "Final progress must be 1.0 after the last step.");
        }
    }
}
