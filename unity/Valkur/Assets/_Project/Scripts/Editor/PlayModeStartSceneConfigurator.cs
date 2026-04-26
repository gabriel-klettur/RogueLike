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
            // Defer until AssetDatabase has finished its initial import — otherwise
            // LoadAssetAtPath returns null on cold-start and emits a false warning.
            EditorApplication.delayCall += EnsureBootstrapAsPlayModeStartScene;
        }

        /// <summary>
        /// True when Unity was launched with <c>-runTests</c> (CI / batchmode).
        /// We must NOT install a Bootstrap → MainMenu start scene in that case
        /// because it hijacks every PlayMode test (the test runner waits for the
        /// start scene flow to settle before loading the test scene, but the
        /// game's full startup flow never returns control).
        /// </summary>
        private static bool IsRunningCommandLineTests()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-runTests") return true;
            }
            return false;
        }

        [MenuItem("Valkur/Setup/Set Play Mode Start Scene (Bootstrap)")]
        public static void EnsureBootstrapAsPlayModeStartScene()
        {
            if (IsRunningCommandLineTests())
            {
                EditorSceneManager.playModeStartScene = null;
                Debug.Log("[PlayModeStartSceneConfigurator] -runTests detected; cleared playModeStartScene so PlayMode tests can run.");
                return;
            }

            var bootstrapScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath);
            if (bootstrapScene == null)
            {
                // Only warn if the file is genuinely missing on disk; otherwise it's
                // a transient AssetDatabase state we should ignore.
                if (!System.IO.File.Exists(BootstrapScenePath))
                {
                    Debug.LogWarning($"[PlayModeStartSceneConfigurator] Bootstrap scene not found at '{BootstrapScenePath}'.");
                }
                return;
            }

            if (EditorSceneManager.playModeStartScene == bootstrapScene)
                return;

            EditorSceneManager.playModeStartScene = bootstrapScene;
            Debug.Log($"[PlayModeStartSceneConfigurator] Play mode start scene set to '{BootstrapScenePath}'.");
        }
    }
}
