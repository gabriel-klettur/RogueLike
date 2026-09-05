using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors.General;

namespace Valkur.Tests.EditMode.Editors.General
{
    /// <summary>
    /// Every editor that EXISTS must be openable, and every per-editor tool must name an
    /// editor that exists.
    ///
    /// <para>Both halves are about the same string: <c>IGameEditor.EditorName</c>. It is the
    /// label the General Editor opens the editor by AND the name inside the input context id
    /// (<c>editor/Tile Editor</c>). Nothing enforced either link, and both failed silently.</para>
    ///
    /// <para>The second one caught a live defect the moment it was written: the per-editor
    /// tool owners had been authored as "Tile", "Buildings", "Map", "Boss" while the shipped
    /// EditorNames are "Tile Editor", "Buildings Editor", "Map Editor", "Boss Editor" — so
    /// <c>InputContextPolicy.IsLive</c> compared two strings that never matched and EVERY
    /// per-editor tool was dead. The fixture that was supposed to cover it passed, because it
    /// derived its list of editors FROM those same owners: one half measured against itself.
    /// Same shape as <c>SPAWNER_COORDINATE_SPACE_DRIFT</c>, and the same answer — assert the
    /// COMPOSITION against the other side.</para>
    /// </summary>
    [TestFixture]
    public class EditorReachabilityTests
    {
        private static string ScriptsRoot()
            => Path.Combine(Application.dataPath, "_Project", "Scripts");

        /// <summary>
        /// Every <c>EditorName</c> declared in production source, read from the source rather
        /// than from a live registry: an EditMode test has no scene, so
        /// <c>GameEditorManager.RegisteredEditors</c> is empty there — and an editor that
        /// exists but is never registered is exactly one of the failures this is for.
        /// </summary>
        private static List<string> ShippedEditorNames()
        {
            var rx = new Regex(@"public\s+string\s+EditorName\s*=>\s*""([^""]+)""");
            var names = new List<string>();

            foreach (var file in Directory.GetFiles(ScriptsRoot(), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in rx.Matches(File.ReadAllText(file)))
                    names.Add(m.Groups[1].Value);

            // The General Editor is the launcher itself. It does not need an entry pointing
            // at itself, and it is reached by Escape rather than from a list.
            names.RemoveAll(n => n == "General");

            return names.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        private static string Normalize(string s) =>
            Regex.Replace(s ?? "", "[^a-zA-Z0-9]", "").ToLowerInvariant();

        // ── Reachability ─────────────────────────────────────────────────────

        /// <summary>
        /// THE test the retirement of the F-keys made load-bearing. With no hotkey, the
        /// General Editor is the only way in — so an editor missing from that list is an
        /// editor nobody can open, and nothing throws to say so.
        /// </summary>
        [Test]
        public void EveryEditor_HasAGeneralEditorEntry()
        {
            var entries = GeneralEditorRegistry.BuildEntries()
                .Select(e => Normalize(e.Label))
                .ToList();

            var unreachable = new List<string>();
            foreach (var name in ShippedEditorNames())
            {
                // The menu says "Spawners" where the editor says "Spawner Editor", and "Tile"
                // where it says "Tile Editor". Match on the stem so the label stays free to
                // read well without breaking the link.
                string stem = Normalize(name.Replace(" Editor", ""));
                if (!entries.Any(e => e.StartsWith(stem, StringComparison.Ordinal)
                                   || stem.StartsWith(e, StringComparison.Ordinal)))
                    unreachable.Add(name);
            }

            Assert.IsEmpty(unreachable,
                "These editors exist and have no General Editor entry. Since the F-key toggles " +
                "were retired, that makes them impossible to open — and it fails silently:\n" +
                string.Join("\n", unreachable));
        }

        [Test]
        public void TheGeneralEditor_IsNotListedInsideItself()
        {
            var labels = GeneralEditorRegistry.BuildEntries().Select(e => e.Label).ToList();
            CollectionAssert.DoesNotContain(labels, "General",
                "The launcher must not offer itself — clicking it would close and reopen the " +
                "panel the click came from.");
        }

        // ── The tool-owner link ──────────────────────────────────────────────

        [Test]
        public void EveryToolOwner_IsARealEditorName()
        {
            var shipped = new HashSet<string>(ShippedEditorNames(), StringComparer.Ordinal);

            var wrong = InputActionCatalog.All
                .Where(d => !string.IsNullOrEmpty(d.OwnerEditor))
                .Where(d => !shipped.Contains(d.OwnerEditor))
                .Select(d => $"{d.Id} owned by '{d.OwnerEditor}'")
                .Distinct()
                .ToList();

            Assert.IsEmpty(wrong,
                "OwnerEditor must be the editor's EXACT EditorName — that string is what ends " +
                "up in the context id, and InputContextPolicy.IsLive compares the two. A " +
                "mismatch is silent: the tool simply never fires.\n" +
                "Shipped names: " + string.Join(", ", shipped) + "\n" +
                string.Join("\n", wrong));
        }

        /// <summary>
        /// The end-to-end version: drive the real context id for each editor and check that
        /// its own tools actually answer. This is the composition the two halves above only
        /// describe separately.
        /// </summary>
        [Test]
        public void EachEditorsTools_AreLiveUnderItsRealContextId()
        {
            InputContexts.ResetForTests();
            InputContextPolicy.ResetForTests();

            try
            {
                var dead = new List<string>();

                foreach (var name in ShippedEditorNames())
                {
                    InputContexts.SetActiveEditorOverride(name);
                    Assert.AreEqual("editor/" + name, InputContexts.Current);

                    foreach (var d in InputActionCatalog.All)
                    {
                        if (d.OwnerEditor != name) continue;
                        if (!InputContextPolicy.IsLive(d))
                            dead.Add($"{d.Id} is not live under 'editor/{name}'");
                    }
                }

                Assert.IsEmpty(dead,
                    "A tool that is not live in its own editor can never fire:\n" +
                    string.Join("\n", dead));
            }
            finally
            {
                InputContexts.ClearActiveEditorOverride();
                InputContexts.ResetForTests();
                InputContextPolicy.ResetForTests();
            }
        }
    }
}
