using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Editor.Validation
{
    /// <summary>
    /// Tells <see cref="WorldDataWriteGuard"/> when a test run is happening, and disarms every
    /// real-path opt-in before each test.
    ///
    /// <para><b>Why the test framework rather than <c>Application.isPlaying</c>.</b> The guards
    /// this replaces asked whether the editor was in Play Mode, which answers a different
    /// question in both directions: an editor tool a human clicks is not in Play Mode and must be
    /// allowed to write shipped data, and a PlayMode TEST is in Play Mode and must not. The
    /// framework knows which of the two is happening; nothing else does.</para>
    ///
    /// <para><b>Why <c>TestStarted</c> and not just the run boundaries.</b> The opt-in used to be
    /// a bare static that a fixture set in <c>[SetUp]</c> and cleared in <c>[TearDown]</c>. A
    /// TearDown that throws before that line — and one of them deletes files, which on
    /// StreamingAssets fails with Win32 1224 while Unity holds the mapping — leaves it armed for
    /// every test that follows in the session. Clearing before each test is what turns that from a
    /// silent session-wide hole into a scope one fixture cannot escape.</para>
    ///
    /// <para><c>[InitializeOnLoad]</c> so it is registered however the run is started: the Test
    /// Runner window, the MCP bridge, or <c>-runTests</c> in batch mode. If registration ever
    /// stopped happening the protection would vanish silently, which is why
    /// <c>WorldDataWriteGuardTests</c> asserts from INSIDE a test that the run is flagged.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class WorldDataWriteGuardTestHook
    {
        static WorldDataWriteGuardTestHook()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) => WorldDataWriteGuard.BeginTestRun();

            public void RunFinished(ITestResultAdaptor result)
            {
                WorldDataWriteGuard.DisarmAll();
                WorldDataWriteGuard.EndTestRun();
            }

            /// <summary>Fires before the fixture's own SetUp, so a fixture may still arm its scope.</summary>
            public void TestStarted(ITestAdaptor test) => WorldDataWriteGuard.DisarmAll();

            public void TestFinished(ITestResultAdaptor result) => WorldDataWriteGuard.DisarmAll();
        }
    }
}
