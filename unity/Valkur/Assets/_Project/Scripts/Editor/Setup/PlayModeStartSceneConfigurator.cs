using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
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
        private const string MtpTestRunStatusTypeName = "MCPForUnity.Editor.Services.TestRunStatus, MCPForUnity.Editor";

        static PlayModeStartSceneConfigurator()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // Defer until AssetDatabase has finished its initial import — otherwise
            // LoadAssetAtPath returns null on cold-start and emits a false warning.
            EditorApplication.delayCall += EnsureBootstrapAsPlayModeStartScene;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    EnsureBootstrapAsPlayModeStartScene();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    EditorApplication.delayCall += EnsureBootstrapAsPlayModeStartScene;
                    break;
            }
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

        private static bool IsRunningEditorTests()
        {
            if (IsRunningCommandLineTests())
                return true;

            if (IsMcpPlayModeTestRunPendingOrActive())
                return true;

            var method = typeof(TestRunnerApi).GetMethod("IsRunActive",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method == null)
                return false;

            return method.Invoke(null, null) is bool isRunning && isRunning;
        }

        private static bool IsMcpPlayModeTestRunPendingOrActive()
        {
            var testRunStatusType = System.Type.GetType(MtpTestRunStatusTypeName);
            if (testRunStatusType == null)
                return false;

            var isRunningProp = testRunStatusType.GetProperty("IsRunning",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var modeProp = testRunStatusType.GetProperty("Mode",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (isRunningProp == null || modeProp == null)
                return false;

            if (isRunningProp.GetValue(null) is not bool isRunning || !isRunning)
                return false;

            var mode = modeProp.GetValue(null);
            return mode != null && string.Equals(mode.ToString(), TestMode.PlayMode.ToString(), System.StringComparison.Ordinal);
        }

        [MenuItem("Valkur/Setup/Set Play Mode Start Scene (Bootstrap)")]
        public static void EnsureBootstrapAsPlayModeStartScene()
        {
            if (IsRunningEditorTests())
            {
                EditorSceneManager.playModeStartScene = null;
                Debug.Log("[PlayModeStartSceneConfigurator] Test run detected; cleared playModeStartScene so PlayMode tests can run.");
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
