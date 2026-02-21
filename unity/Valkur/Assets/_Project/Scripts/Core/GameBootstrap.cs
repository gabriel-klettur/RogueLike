using UnityEngine;
using UnityEngine.SceneManagement;

namespace Valkur.Core
{
    /// <summary>
    /// Entry point for the game. Lives in the Bootstrap scene.
    /// Initializes core services and loads the gameplay scene.
    /// Equivalent to Python's GameInitializer pipeline.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Scene Configuration")]
        [SerializeField] private string _gameplaySceneName = "MainGameplay";

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            InitializeCoreServices();
        }

        private void Start()
        {
            LoadGameplayScene();
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

        private void LoadGameplayScene()
        {
            Debug.Log($"[Bootstrap] Loading scene: {_gameplaySceneName}");
            SceneManager.LoadScene(_gameplaySceneName);
        }
    }
}
