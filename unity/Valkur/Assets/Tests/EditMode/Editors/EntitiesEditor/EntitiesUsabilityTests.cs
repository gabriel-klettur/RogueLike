using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Editors.EntitiesEditor
{
    /// <summary>
    /// The Entities editor can be READ and REACHED, not only used with a mouse.
    ///
    /// <para>These pin the findings of the usability audit of 2026-09-13, which asked a
    /// different question from the panel audit the day before: not "does the field exist" but
    /// "can an author finish the task". The functionality scored 8.2 and the interaction layer
    /// 4.3, and every item below is one of the measurements that separated them.</para>
    ///
    /// <para>They are SOURCE guards. uGUI performs no layout in EditMode, so a fixture that
    /// built the panel would be asserting on rows whose size and visibility mean nothing —
    /// and the properties these protect are structural anyway: which API a call site uses,
    /// and whether a key any of them reads exists.</para>
    /// </summary>
    public class EntitiesUsabilityTests
    {
        private static string EditorRoot => Path.Combine(
            Application.dataPath, "_Project/Scripts/Gameplay/Editors/Entities");

        private static string ReadAll()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in Directory.GetFiles(EditorRoot, "*.cs")) sb.Append(File.ReadAllText(f));
            return sb.ToString();
        }

        /// <summary>Source with `//` and `///` lines stripped — several of these rules are
        /// discussed by name in the doc blocks, and a guard that trips on its own explanation
        /// teaches people to delete the explanation.</summary>
        private static string CodeOnly(string path) =>
            string.Join("\n", File.ReadAllLines(path)
                .Where(l => !l.TrimStart().StartsWith("//")));

        /// <summary>Every file of the editor, comments stripped. The editor is fifteen
        /// partials and a rule about "does anything read this" is a question about all of
        /// them — asking one file reports a wired key as unwired.</summary>
        private static string AllCodeOnly() =>
            string.Join("\n", Directory.GetFiles(EditorRoot, "*.cs").Select(CodeOnly));

        [Test]
        public void ThePickerLabel_IsEllipsisedByTheWidget_NotCutToACharacterCount()
        {
            string code = CodeOnly(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.Interaction.cs"));

            // Measured before the fix, on the shipped catalogue: 20 of the 28 names are longer
            // than the 9-character budget, and when a slot also carried its placed count the
            // budget dropped to 7 -- at which point the eleven barbols all rendered as
            // 'Barbol...' and FOUR PAIRS were pixel-identical on screen at once. Meanwhile the
            // label rect measures 66 px and TMP reports 'Barbol Gigante' at 60: the room was
            // already there.
            Assert.That(code.Contains("TextOverflowModes.Ellipsis"), Is.True,
                "The picker label must spend the width the widget has, not a hand-written " +
                "character count.");

            Assert.That(code.Contains("TruncateName("), Is.False,
                "TruncateName is back. A helper that cuts a label to a fixed character count " +
                "is the defect itself -- it makes four pairs of entities indistinguishable on " +
                "the shipped catalogue.");
        }

        [Test]
        public void ThePlacedCount_IsABadge_NotAppendedToTheName()
        {
            string code = CodeOnly(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.Interaction.cs"));

            Assert.That(code.Contains("MakeSlotCountBadge"), Is.True,
                "The placed count belongs in its own corner badge. Appended to the label it " +
                "evicts the characters an author is reading -- the marker that says 'this is " +
                "on the map' was the reason they could not tell WHICH.");
        }

        [Test]
        public void ThePickerSlotTint_GoesThroughSetSlotTint()
        {
            string code = CodeOnly(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.Interaction.cs"));

            Assert.That(code.Contains("EditorUIHelpers.SetSlotTint("), Is.True,
                "A slot is a Selectable on ColorTint: its CanvasRenderer colour MULTIPLIES " +
                "with the Graphic's and Unity rewrites it from colors.normalColor on every " +
                "transition. SetSlotTint is the only path that survives one.");

            Assert.That(code.Contains("btn.GetComponent<Image>().color ="), Is.False,
                "Writing the slot's Image directly renders written x normalColor and reverts " +
                "to flat SLOT_BG on the next pointer enter. UIButton.SetTint documents the " +
                "measured arithmetic.");
        }

        [Test]
        public void ThePickerSlot_SaysTheFullNameSomewhere()
        {
            string code = CodeOnly(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.Interaction.cs"));

            // An ellipsised label needs somewhere to say the rest, and this editor had no
            // tooltip of any kind. UIHoverText restores the previous status text on exit and
            // survives a row destroyed while hovered -- which the picker makes the NORMAL
            // case, since it rebuilds its slots on every keystroke of the search box.
            Assert.That(code.Contains("UIHoverText.Attach("), Is.True,
                "A truncated label with no hover text is a name that cannot be read at all.");
        }

        [Test]
        public void TheEditor_ReadsTheSharedKeyboardVerbs()
        {
            string code = CodeOnly(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.cs"));

            // It was the fourteenth of fourteen and the only one that did not: there is no
            // generic dispatcher, so Ctrl+Z, Ctrl+Y and Ctrl+S did nothing here -- while the
            // editor's own overlay listed Ctrl+Z and Ctrl+Y as hotkeys.
            foreach (var verb in new[] { "EditorInput.UndoPressed()",
                                         "EditorInput.RedoPressed()",
                                         "EditorInput.SavePressed()" })
                Assert.That(code.Contains(verb), Is.True,
                    $"{verb} is not read. Thirteen other runtime editors read it, and this " +
                    "editor's tutorial promises the key.");
        }

        [Test]
        public void EveryKeyTheTutorialTeaches_IsAKeyTheEditorReads()
        {
            string all = ReadAll();
            // The WHOLE editor, not one file: the nudge tools live in their own partial, and
            // a guard that asks the wrong file reports a wired key as unwired.
            string code = AllCodeOnly();

            // The existing F-key guard asks whether a tutorial names a RETIRED key. This asks
            // the other half -- whether it names one that was never wired -- which is how
            // Ctrl+Z and Ctrl+Y sat in this overlay for the life of the editor.
            var promises = new Dictionary<string, string>
            {
                { "(\"Ctrl+Z\"", "EditorInput.UndoPressed()" },
                { "(\"Ctrl+Y\"", "EditorInput.RedoPressed()" },
                { "(\"Ctrl+S\"", "EditorInput.SavePressed()" },
                { "(\"Arrows\"", "\"NudgeUp\"" },
                { "(\"G\"",      "\"ToggleSnap\"" },
            };

            foreach (var kv in promises)
            {
                if (!all.Contains(kv.Key)) continue;   // not taught, nothing to honour
                Assert.That(code.Contains(kv.Value), Is.True,
                    $"The tutorial teaches {kv.Key.Substring(2)} and nothing reads " +
                    $"{kv.Value}. A hotkey list that names a key nothing answers is worse " +
                    "than no list: the author blames themselves.");
            }
        }

        [Test]
        public void TheEditor_DeclaresItsOwnToolsInTheCatalogue_UnderItsExactEditorName()
        {
            // OwnerEditor must be the EditorName VERBATIM: InputContexts.Current puts that
            // string in the context id and InputContextPolicy.IsLive compares the two, so a
            // mismatch kills every tool of the editor in silence. It shipped wrong once
            // project-wide -- all 35 tools dead -- and the fixture meant to catch it passed,
            // because it derived its editor list from those same owners.
            const string EditorName = "Entities Editor";

            var tools = InputActionCatalog.All
                .Where(d => d.Map == InputActionCatalog.MapEntitiesEditor)
                .ToList();

            Assert.That(tools, Is.Not.Empty,
                "The Entities editor declares no tools. It was the only one of the fourteen " +
                "with none, which is why a placement could be authored by dragging and by " +
                "nothing else.");

            foreach (var d in tools)
                Assert.That(d.OwnerEditor, Is.EqualTo(EditorName),
                    $"'{d.Action}' is owned by '{d.OwnerEditor}'. The EditorName is " +
                    $"'{EditorName}' and the comparison is exact.");

            foreach (var action in new[] { "NudgeUp", "NudgeDown", "NudgeLeft", "NudgeRight",
                                           "ToggleSnap" })
                Assert.That(tools.Any(d => d.Action == action), Is.True,
                    $"'{action}' is missing from the catalogue.");
        }

        [Test]
        public void PlacingAndDeleting_AreUndoable()
        {
            string all = ReadAll();

            // These were the last two mutations outside the stack, and the two an author
            // repeats all session. The inconsistency was the part nobody could predict:
            // DRAGGING an already-placed entity undid, because that one gesture was all the
            // original stack ever covered.
            Assert.That(all.Contains("RecordPlacementCreated("), Is.True, "Placing is not recorded.");
            Assert.That(all.Contains("RecordPlacementDeleted("), Is.True, "Deleting is not recorded.");

            string code = AllCodeOnly();
            Assert.That(code.Contains("This is not undoable"), Is.False,
                "The Delete hint still says the operation cannot be undone. (Comments are " +
                "stripped: the doc block explaining this rule quotes the old hint.)");
        }

        [Test]
        public void EveryPositionChange_AlsoSchedulesASave()
        {
            string code = CodeOnly(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.SelectionFx.cs"));

            // MarkEntityPlacementsDirty's own doc claims it is "called by every mutation
            // (place, delete)" -- and MOVE was not one of them, so dragging a monster to a new
            // spot changed the world, pushed an undo step, and never scheduled a write. The
            // new position survived a Stop only if some later edit happened to dirty the file.
            Assert.That(code.Contains("RecordEntityMove("), Is.True,
                "The drag path must go through RecordEntityMove, which records the undo step " +
                "AND marks the placements dirty. Recording only the first loses the move.");
        }

        [Test]
        public void ThePropertiesSections_Fold()
        {
            string code = CodeOnly(Path.Combine(EditorRoot, "EntitiesEditorUIBuilder.Properties.cs"));

            // Measured with dark_dwarf selected: the form is 1446 px in a 503 px viewport --
            // 34.8 % visible, three screens of scrolling, and every trip to an AI dial goes
            // past Identity, Stats, AI and Spawn.
            Assert.That(code.Contains("ToggleSectionFold"), Is.True,
                "Section headers must fold their body.");

            string filter = CodeOnly(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.PropsFilter.cs"));
            Assert.That(filter.Contains("SetSectionOpen"), Is.True,
                "A folded section still has to answer the filter: leaving it folded means " +
                "typing a field's name and being told nothing matches, with the match one " +
                "collapsed header away.");
            Assert.That(filter.Contains("_foldedSections"), Is.True,
                "The fold must be remembered by NAME -- the editor destroys and rebuilds " +
                "every section body on each selection change, so object references would " +
                "hold destroyed Transforms and lose the fold on the next click in the Picker.");
        }

        [Test]
        public void NoStatusLineOrLog_NamesARetiredEditorToggle()
        {
            // The existing guard requires a tutorial tuple's trailing comma, so it cannot see
            // a plain sentence -- and the FIRST thing this editor said on opening was
            // "Entities Editor active. F5 to close." The F-row toggles were retired
            // 2026-09-05; every editor is reached from Escape.
            var offenders = new List<string>();
            string root = Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Editors");

            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//")) continue;
                    if (!line.Contains("SetStatus(") && !line.Contains("Debug.Log(")) continue;

                    // The PHRASE, not the bare key, and that distinction is load-bearing in
                    // both directions.
                    //
                    // A bare \bF\d\b match is wrong twice over. It catches {value:F2} and
                    // ToString("F2"), which are float format specifiers -- the first pass
                    // flagged two of this session's own status messages, the same false
                    // positive the tutorial-row guard already had to be tightened for. And it
                    // would condemn "Shift+F8 to toggle", which is CORRECT: the Tile and
                    // Buildings perf probes really do own F2-F8 while their overlay is up, and
                    // the asset binds them.
                    //
                    // What was retired is the editor TOGGLE, so what the guard looks for is an
                    // F-key claiming to open or close an editor.
                    foreach (var pattern in new[]
                             {
                                 @"\bF\d{1,2} to (close|open)\b",
                                 @"\(F\d{1,2}\)",
                                 @"\bUse F\d{1,2}\b",
                             })
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line, pattern);
                        if (m.Success)
                            offenders.Add($"{Path.GetFileName(file)}:{i + 1} -> \"{m.Value}\"");
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Status lines or logs naming a retired F-key toggle:\n  " +
                string.Join("\n  ", offenders) +
                "\n\nEvery runtime editor is opened from the General Editor on Escape.");
        }

        [Test]
        public void ActivateGoesThroughSetMode_SoTheHintMatchesTheMode()
        {
            string code = CodeOnly(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.cs"));

            // Activate used to assign the field and call RefreshModeButtons, which lit the
            // right button and left the hint on its build-time default -- measured live, the
            // editor opened in Select mode reading "Select a mode then click on the map".
            Assert.That(code.Contains("SetMode(EditorMode.Select);"), Is.True,
                "Activate must go through SetMode: it is the one place that puts the mode, " +
                "the buttons, the hint and the status line in agreement.");
        }
    }
}
