using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game
{
    /// <summary>
    /// Regression guard against the duplicate-class / unbalanced-brace corruption
    /// that produced <c>CS1022 Type or namespace definition, or end-of-file expected</c>
    /// (originally hit on <c>MinimizedHUDTray.cs</c>: a stale second copy of the
    /// class was pasted after the namespace's closing brace).
    ///
    /// This test does a fast lexical scan of every <c>.cs</c> file under
    /// <c>Assets/_Project/Scripts</c> and asserts that <c>{</c> / <c>}</c> are
    /// balanced once string literals, char literals, and comments are stripped.
    ///
    /// It will NOT catch every C# syntax error — it is intentionally narrow,
    /// targeting the specific corruption pattern that has bitten us in the past.
    /// </summary>
    [TestFixture]
    public class BraceBalanceRegressionTests
    {
        // Roots to scan (relative to project root).
        private static readonly string[] ScanRoots =
        {
            "Assets/_Project/Scripts",
        };

        [Test]
        public void AllProductionScripts_HaveBalancedBraces()
        {
            var failures = new List<string>();

            foreach (var rel in ScanRoots)
            {
                string root = Path.Combine(Application.dataPath, "..", rel);
                root = Path.GetFullPath(root);
                if (!Directory.Exists(root))
                {
                    Debug.LogWarning($"[BraceBalance] Skipping missing root: {root}");
                    continue;
                }

                foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string source;
                    try { source = File.ReadAllText(path); }
                    catch (IOException ex)
                    {
                        failures.Add($"{path}: read failed ({ex.Message})");
                        continue;
                    }

                    int balance = ComputeBraceBalance(source, out int firstNegativeIndex);
                    if (balance != 0)
                    {
                        int line = (firstNegativeIndex >= 0)
                            ? LineOf(source, firstNegativeIndex)
                            : LineOf(source, source.Length - 1);
                        failures.Add(
                            $"{path}: brace imbalance = {balance} (extra '{(balance > 0 ? '{' : '}')}'). " +
                            $"First anomaly near line {line}.");
                    }
                }
            }

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Brace-balance check failed for {failures.Count} file(s):");
                foreach (var f in failures) sb.AppendLine("  • " + f);
                Assert.Fail(sb.ToString());
            }
        }

        /// <summary>
        /// Counts net brace balance ignoring string/char literals and comments.
        /// Returns balance = (open − close). Sets <paramref name="firstNegativeIndex"/>
        /// to the index of the first stray closing brace (where running depth went
        /// below zero), or −1 if no such position exists.
        /// </summary>
        private static int ComputeBraceBalance(string s, out int firstNegativeIndex)
        {
            firstNegativeIndex = -1;
            int depth = 0;
            int min = 0;
            int minIndex = -1;

            int i = 0;
            int n = s.Length;
            while (i < n)
            {
                char c = s[i];

                // Line comment
                if (c == '/' && i + 1 < n && s[i + 1] == '/')
                {
                    while (i < n && s[i] != '\n') i++;
                    continue;
                }
                // Block comment
                if (c == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(s[i] == '*' && s[i + 1] == '/')) i++;
                    i = System.Math.Min(n, i + 2);
                    continue;
                }
                // Verbatim string @"..."
                if (c == '@' && i + 1 < n && s[i + 1] == '"')
                {
                    i += 2;
                    while (i < n)
                    {
                        if (s[i] == '"')
                        {
                            if (i + 1 < n && s[i + 1] == '"') { i += 2; continue; } // escaped ""
                            i++; break;
                        }
                        i++;
                    }
                    continue;
                }
                // Interpolated verbatim $@"..." or @$"..."
                if ((c == '$' && i + 2 < n && s[i + 1] == '@' && s[i + 2] == '"') ||
                    (c == '@' && i + 2 < n && s[i + 1] == '$' && s[i + 2] == '"'))
                {
                    i += 3;
                    int interpDepth = 0;
                    while (i < n)
                    {
                        if (s[i] == '{' && i + 1 < n && s[i + 1] == '{') { i += 2; continue; }
                        if (s[i] == '}' && i + 1 < n && s[i + 1] == '}') { i += 2; continue; }
                        if (s[i] == '{') { interpDepth++; i++; continue; }
                        if (s[i] == '}' && interpDepth > 0) { interpDepth--; i++; continue; }
                        if (s[i] == '"' && interpDepth == 0)
                        {
                            if (i + 1 < n && s[i + 1] == '"') { i += 2; continue; }
                            i++; break;
                        }
                        i++;
                    }
                    continue;
                }
                // Interpolated string $"..."
                if (c == '$' && i + 1 < n && s[i + 1] == '"')
                {
                    i += 2;
                    int interpDepth = 0;
                    while (i < n)
                    {
                        if (s[i] == '\\' && i + 1 < n) { i += 2; continue; }
                        if (s[i] == '{' && i + 1 < n && s[i + 1] == '{') { i += 2; continue; }
                        if (s[i] == '}' && i + 1 < n && s[i + 1] == '}') { i += 2; continue; }
                        if (s[i] == '{') { interpDepth++; i++; continue; }
                        if (s[i] == '}' && interpDepth > 0) { interpDepth--; i++; continue; }
                        if (s[i] == '"' && interpDepth == 0) { i++; break; }
                        if (s[i] == '\n' && interpDepth == 0) break; // unterminated; bail
                        i++;
                    }
                    continue;
                }
                // Regular string "..."
                if (c == '"')
                {
                    i++;
                    while (i < n)
                    {
                        if (s[i] == '\\' && i + 1 < n) { i += 2; continue; }
                        if (s[i] == '"') { i++; break; }
                        if (s[i] == '\n') break;
                        i++;
                    }
                    continue;
                }
                // Char literal '...'
                if (c == '\'')
                {
                    i++;
                    while (i < n)
                    {
                        if (s[i] == '\\' && i + 1 < n) { i += 2; continue; }
                        if (s[i] == '\'') { i++; break; }
                        if (s[i] == '\n') break;
                        i++;
                    }
                    continue;
                }

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth < min) { min = depth; minIndex = i; }
                }
                i++;
            }

            if (min < 0) firstNegativeIndex = minIndex;
            return depth;
        }

        private static int LineOf(string s, int index)
        {
            if (index < 0) return 0;
            int line = 1;
            int max = System.Math.Min(index, s.Length - 1);
            for (int i = 0; i <= max; i++) if (s[i] == '\n') line++;
            return line;
        }
    }
}
