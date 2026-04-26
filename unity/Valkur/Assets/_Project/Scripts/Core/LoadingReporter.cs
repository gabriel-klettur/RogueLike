using System;

namespace Valkur.Core
{
    /// <summary>
    /// Decoupled relay between Valkur.Gameplay (GameplaySceneSetup) and
    /// Valkur.UI.Loading (LoadingScreenController).
    ///
    /// Gameplay calls ReportStage / ReportGameplayReady.
    /// The loading screen subscribes to OnStageProgress / OnGameplayReady in its Start().
    /// Both assemblies reference Valkur.Core — no circular dependency.
    /// </summary>
    public static class LoadingReporter
    {
        /// <summary>Raised after each initialization stage. Args: (message, 0..1 progress).</summary>
        public static Action<string, float> OnStageProgress;

        /// <summary>Raised when all gameplay systems are ready.</summary>
        public static Action OnGameplayReady;

        public static void ReportStage(string message, float progress)
            => OnStageProgress?.Invoke(message, progress);

        public static void ReportGameplayReady()
            => OnGameplayReady?.Invoke();

        /// <summary>Called by LoadingScreenController.OnDestroy to avoid stale references.</summary>
        public static void Clear()
        {
            OnStageProgress = null;
            OnGameplayReady = null;
        }
    }
}
