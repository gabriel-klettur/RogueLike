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

        /// <summary>
        /// Raised right after <see cref="OnGameplayReady"/>, for SYSTEMS rather than the loading
        /// screen.
        ///
        /// <para>The screen owns <see cref="OnGameplayReady"/>: it ASSIGNS it in its Start —
        /// replacing whatever was there — and clears it on teardown. So a system that subscribed
        /// with <c>+=</c> before the scene load (the only moment a menu choice can arm something
        /// for the next boot) was erased the instant the screen started, and nothing said so. This
        /// channel is never assigned or cleared by the screen.</para>
        /// </summary>
        public static event Action OnGameplayReadyForSystems;

        public static void ReportStage(string message, float progress)
            => OnStageProgress?.Invoke(message, progress);

        public static void ReportGameplayReady()
        {
            OnGameplayReady?.Invoke();
            OnGameplayReadyForSystems?.Invoke();
        }

        /// <summary>
        /// Static delegates outlive a Play session with Domain Reload off, so the
        /// previous session's loading-screen handlers would fire against destroyed UI.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEnter()
        {
            Clear();
            OnGameplayReadyForSystems = null;
        }

        /// <summary>Called by LoadingScreenController.OnDestroy to avoid stale references. Leaves
        /// <see cref="OnGameplayReadyForSystems"/> alone: those listeners are not the screen's.</summary>
        public static void Clear()
        {
            OnStageProgress = null;
            OnGameplayReady = null;
        }
    }
}
