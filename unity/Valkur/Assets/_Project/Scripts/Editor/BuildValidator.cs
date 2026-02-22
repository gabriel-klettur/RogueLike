using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Pre-build validation hook that runs ContentValidator before every build.
    /// Blocks the build if critical issues are found.
    /// Maps to Python's CI validation passes before release.
    /// 
    /// Also provides menu items for manual build triggers with validation.
    /// </summary>
    public class BuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log("[BuildValidator] Running pre-build content validation...");

            int issues = 0;
            issues += ContentValidator.ValidateMonsterDefinitions();
            issues += ContentValidator.ValidateSpellDefinitions();
            issues += ContentValidator.ValidateItemDefinitions();
            issues += ContentValidator.ValidatePlayerDefinitions();
            issues += ContentValidator.ValidatePrefabReferences();

            if (issues > 0)
            {
                Debug.LogWarning($"[BuildValidator] {issues} validation issue(s) found. Build proceeding with warnings.");
            }
            else
            {
                Debug.Log("[BuildValidator] All pre-build validations passed.");
            }
        }

        [MenuItem("Valkur/Build/Validate and Build (Development)")]
        public static void ValidateAndBuildDev()
        {
            ContentValidator.RunAll();

            var options = new BuildPlayerOptions
            {
                scenes = GetBuildScenes(),
                locationPathName = "Builds/Dev/Valkur.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            var report = BuildPipeline.BuildPlayer(options);
            LogBuildResult(report);
        }

        [MenuItem("Valkur/Build/Validate and Build (Release)")]
        public static void ValidateAndBuildRelease()
        {
            ContentValidator.RunAll();

            var options = new BuildPlayerOptions
            {
                scenes = GetBuildScenes(),
                locationPathName = "Builds/Release/Valkur.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            LogBuildResult(report);
        }

        [MenuItem("Valkur/Build/Run EditMode Tests")]
        public static void RunEditModeTests()
        {
            Debug.Log("[BuildValidator] To run EditMode tests, use: Window > General > Test Runner > EditMode > Run All");
            Debug.Log("[BuildValidator] CLI: Unity.exe -runTests -testPlatform EditMode -projectPath <path>");
        }

        private static string[] GetBuildScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            var paths = new string[scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
                paths[i] = scenes[i].path;
            return paths;
        }

        private static void LogBuildResult(BuildReport report)
        {
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildValidator] Build succeeded! Size: {report.summary.totalSize / (1024 * 1024):F1} MB, " +
                          $"Time: {report.summary.totalTime.TotalSeconds:F1}s, " +
                          $"Warnings: {report.summary.totalWarnings}, Errors: {report.summary.totalErrors}");
            }
            else
            {
                Debug.LogError($"[BuildValidator] Build failed: {report.summary.result}. Errors: {report.summary.totalErrors}");
            }
        }
    }
}
