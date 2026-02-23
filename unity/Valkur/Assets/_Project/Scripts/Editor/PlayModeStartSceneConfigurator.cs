using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Ensures Play mode starts from Bootstrap so MainMenu flow is always respected.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeStartSceneConfigurator
    {
        private const string BootstrapScenePath = "Assets/_Project/Scenes/Bootstrap.unity";

        static PlayModeStartSceneConfigurator()
        {
            EnsureBootstrapAsPlayModeStartScene();
        }

        [MenuItem("Valkur/Scenes/Set Play Mode Start Scene (Bootstrap)")]
        public static void EnsureBootstrapAsPlayModeStartScene()
        {
            var bootstrapScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath);
            if (bootstrapScene == null)
            {
                Debug.LogWarning($"[PlayModeStartSceneConfigurator] Bootstrap scene not found at '{BootstrapScenePath}'.");
                return;
            }

            if (EditorSceneManager.playModeStartScene == bootstrapScene)
                return;

            EditorSceneManager.playModeStartScene = bootstrapScene;
            Debug.Log($"[PlayModeStartSceneConfigurator] Play mode start scene set to '{BootstrapScenePath}'.");
        }
    }
}
