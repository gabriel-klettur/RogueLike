using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Editors.Shared
{
    /// <summary>
    /// Stops the runtime editors growing new hard-coded colours.
    ///
    /// The project already knows what happens to a convention nothing enforces: the
    /// editors' canonical UX pattern was written down and the palette still drifted to
    /// <b>459</b> raw <c>new Color(...)</c> literals across sixteen editors — Map 90,
    /// Tile 85, Spells 68 — while three theme sources sat unused beside them.
    ///
    /// A blanket rewrite is the wrong answer and the measurement says why: only 37 of the
    /// 421 parseable literals matched an existing token exactly. The rest are one-off
    /// semantic colours a designer chose on purpose, and replacing those would change
    /// pixels nobody asked to change. So this is a RATCHET: the count per file may fall
    /// freely and may never rise. Same shape as the Domain-Reload static scanner and the
    /// FSM transition registry — the two conventions in this repo that have actually held.
    /// </summary>
    [TestFixture]
    public sealed class EditorRawColorRatchetTests
    {
        private const string EDITORS_REL  = "_Project/Scripts/Gameplay/Editors";
        private const string BASELINE_REL = "Tests/EditMode/Baselines/editor-raw-colors.txt";

        /// <summary>
        /// The theme itself is where colours are SUPPOSED to be written, so it is not an
        /// offender. It is the only exclusion on purpose: every other file under Editors/
        /// should be reaching for a token.
        /// </summary>
        private const string THEME_FILE = "Tile/TileEditorTheme.cs";

        private static readonly Regex RawColor = new Regex(@"new Color(?:32)?\(", RegexOptions.Compiled);

        [Test]
        public void NoEditorGrowsNewHardCodedColours()
        {
            var editorsDir = EditorsDirectory();
            var baseline   = ReadBaseline();
            var live       = CountLiveColours(editorsDir);

            var grew    = new List<string>();
            var unlisted = new List<string>();

            foreach (var pair in live)
            {
                if (!baseline.TryGetValue(pair.Key, out int allowed))
                {
                    unlisted.Add($"  {pair.Value,4}\t{pair.Key}");
                    continue;
                }
                if (pair.Value > allowed)
                    grew.Add($"  {pair.Key}: {allowed} -> {pair.Value} (+{pair.Value - allowed})");
            }

            if (grew.Count == 0 && unlisted.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("New hard-coded colours in the runtime editors.");
            sb.AppendLine();
            sb.AppendLine("Put the colour in UITheme — or TileEditorTheme, for chrome the F8 UX panel");
            sb.AppendLine("retunes live — and reference the token, so restyling the editors stays one edit");
            sb.AppendLine("instead of a hunt through 102 files.");
            sb.AppendLine();
            sb.AppendLine("If the colour genuinely belongs to one widget and nowhere else, raise its count");
            sb.AppendLine($"in {BASELINE_REL} in the same commit — a reviewed exception, not a silent one.");

            if (grew.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Files that grew:");
                foreach (var line in grew) sb.AppendLine(line);
            }
            if (unlisted.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Files with raw colours that the baseline does not list:");
                foreach (var line in unlisted) sb.AppendLine(line);
            }

            Assert.Fail(sb.ToString());
        }

        [Test]
        public void TheBaselineDoesNotListFilesThatAreGone()
        {
            var editorsDir = EditorsDirectory();
            var baseline   = ReadBaseline();

            var missing = new List<string>();
            foreach (var rel in baseline.Keys)
                if (!File.Exists(Path.Combine(editorsDir, rel.Replace('/', Path.DirectorySeparatorChar))))
                    missing.Add("  " + rel);

            // A stale entry is not harmless: it is an allowance for a file that no longer
            // exists, so a NEW file created at that exact path would inherit it silently.
            Assert.IsEmpty(missing,
                "The raw-colour baseline lists files that no longer exist. Drop these lines:\n"
                + string.Join("\n", missing));
        }

        [Test]
        public void TheThemeItselfIsTheOnlyExclusion()
        {
            var themePath = Path.Combine(EditorsDirectory(),
                THEME_FILE.Replace('/', Path.DirectorySeparatorChar));

            Assert.IsTrue(File.Exists(themePath),
                $"{THEME_FILE} is the ratchet's one exclusion. If it moved, this test is " +
                "silently excluding nothing and the file that replaced it is unguarded.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static string EditorsDirectory()
        {
            string dir = Path.Combine(Application.dataPath,
                EDITORS_REL.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(Directory.Exists(dir), $"Editors directory not found at '{dir}'.");
            return dir;
        }

        private static Dictionary<string, int> ReadBaseline()
        {
            string path = Path.Combine(Application.dataPath,
                BASELINE_REL.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(path), $"Baseline not found at '{path}'.");

            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                if (!int.TryParse(line.Substring(0, tab).Trim(), out int count)) continue;

                map[line.Substring(tab + 1).Trim()] = count;
            }
            return map;
        }

        private static Dictionary<string, int> CountLiveColours(string editorsDir)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(editorsDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = path.Substring(editorsDir.Length)
                                 .TrimStart(Path.DirectorySeparatorChar, '/')
                                 .Replace('\\', '/');
                if (rel == THEME_FILE) continue;

                int n = RawColor.Matches(File.ReadAllText(path)).Count;
                if (n > 0) map[rel] = n;
            }
            return map;
        }
    }
}
