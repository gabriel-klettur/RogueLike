using UnityEngine;
using UnityEngine.SceneManagement;

namespace Valkur.Core
{
    /// <summary>
    /// Centralized scene transition handler that ensures clean state before loading a new scene.
    /// Resets Time.timeScale, clears EntityRegistry, and provides a single entry point
    /// for all scene loads to prevent stale references and MissingReferenceExceptions.
    /// </summary>
    public static class SceneTransitionManager
    {
        /// <summary>
        /// Load a scene by name with proper cleanup.
        /// All scene transitions should go through this method.
        /// </summary>
        public static void LoadScene(string sceneName)
        {
            Debug.Log($"[SceneTransition] Loading scene: {sceneName}");

            // Reset time scale (may have been paused by death screen or pause menu)
            Time.timeScale = 1f;

            // Clear entity registry to prevent stale references
            EntityRegistry.Clear();

            SceneManager.LoadScene(sceneName);
        }
    }
}
