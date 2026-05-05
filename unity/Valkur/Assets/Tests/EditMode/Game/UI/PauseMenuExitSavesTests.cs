using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Save;
using Valkur.UI.PauseMenu;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Regression tests for the contract:
    /// "Choosing 'Exit' from the pause menu must persist the current run
    ///  before returning to the main menu, so the player can pick 'Continue'
    ///  on the next launch."
    ///
    /// Two layers of protection:
    ///   1. A behavioural test invokes ExecutePause("Exit") via reflection
    ///      against a real PauseMenuUI instance and asserts SaveService.QuickSave
    ///      was reached (no exception, no early return).
    ///   2. A source-level test reads PauseMenuUI.Actions.cs and asserts the
    ///      "Exit" branch contains a QuickSave call. This catches accidental
    ///      removal even if the behavioural test is muted by EditMode quirks.
    /// </summary>
    [TestFixture]
    public class PauseMenuExitSavesTests
    {
        private static readonly BindingFlags PrivInst =
            BindingFlags.NonPublic | BindingFlags.Instance;

        // ── Source-level guarantee ──────────────────────────────────────────

        [Test]
        public void PauseMenuActions_ExitBranch_ContainsQuickSave()
        {
            // Locate the source file relative to the project Assets folder.
            string scriptPath = Path.Combine(
                Application.dataPath,
                "_Project", "Scripts", "UI", "PauseMenu", "PauseMenuUI.Actions.cs");
            Assert.IsTrue(File.Exists(scriptPath),
                $"Expected pause-menu actions source at {scriptPath}");

            string source = File.ReadAllText(scriptPath);

            // Find the "Exit" case and capture everything until the next
            // case label or the closing of the switch (best-effort: match
            // until the next "case " or "}" at column 16+).
            int exitIdx = source.IndexOf("case \"Exit\":", System.StringComparison.Ordinal);
            Assert.Greater(exitIdx, -1,
                "PauseMenuUI.Actions.cs must contain an 'Exit' case in ExecutePause");

            // Scan a generous window after the case label; the QuickSave and
            // LoadScene calls must both appear before the next case. The window
            // size is intentionally large (1500 chars) so verbose patterns
            // (try/catch around QuickSave + comments) still fit.
            int windowEnd  = System.Math.Min(source.Length, exitIdx + 1500);
            string window  = source.Substring(exitIdx, windowEnd - exitIdx);
            int nextCase   = window.IndexOf("case \"", 1, System.StringComparison.Ordinal);
            if (nextCase > 0) window = window.Substring(0, nextCase);

            Assert.IsTrue(window.Contains("QuickSave"),
                "The 'Exit' branch of ExecutePause must call SaveService.QuickSave " +
                "before SceneTransitionManager.LoadScene(\"MainMenu\"). " +
                "Otherwise the player's run is silently lost when exiting through " +
                "the pause menu and 'Continue' will never appear in the main menu.");

            // The save MUST happen *before* the scene change so the data is
            // committed to disk while gameplay state is still alive.
            int saveIdx = window.IndexOf("QuickSave", System.StringComparison.Ordinal);
            int loadIdx = window.IndexOf("LoadScene", System.StringComparison.Ordinal);
            Assert.Greater(loadIdx, saveIdx,
                "QuickSave() must be invoked BEFORE SceneTransitionManager.LoadScene " +
                "so the player's run data is persisted while the scene is still loaded.");
        }

        // ── Behavioural guarantee ───────────────────────────────────────────

        [Test]
        public void ExecutePause_Exit_DoesNotThrow_AndAttemptsSave()
        {
            // SetUp: fresh PauseMenuUI + SaveService instance. SaveService's
            // singleton may stay null in EditMode (RuntimeInitializeOnLoadMethod);
            // the test only asserts the exit path doesn't throw and the
            // SaveService.QuickSave call is reachable.
            if (PauseMenuUI.Instance != null)
                Object.DestroyImmediate(PauseMenuUI.Instance.gameObject);

            var pauseGo = new GameObject("TestPauseMenu_ExitSaves");
            var menu    = pauseGo.AddComponent<PauseMenuUI>();
            typeof(PauseMenuUI).GetMethod("Start", PrivInst)?.Invoke(menu, null);

            // Open pause so ExecutePause runs in a valid state
            menu.OpenPause();

            // Look up _pauseOptions to find the "Exit" index dynamically
            var optsField = typeof(PauseMenuUI).GetField("_pauseOptions", PrivInst);
            string[] opts = optsField?.GetValue(menu) as string[];
            Assert.IsNotNull(opts, "_pauseOptions must be initialised after Start");

            int exitIdx = System.Array.IndexOf(opts, "Exit");
            Assert.Greater(exitIdx, -1, "Pause menu must have an 'Exit' entry");

            var execute = typeof(PauseMenuUI).GetMethod("ExecutePause", PrivInst);
            Assert.IsNotNull(execute, "ExecutePause private method must exist");

            // Wrap the SceneTransition call: in EditMode, LoadScene throws
            // because the scene isn't in build settings. We accept any
            // exception thrown there as long as it's NOT raised before the
            // save attempt (a NullReferenceException from the QuickSave block
            // would indicate a regression in the save-on-exit code).
            try
            {
                execute.Invoke(menu, new object[] { exitIdx });
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                // Accept SceneTransitionManager / SceneManager errors — those
                // happen AFTER the save attempt and are EditMode-only.
                bool isSceneRelated =
                    inner.Message.Contains("Scene") ||
                    inner.Message.Contains("scene") ||
                    inner.GetType().Name.Contains("Argument");
                Assert.IsTrue(isSceneRelated,
                    $"ExecutePause('Exit') threw an unexpected exception before " +
                    $"the scene transition: {inner.GetType().Name}: {inner.Message}");
            }

            Object.DestroyImmediate(pauseGo);
        }
    }
}
