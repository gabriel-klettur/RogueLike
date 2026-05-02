using System.Threading;
using System.Threading.Tasks;
using Valkur.Core.WorldContext;

namespace Valkur.Gameplay.Bootstrap
{
    /// <summary>
    /// One unit of the gameplay scene boot sequence. Today
    /// <see cref="GameplaySceneSetup"/> runs ~32 distinct steps inline; Phase 0
    /// extracts each into an <see cref="IBootstrapStep"/> so they become:
    ///
    /// <list type="bullet">
    ///   <item><b>Reorderable</b>: the pipeline owns the order, not a 600-line method.</item>
    ///   <item><b>Testable</b>: an EditMode test can run a 3-step subset in isolation.</item>
    ///   <item><b>Async-aware</b>: Phase 2 chunk streaming needs steps that load on
    ///   demand without blocking the main thread. The <see cref="Task"/> return
    ///   makes that possible without revisiting the contract.</item>
    /// </list>
    ///
    /// During Phase 0 the steps still run synchronously — implementations return
    /// <see cref="Task.CompletedTask"/>. The async surface is reserved seating.
    /// </summary>
    public interface IBootstrapStep
    {
        /// <summary>Display name for logs and the loading-progress UI.</summary>
        string Name { get; }

        /// <summary>Execute this step against the supplied world context.
        /// Implementations should be idempotent — running twice in a row must
        /// not corrupt state — so a partial run can be retried.</summary>
        Task ExecuteAsync(IWorldContext ctx, BootstrapProgress progress, CancellationToken ct);
    }

    /// <summary>
    /// Progress sink the pipeline hands to each step. Steps call
    /// <see cref="Report"/> with a 0..1 value to feed the loading bar.
    /// Implementation defers to <see cref="Valkur.Core.LoadingReporter"/> so
    /// the existing UI keeps working.
    /// </summary>
    public sealed class BootstrapProgress
    {
        private readonly System.Action<string, float> _sink;

        public BootstrapProgress(System.Action<string, float> sink)
        {
            _sink = sink ?? ((_, __) => { });
        }

        public void Report(string status, float fraction01) => _sink(status, fraction01);
    }
}
