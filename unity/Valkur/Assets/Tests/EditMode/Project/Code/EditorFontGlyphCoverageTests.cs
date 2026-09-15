using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace Valkur.Tests.EditMode.Project.Code
{
    /// <summary>
    /// No runtime UI string asks the shipped font for a character it does not have.
    ///
    /// <para><b>Measured 2026-09-13: every dropdown in the Entities editor drew an empty
    /// box.</b> <c>UIDropdown</c> set its caret to <c>U+25BE</c> and <c>LiberationSans SDF</c>
    /// does not carry it, so TMP substituted <c>U+25A1</c> and logged one warning per instance
    /// — fourteen tofu boxes and fourteen console warnings from one editor, in a project whose
    /// first cardinal rule is that the console must be clean.</para>
    ///
    /// <para><b>Choosing a different arrow is not the fix, and that is what this test is
    /// for.</b> Asked directly, the font carries NONE of <c>▾ ▼ ▽ ▴ ↓ ∨ ▪ ● ▶ → ≤ ≥ ≈ ⚙ ♪ ↔
    /// ─ −</c>. Every one of those is a plausible thing to type into a label, and each fails
    /// the same way: silently, at runtime, in a warning nobody reads. A caret is DRAWN now
    /// (<c>Valkur.UIKit.CaretGraphic</c>); everything else uses ASCII.</para>
    ///
    /// <para>It scans SOURCE rather than a built canvas because uGUI performs no layout in
    /// EditMode and most of these strings live in panels no fixture builds. What can be
    /// checked without a frame is whether the character was ever typed — which is the failure
    /// that happened.</para>
    /// </summary>
    public class EditorFontGlyphCoverageTests
    {
        /// <summary>Folders whose string literals reach a TMP label.</summary>
        private static readonly string[] UiRoots =
        {
            "_Project/Scripts/Gameplay/Editors",
            "_Project/Scripts/Gameplay/UIKit",
            "_Project/Scripts/UI",
        };

        /// <summary>
        /// Only characters ABOVE Latin-1 are asked about. The font carries the accented
        /// letters this project's Spanish UI needs, and an em dash; what it lacks is the
        /// symbol blocks. Scanning below U+00FF would flag every "á" in the game.
        /// </summary>
        private const int FirstInterestingCodepoint = 0x2000;

        private static readonly Regex StringLiteral = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");

        /// <summary>
        /// Everything before a trailing <c>//</c>, leaving the <c>//</c> of a URL alone.
        /// Crude on purpose: no string literal in this repo contains a bare <c>//</c>, and a
        /// full C# lexer here would be machinery guarding a comment.
        /// </summary>
        private static string StripTrailingComment(string line)
        {
            int i = line.IndexOf("//", System.StringComparison.Ordinal);
            while (i > 0 && line[i - 1] == ':')
                i = line.IndexOf("//", i + 2, System.StringComparison.Ordinal);
            return i < 0 ? line : line.Substring(0, i);
        }

        private static TMP_FontAsset Font()
        {
            var font = TMP_Settings.defaultFontAsset;
            Assert.That(font, Is.Not.Null,
                "TMP_Settings.defaultFontAsset is null — every label in the game renders " +
                "with it, so this test cannot answer anything without it.");
            return font;
        }

        [Test]
        public void NoUiStringLiteral_UsesAGlyphTheShippedFontLacks()
        {
            var font = Font();
            var offenders = new List<string>();
            int scanned = 0;

            foreach (var rel in UiRoots)
            {
                string root = Path.Combine(Application.dataPath, rel);
                if (!Directory.Exists(root)) continue;

                foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    scanned++;
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // Comments are exempt: this rule is DISCUSSED by name in several doc
                        // blocks, and a guard that fails on its own explanation is one people
                        // delete the explanation to satisfy. TRAILING comments count too --
                        // the first pass flagged a `// "⚙ Columns" — opens the popup` note
                        // sitting after a field declaration, which reaches no label at all.
                        string trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("//")) continue;

                        // Inspector attributes are drawn by Unity's own IMGUI font, which has
                        // full coverage; only TMP substitutes.
                        if (lines[i].Contains("Tooltip(") || lines[i].Contains("[Header(")) continue;

                        string code = StripTrailingComment(lines[i]);
                        if (code.Length == 0) continue;

                        foreach (Match m in StringLiteral.Matches(code))
                        {
                            foreach (char c in m.Groups[1].Value)
                            {
                                if (c < FirstInterestingCodepoint) continue;
                                if (font.HasCharacter(c)) continue;
                                offenders.Add(
                                    $"{Path.GetFileName(file)}:{i + 1}  U+{((int)c):X4} '{c}'");
                            }
                        }
                    }
                }
            }

            Assert.That(scanned, Is.GreaterThan(0), "Scanned no files — the roots moved.");
            Assert.That(offenders, Is.Empty,
                "UI strings asking the font for characters it does not have (each renders as " +
                "an empty box AND logs a warning):\n  " + string.Join("\n  ", offenders) +
                "\n\nUse ASCII, or DRAW the glyph — Valkur.UIKit.CaretGraphic is the caret.");
        }

        [Test]
        public void TheFontReallyLacksTheseGlyphs_SoTheRuleIsNotVacuous()
        {
            // The guard above passes trivially if the font happens to carry everything. Pin
            // the measurement it rests on: these are the characters that were actually being
            // typed into this project's UI, and the font has none of them.
            var font = Font();
            foreach (int cp in new[] { 0x25BE, 0x25BC, 0x25B6, 0x2192, 0x2500, 0x2212, 0x25CF })
                Assert.That(font.HasCharacter((char)cp), Is.False,
                    $"U+{cp:X4} is in the font now — good, but this test's premise moved.");

            // And the ones it DOES carry, so the scan is not simply banning all punctuation:
            // the em dash is used 308 times and the ellipsis is what TMP's own Ellipsis
            // overflow mode appends.
            foreach (int cp in new[] { 0x2014, 0x2026, 0x2019, 0x2022 })
                Assert.That(font.HasCharacter((char)cp), Is.True,
                    $"U+{cp:X4} was expected to be present.");
        }
    }
}
