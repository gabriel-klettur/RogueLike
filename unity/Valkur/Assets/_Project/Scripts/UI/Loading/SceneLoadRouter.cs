using UnityEngine;
using Valkur.Core;

namespace Valkur.UI.Loading
{
    /// <summary>
    /// Installs the loading screen as the handler for every scene transition in the
    /// game, once, at startup.
    ///
    /// <para><b>The defect this closes.</b> The loading screen was reachable from
    /// exactly two places — the main menu's "Nueva partida" and its load panel — while
    /// the game has six scene transitions. The other four (pause &gt; New Game, pause
    /// &gt; Exit, a <c>ZonePortal</c> carrying a destination scene, and the very first
    /// Bootstrap &gt; MainMenu hop) called <c>SceneManager.LoadScene</c> SYNCHRONOUSLY,
    /// which re-ran the entire seventy-step gameplay boot behind a frozen frame with
    /// nothing on screen at all. Restarting from the pause menu was, measurably, the
    /// worst-looking thing the game did.</para>
    ///
    /// <para><b>Why a relay and not a direct call.</b> <c>SceneTransitionManager</c>
    /// lives in <c>Valkur.Core</c>, which may not reference <c>Valkur.UI</c>; the
    /// dependency rule is not negotiable and the project already answers this exact
    /// question twice, with <c>LoadingReporter</c> and with
    /// <c>DraggablePanel</c>'s state sink. Core exposes a delegate, UI fills it,
    /// and Core never learns a loading screen exists — so a scene with no UI
    /// assembly loaded still transitions, through the synchronous fallback.</para>
    ///
    /// <para><see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/> rather than
    /// <c>AfterSceneLoad</c>: the very first transition in the game is
    /// <c>GameBootstrap.Start</c>, which runs in the first scene, and a hook installed
    /// after that scene loads would miss it.</para>
    /// </summary>
    public static class SceneLoadRouter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneTransitionManager.LoadSceneHandler = Route;
        }

        /// <summary>
        /// Offer the load to the loading screen. Answering <c>false</c> is a normal
        /// outcome, not a failure, and <c>SceneTransitionManager</c> reads it as
        /// "load this directly" — a transition must never be silently dropped, because
        /// a menu button that does nothing is worse than one that stutters.
        ///
        /// The Play Mode check is load-bearing and was found by a red test rather than
        /// by reasoning: this delegate is STATIC and Domain Reload is off, so the
        /// handler a Play session installs is still installed in the Editor afterwards.
        /// An EditMode fixture that exercises the pause menu's Exit therefore reached
        /// straight into <c>DontDestroyOnLoad</c>, which Unity refuses outside Play Mode.
        /// </summary>
        private static bool Route(string sceneName)
        {
            if (!Application.isPlaying) return false;
            if (!NeedsLoadingScreen(sceneName)) return false;
            return LoadingScreenController.Show(sceneName);
        }

        /// <summary>
        /// Which scenes actually earn a loading screen: the ones that run a boot
        /// sequence behind them.
        ///
        /// <para><b>ONE DOOR IS NOT ONE CURTAIN, and conflating the two was a regression
        /// this router shipped.</b> Routing every transition through the screen is right
        /// — that is what gives pause &gt; New Game and a scene-carrying ZonePortal the
        /// seventy stages of feedback they never had. SHOWING it for every transition is
        /// not: the main menu loads in a blink, so the player got a full loading screen
        /// (bar, tip, minimum display, fade) in front of it and then a second one the
        /// moment they pressed New Game. Two screens back to back at startup, the first
        /// of which reported nothing because there was nothing to report.</para>
        ///
        /// <para>The scene name comes from <see cref="GameBootstrap.GameplaySceneName"/>,
        /// which the Bootstrap scene publishes from its own serialized field, so this
        /// tracks whatever that field says rather than being a second copy of it. The
        /// literal is the fallback for entering the gameplay scene directly, where
        /// <c>GameBootstrap</c> never ran.</para>
        ///
        /// <para>Going the OTHER way — gameplay back to the menu — stays on the direct
        /// path deliberately. It is a synchronous load either way, and a loading screen
        /// in front of a synchronous load is a still frame with a bar on it: it cannot
        /// report progress it is not being told about.</para>
        /// </summary>
        public static bool NeedsLoadingScreen(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName)) return false;
            string gameplay = GameBootstrap.GameplaySceneName;
            if (string.IsNullOrWhiteSpace(gameplay)) gameplay = FallbackGameplayScene;
            return string.Equals(sceneName, gameplay, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Used only when <c>GameBootstrap</c> has not run — entering the gameplay scene
        /// straight from the Editor. Kept beside the property it stands in for so the two
        /// cannot drift apart silently.
        /// </summary>
        private const string FallbackGameplayScene = "MainGameplay";
    }
}
