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

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            GameplaySceneName = _gameplaySceneName;
            InitializeCoreServices();
        }

        private void Start()
        {
            LoadMainMenu();
        }

        private void InitializeCoreServices()
        {
            Debug.Log("[Bootstrap] Initializing core services...");

            // Service Locator registration will go here as services are migrated:
            // - SaveService
            // - AudioService
            // - InputService
            // - AssetService (Addressables)

            Debug.Log("[Bootstrap] Core services initialized.");
        }

        private void LoadMainMenu()
        {
            Debug.Log($"[Bootstrap] Loading scene: {_mainMenuSceneName}");
            SceneManager.LoadScene(_mainMenuSceneName);
        }
    }
}
