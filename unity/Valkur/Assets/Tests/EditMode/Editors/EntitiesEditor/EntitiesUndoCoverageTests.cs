using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Editors.EntitiesEditor
{
    /// <summary>
    /// Undo reaches every mutation, and Save writes only this editor's work.
    ///
    /// <para><b>Audited 2026-09-12: <c>_undo.Record</c> was called from exactly ONE place</b> —
    /// dragging a placed entity — out of roughly fifteen operations that change something. An
    /// author who retuned six stats and pressed Ctrl+Z got their last MOVE back and kept every
    /// stat, with nothing on screen saying the buttons did not cover what they appeared to.</para>
    ///
    /// <para>The fix was one seam rather than fifteen commands: every mutation already ends at
    /// <c>CommitDefinitionEdit</c>, which now snapshots the definition. These tests read the
    /// SOURCE, because the property they protect is structural — "no write path bypasses the
    /// seam" — and that is a claim about call sites, not about a value at runtime.</para>
    /// </summary>
    public class EntitiesUndoCoverageTests
    {
        private static string EditorRoot => Path.Combine(
            Application.dataPath, "_Project/Scripts/Gameplay/Editors/Entities");

        private static IEnumerable<string> SourceFiles()
            => Directory.GetFiles(EditorRoot, "*.cs");

        [Test]
        public void CommitDefinitionEdit_RecordsAnUndoStep()
        {
            string text = File.ReadAllText(
                Path.Combine(EditorRoot, "EntitiesRuntimeEditor.Interaction.cs"));

            Assert.That(text.Contains("RecordDefinitionUndo(def, label)"), Is.True,
                "The one seam every mutation passes through must record an undo step. " +
                "Without it the Tools panel's two buttons cover a drag and nothing else.");
        }

        [Test]
        public void TheSnapshotIsSeededWhereADefinitionIsResolvedForEditing()
        {
            // Seeding lazily at commit time would make the FIRST change to each definition the
            // one change that cannot be undone -- silently, and on the edit an author is most
            // likely to be trying out.
            bool seeded = SourceFiles()
                .Select(File.ReadAllText)
                .Any(t => t.Contains("SeedDefinitionSnapshot(def)"));

            Assert.That(seeded, Is.True,
                "CurrentEditableMonster must seed the snapshot before an edit can happen.");
        }

        [Test]
        public void SaveDoesNotWriteTheWholeProject()
        {
            string text = File.ReadAllText(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.Edits.cs"));

            Assert.That(text.Contains("SaveAssetIfDirty"), Is.True,
                "Save must write the assets this editor touched.");

            foreach (var file in SourceFiles())
            {
                string body = File.ReadAllText(file);
                // Strip doc comments: this rule is discussed by name in several of them, and a
                // guard that fails on its own explanation teaches people to delete the comment.
                string code = string.Join("\n", body.Split('\n')
                    .Where(line => !line.TrimStart().StartsWith("//") &&
                                   !line.TrimStart().StartsWith("///")));

                Assert.That(code.Contains("AssetDatabase.SaveAssets()"), Is.False,
                    Path.GetFileName(file) + " calls AssetDatabase.SaveAssets(), which commits " +
                    "every dirty object in the PROJECT. A Save button inside one editor must " +
                    "mean that editor's own work -- this is the call that, with a corrupted " +
                    "in-memory ScriptableObject, would flush the corruption over good files.");
            }
        }

        [Test]
        public void ReloadActuallyReImportsRatherThanRelistingMemory()
        {
            string text = File.ReadAllText(Path.Combine(EditorRoot, "EntitiesRuntimeEditor.Edits.cs"));

            Assert.That(text.Contains("AssetDatabase.ImportAsset"), Is.True,
                "Reload used to be RefreshPicker(), which re-lists the same in-memory objects. " +
                "A domain reload does not reload assets either, so re-importing is the only " +
                "thing that re-reads the file an author edited outside the game.");

            Assert.That(text.Contains("_reloadArmed"), Is.True,
                "Reload discards unsaved edits, so it must ask twice: the same gesture that " +
                "recovers from a bad edit would otherwise throw away a good session.");
        }

        [Test]
        public void RenameWarnsAboutWhatPointsAtTheOldKey()
        {
            string text = File.ReadAllText(
                Path.Combine(EditorRoot, "EntitiesRuntimeEditor.RenameGuard.cs"));

            // monsterKey is a join key three subsystems point at by STRING, none of which this
            // editor fixes up. Its failure mode is the quietest kind: the monster keeps working
            // but boots a bare IdleState, and the camp that spawned it spawns nothing.
            Assert.That(text.Contains("HasSetForArchetype"), Is.True, "FSM assignments unchecked.");
            Assert.That(text.Contains("CountSpawnerWaveReferences"), Is.True, "Spawner waves unchecked.");
            Assert.That(text.Contains("CountPlacedInstances"), Is.True, "Map placements unchecked.");
            Assert.That(text.Contains("_renameArmedForKey"), Is.True,
                "A rename with live references must require a second press.");
        }

        [Test]
        public void TheCategoryTabsReadTheAuthoredFaction_NotTheKeyText()
        {
            string text = File.ReadAllText(
                Path.Combine(EditorRoot, "EntitiesRuntimeEditor.Interaction.cs"));

            Assert.That(text.Contains("IsNeutralFaction(def.stats.faction)"), Is.True,
                "The tabs must sort by the field the GAME sorts by. A string search over " +
                "monsterKey misfiled barbol_brother_felipondor -- a NEUTRAL under Hostiles -- " +
                "because his key contains none of the words it looked for.");

            foreach (var needle in new[] { "k.Contains(\"vendor\")", "k.Contains(\"boss\")" })
                Assert.That(text.Contains(needle), Is.False,
                    "The old key-text heuristic is still in MatchesCategory: " + needle);
        }
    }
}
