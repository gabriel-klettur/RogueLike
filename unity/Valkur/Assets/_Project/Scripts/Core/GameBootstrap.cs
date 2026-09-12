using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Valkur.Core
{
    /// <summary>
    /// Entry point for the game. Lives in the Bootstrap scene.
    /// Initializes core services and loads the main menu scene.
    /// Equivalent to Python's GameInitializer pipeline.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Scene Configuration")]
        [SerializeField] private string _mainMenuSceneName = "MainMenu";
        [SerializeField] private string _gameplaySceneName = "MainGameplay";

        public static string GameplaySceneName { get; private set; }

        /// <summary>
        /// Installed by the UI assembly. Offered the chance to show a brand plane before the
        /// menu; it answers <c>true</c> if it took the job and will call the continuation itself,
        /// and <c>false</c> to decline — in which case the menu loads immediately, exactly as it
        /// did before this existed.
        ///
        /// <para>A relay for the same reason <see cref="SceneTransitionManager.LoadSceneHandler"/>
        /// is one: <c>Valkur.Core</c> may not reference <c>Valkur.UI</c>, and Core must not learn
        /// that a splash exists. Declining is a NORMAL answer, not an error — it is a static
        /// delegate with Domain Reload off, so a handler installed by one Play session is still
        /// here in the Editor afterwards.</para>
        /// </summary>
        public static Func<Action, bool> SplashHandler;

        /// <summary>
        /// Static mutable state with Domain Reload off — and this one is a DELEGATE, which is
        /// the case the rule exists for: a handler installed by one Play session is still
        /// installed in the Editor afterwards, holding whatever it closed over. A direct
        /// assignment rather than a helper call, because <c>DomainReloadStaticResetTests</c>
        /// reads this method's raw IL and only recognises a <c>stsfld</c>.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SplashHandler = null;
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            GameplaySceneName = _gameplaySceneName;
            InitializeCoreServices();
        }

        private void Start()
        {
            var handler = SplashHandler;
            if (handler != null)
            {
                bool taken = false;
                try { taken = handler(LoadMainMenu); }
                catch (Exception e)
                {
                    Debug.LogError($"[Bootstrap] El plano de marca fallo: {e.Message}");
                    taken = false;
                }
                // A splash that threw must not cost the player the game. Falling through is the
                // same decision the scene router makes about a declined load.
                if (taken) return;
            }
            LoadMainMenu();
        }

        /// <summary>
        /// Applies the settings Unity itself owns, before anything draws a frame.
        ///
        /// <para>What this replaced was two <c>Debug.Log</c> lines around a comment listing four
        /// services it never registered — the authored-and-inert shape this project records a
        /// dozen times. It was harmless, because the services are created elsewhere, and the
        /// method still claimed to do something it did not.</para>
        ///
        /// <para>V-sync and the frame cap belong here rather than in the menu: they are engine
        /// state, they are read from a file the player owns, and applying them only once a menu
        /// has been built means the first seconds of the game ignore them.</para>
        /// </summary>
        private void InitializeCoreServices()
        {
            GameSettings.Instance?.ApplyVideoSettings();
        }

        private void LoadMainMenu()
        {
            Debug.Log($"[Bootstrap] Loading scene: {_mainMenuSceneName}");
            SceneTransitionManager.LoadScene(_mainMenuSceneName);
        }
    }
}
