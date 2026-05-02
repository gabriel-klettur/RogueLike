using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Valkur.Core.WorldContext;

namespace Valkur.Gameplay.Bootstrap
{
    /// <summary>
    /// Runs an ordered list of <see cref="IBootstrapStep"/> against an
    /// <see cref="IWorldContext"/>. Phase 0 introduces this pipeline as a
    /// thin orchestrator — <see cref="GameplaySceneSetup"/> still owns its
    /// 32-step boot sequence inline, but new initialization work going
    /// forward should land as <see cref="IBootstrapStep"/> instances and
    /// be appended to a pipeline managed here. Phase 1 migrates the
    /// monolithic <c>RunSetupSequence</c> into the pipeline wholesale.
    ///
    /// Failure semantics: if a step throws, the pipeline logs the failure
    /// with the step name + index and rethrows. Subsequent steps do not
    /// execute. Idempotent step design (see <see cref="IBootstrapStep"/>)
    /// means a partial run can be retried by re-invoking
    /// <see cref="ExecuteAsync"/> without rewinding state.
    /// </summary>
    public sealed class BootstrapPipeline
    {
        private readonly List<IBootstrapStep> _steps = new List<IBootstrapStep>();

        public IReadOnlyList<IBootstrapStep> Steps => _steps;

        public BootstrapPipeline Add(IBootstrapStep step)
        {
            if (step != null) _steps.Add(step);
            return this;
        }

        public BootstrapPipeline AddRange(IEnumerable<IBootstrapStep> steps)
        {
            if (steps != null) _steps.AddRange(steps);
            return this;
        }

        public async Task ExecuteAsync(IWorldContext ctx,
                                       BootstrapProgress progress = null,
                                       CancellationToken ct = default)
        {
            if (ctx == null) ctx = WorldContext.Global;
            progress = progress ?? new BootstrapProgress((_, __) => { });

            for (int i = 0; i < _steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var step = _steps[i];
                float fraction = _steps.Count > 0 ? (float)i / _steps.Count : 0f;
                progress.Report(step.Name, fraction);
                try
                {
                    await step.ExecuteAsync(ctx, progress, ct);
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[BootstrapPipeline] Step #{i} '{step.Name}' failed: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            }
            progress.Report("Done", 1f);
        }
    }
}
