using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Valkur.Editor.Testing
{
    /// <summary>
    /// Writes one line per test of every run to <c>Library/ValkurTestResults/</c>: result,
    /// duration and full name, as tab-separated text.
    ///
    /// <para><b>Why it exists.</b> The MCP bridge cannot carry per-test detail for a suite of
    /// nine thousand tests, so "which tests are slow" and "which tests were already red before
    /// my change" had no answer short of bisecting by namespace. A file on disk answers both,
    /// for any run however it was started (Test Runner window, MCP, <c>-runTests</c>).</para>
    ///
    /// <para><b>What it writes.</b> <c>last-EditMode.tsv</c> / <c>last-PlayMode.tsv</c> is
    /// overwritten by every run of that mode; a timestamped copy is kept beside it so two runs
    /// can be compared. <c>Library/</c> is machine state and is never committed.</para>
    ///
    /// <para>A partial run (a filter) writes a partial file. Read the header line, which
    /// records how many tests the run contained, before treating the file as the whole suite.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunRecorder
    {
        public const string Folder = "Library/ValkurTestResults";

        static TestRunRecorder()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
        }

        /// <summary>Writes <paramref name="result"/> as TSV. Public so a probe can re-save a result.</summary>
        public static string Write(ITestResultAdaptor result, TestMode mode)
        {
            var sb = new StringBuilder(1 << 20);
            var counts = new int[3];
            sb.Append("# mode=").Append(mode)
              .Append(" finished=").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
              .Append('\n');
            sb.Append("result\tseconds\tfullName\n");
            AppendLeaves(result, sb, counts);
            sb.Insert(0, $"# passed={counts[0]} failed={counts[1]} skipped={counts[2]}\n");

            Directory.CreateDirectory(Folder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var text = sb.ToString();
            File.WriteAllText(Path.Combine(Folder, $"last-{mode}.tsv"), text);
            var stamped = Path.Combine(Folder, $"{mode}-{stamp}.tsv");
            File.WriteAllText(stamped, text);
            return stamped;
        }

        private static void AppendLeaves(ITestResultAdaptor node, StringBuilder sb, int[] counts)
        {
            if (node == null) return;
            if (node.HasChildren)
            {
                foreach (var child in node.Children) AppendLeaves(child, sb, counts);
                return;
            }
            if (node.Test == null || node.Test.IsSuite) return;

            string status;
            switch (node.TestStatus)
            {
                case TestStatus.Passed: status = "Passed"; counts[0]++; break;
                case TestStatus.Failed: status = "Failed"; counts[1]++; break;
                default: status = node.TestStatus.ToString(); counts[2]++; break;
            }
            sb.Append(status).Append('\t')
              .Append(node.Duration.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
              .Append(node.FullName).Append('\n');
        }

        private sealed class Callbacks : ICallbacks
        {
            private TestMode _mode = TestMode.EditMode;

            /// <summary>The root suite reports no mode (0); the first descendant that has one decides.</summary>
            public void RunStarted(ITestAdaptor testsToRun)
            {
                _mode = ResolveMode(testsToRun) ?? TestMode.EditMode;
            }

            private static TestMode? ResolveMode(ITestAdaptor node)
            {
                if (node == null) return null;
                if (node.TestMode == TestMode.EditMode || node.TestMode == TestMode.PlayMode) return node.TestMode;
                if (!node.HasChildren) return null;
                foreach (var child in node.Children)
                {
                    var mode = ResolveMode(child);
                    if (mode.HasValue) return mode;
                }
                return null;
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                try { Write(result, _mode); }
                catch (Exception e) { Debug.LogWarning($"[TestRunRecorder] could not write results: {e.Message}"); }
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
