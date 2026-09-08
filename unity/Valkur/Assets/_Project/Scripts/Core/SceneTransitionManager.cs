using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Valkur.Core
{
    /// <summary>
    /// Centralized scene transition handler that ensures clean state before loading a new scene.
    /// Resets Time.timeScale, clears EntityRegistry, and provides a single entry point
    /// for all scene loads to prevent stale references and MissingReferenceExceptions.
    ///
    /// <b>It is also the ONE DOOR into a scene load, and that is new.</b> The loading
    /// screen used to be reachable only from the two main-menu buttons, so four of the
    /// game's six scene transitions — pause &gt; New Game, pause &gt; Exit, a
    /// <c>ZonePortal</c> with a destination scene, and the very first Bootstrap &gt;
    /// MainMenu hop — went through a synchronous <see cref="SceneManager.LoadScene"/>
    /// and re-ran the whole seventy-step gameplay boot behind a frozen frame with
    /// nothing on screen.
    ///
    /// <see cref="LoadSceneHandler"/> is the relay that fixes it without a circular
    /// assembly reference: <c>Valkur.UI</c> installs the loading screen into it at
    /// startup, and <c>Valkur.Core</c> never learns that a loading screen exists.
    /// Same shape as <see cref="LoadingReporter"/>, for the same reason.
    /// </summary>
    public static class SceneTransitionManager
    {
        /// <summary>
        /// Installed by the UI assembly. When present, every load is OFFERED to it;
        /// it answers <c>true</c> if it took the load and <c>false</c> to decline, and a
        /// decline falls through to the synchronous path below. Nothing depends on the
        /// loading screen existing.
        ///
        /// A bool rather than an <c>Action</c>, because declining is a NORMAL answer and
        /// not an error: this is a static delegate and Domain Reload is off, so a handler
        /// installed by one Play session is still here in the Editor afterwards, pointing
        /// at a screen that cannot be built outside Play Mode. Modelling that as an
        /// exception made an EditMode fixture fail on an error log for behaviour that was
        /// working exactly as designed.
        /// </summary>
        public static Func<string, bool> LoadSceneHandler;

        /// <summary>
        /// Load a scene by name with proper cleanup.
        /// All scene transitions should go through this method.
        /// </summary>
        public static void LoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("[SceneTransition] Refused a load with no scene name.");
                return;
            }

            var handler = LoadSceneHandler;
            if (handler != null)
            {
                // The loading screen owns the teardown too: it clears the registry and
                // the event bus itself, and it stands the EventSystem down at the exact
                // frame it flips allowSceneActivation. Doing it twice is harmless but
                // doing it HERE would clear the bus a whole async load early.
                bool taken = false;
                try
                {
                    taken = handler(sceneName);
                }
                catch (Exception ex)
                {
                    // A broken relay must never strand the player on the current scene.
                    Debug.LogError($"[SceneTransition] La pantalla de carga fallo con " +
                                   $"'{sceneName}' ({ex.Message}); se carga directamente.");
                }

                if (taken)
                {
                    Debug.Log($"[SceneTransition] Loading scene through loading screen: {sceneName}");
                    return;
                }
            }

            LoadSceneImmediate(sceneName);
        }

        /// <summary>
        /// The historical synchronous path. Public because the loading screen's own
        /// failure branch needs a way out that cannot recurse back into itself.
        /// </summary>
        public static void LoadSceneImmediate(string sceneName)
        {
            Debug.Log($"[SceneTransition] Loading scene: {sceneName}");

            // Reset time scale (may have been paused by death screen or pause menu)
            Time.timeScale = 1f;

            // Clear entity registry to prevent stale references
            EntityRegistry.Clear();

            // Clear global event bus to prevent subscriber leaks
            GameEvents.Clear();

            // Stand the persistent EventSystem down before the next scene awakes.
            // MainMenu.unity still ships its own; with ours enabled, its OnEnable
            // registers a second active EventSystem and uGUI logs "There can be only
            // one active Event System." RuntimeInputBootstrap's sceneLoaded hook
            // re-runs PersistentEventSystem.Ensure, which drops the duplicate and
            // re-enables ours. LoadingScreenController already does the same before
            // it flips allowSceneActivation.
            Input.PersistentEventSystem.Pause();

            SceneManager.LoadScene(sceneName);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // Static delegate with Domain Reload off: a handler from the previous Play
            // session points at a destroyed controller and would swallow every load.
            LoadSceneHandler = null;
        }
    }
}
