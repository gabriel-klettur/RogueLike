using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Editors.Spawners
{
    /// <summary>
    /// Spawners placed with F3 must reach disk.
    ///
    /// They did not, and the mechanism was never at fault: the repository routing, the
    /// map-slot path and the loader all agreed. What was missing was a caller. Every sibling
    /// runtime editor either saves on Ctrl+S, saves when it closes, or saves after each edit —
    /// the Spawner editor did none of the three, so its only save trigger was a toolbar button.
    /// A whole session of placing spawners was lost on restart unless the user happened to
    /// click it, which reads exactly like broken persistence.
    ///
    /// These are source scans rather than behavioural tests because the trigger is the thing
    /// that was missing, and a trigger is a wiring fact. A behavioural test of
    /// SaveInstancesToJson would have passed throughout the entire period the feature was
    /// broken.
    /// </summary>
    [TestFixture]
    public class SpawnerPersistenceTriggerTests
    {
        private static string EditorsRoot =>
            Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Editors");

        private static string Read(params string[] parts) =>
            File.ReadAllText(Path.Combine(EditorsRoot, Path.Combine(parts)));

        private static IEnumerable<string> SpawnerSources() =>
            Directory.GetFiles(Path.Combine(EditorsRoot, "Spawners"), "*.cs")
                     .Select(File.ReadAllText);

        // ── The triggers that were missing ───────────────────────────────────────

        [Test]
        public void CtrlSSavesTheSpawnerEditor()
        {
            bool bound = SpawnerSources().Any(src =>
                src.Contains("IsCtrlHeld()") && src.Contains("Key.S") && src.Contains("SaveInstancesToJson"));

            Assert.IsTrue(bound,
                "Ctrl+S must save, as it does in Buildings, Tile and Lighting. Without it the " +
                "only way to persist is a toolbar button most people never find.");
        }

        [Test]
        public void ClosingTheEditorSaves()
        {
            string src = Read("Spawners", "SpawnerEditorManager.cs");

            int deactivate = src.IndexOf("public void Deactivate()", System.StringComparison.Ordinal);
            Assert.Greater(deactivate, -1, "Deactivate moved — update this test.");

            string body = src.Substring(deactivate, System.Math.Min(900, src.Length - deactivate));
            Assert.IsTrue(body.Contains("FlushAutosave") || body.Contains("SaveInstancesToJson"),
                "Closing the editor is how people finish editing, so it is the trigger that " +
                "actually carries a session to disk. Ctrl+S only helps someone who already " +
                "knows they need it.");
        }

        [Test]
        public void EveryPlacementEditorHasAnAutomaticSaveTrigger()
        {
            // The gap was invisible because each editor was written on its own. Comparing them
            // is what surfaced it, so the comparison is what gets pinned.
            foreach (var dir in new[] { "Spawners", "Buildings", "Lighting" })
            {
                string folder = Path.Combine(EditorsRoot, dir);
                if (!Directory.Exists(folder)) continue;

                bool auto = Directory.GetFiles(folder, "*.cs")
                    .Select(File.ReadAllText)
                    .Any(src => src.Contains("IsCtrlHeld()")
                             || System.Text.RegularExpressions.Regex.IsMatch(
                                    src, @"Deactivate\(\)\s*\{[^}]*Save", System.Text.RegularExpressions.RegexOptions.Singleline));

                Assert.IsTrue(auto,
                    $"{dir} has no automatic save trigger — no Ctrl+S and no save on close. " +
                    "Its edits survive only if the user finds the Save button, which is how " +
                    "spawner persistence appeared broken for months.");
            }
        }

        // ── Automatic saving ────────────────────────────────────────────────────

        [Test]
        public void EveryMutationMarksTheMapDirty()
        {
            // Placing, moving, deleting and undo/redo all change what should be on disk. Each
            // funnels through MarkInstancesDirty rather than calling save itself, so a new edit
            // operation added later cannot quietly forget to persist — which is how this editor
            // ended up with no automatic save at all.
            string modes = Read("Spawners", "SpawnerEditorManager.Modes.cs");
            string ui    = Read("Spawners", "SpawnerEditorManager.UI.cs");
            string mgr   = Read("Spawners", "SpawnerEditorManager.cs");

            Assert.IsTrue(modes.Contains("MarkInstancesDirty();"), "PlaceSpawner must mark dirty.");
            Assert.IsTrue(ui.Contains("MarkInstancesDirty();"), "Deleting must mark dirty.");
            Assert.IsTrue(mgr.Contains("_undo.Undo(); MarkInstancesDirty();"), "Undo must mark dirty.");
            Assert.IsTrue(mgr.Contains("_undo.Redo(); MarkInstancesDirty();"), "Redo must mark dirty.");
        }

        [Test]
        public void TheMoveIsPersistedWhenTheDragEndsRatherThanWhileItRuns()
        {
            string src = Read("Spawners", "SpawnerEditorManager.Modes.cs");

            int finalize = src.IndexOf("internal void FinalizeMoveDrag()", System.StringComparison.Ordinal);
            Assert.Greater(finalize, -1, "FinalizeMoveDrag moved — update this test.");
            Assert.IsTrue(src.Substring(finalize).Contains("MarkInstancesDirty"),
                "The end of a drag is what must persist. A spawner dragged across the map moves " +
                "every frame; marking during the drag rewrites the whole file dozens of times " +
                "for one gesture.");
        }

        [Test]
        public void TheAutosaveIsDebouncedRatherThanImmediate()
        {
            string src = Read("Spawners", "SpawnerEditorManager.Modes.cs");

            Assert.IsTrue(src.Contains("AUTOSAVE_DEBOUNCE_SECONDS"),
                "Saving on the same frame as the edit is wrong in a way that is easy to miss: " +
                "DeleteSelectedInstance goes through SafeDestroy, which defers to Destroy in " +
                "Play Mode, so the deleted spawner is still alive until end of frame and " +
                "FindObjectsOfType would write it straight back.");
            Assert.IsTrue(src.Contains("private void TickAutosave()"),
                "Something has to flush the pending write each frame.");
        }

        [Test]
        public void ClosingOrQuittingFlushesAPendingWrite()
        {
            string src = Read("Spawners", "SpawnerEditorManager.cs");

            Assert.IsTrue(src.Contains("if (_active) FlushAutosave();"),
                "Placing a spawner and closing within the debounce window is exactly what " +
                "someone does when they place one last spawner and hit F3.");
            Assert.IsTrue(src.Contains("OnApplicationQuit() => FlushAutosave();"),
                "Stopping Play Mode with the editor still open is the other way to lose an " +
                "edit inside the debounce window.");
        }

        [Test]
        public void EditModeCannotWriteTheRealMap()
        {
            string src = Read("Spawners", "SpawnerEditorManager.Modes.cs");

            Assert.IsTrue(src.Contains("Application.isEditor && !Application.isPlaying"),
                "Fixtures construct this manager and drive Activate/Deactivate. Now that " +
                "closing saves, an unguarded write lets the test runner replace the real " +
                "StreamingAssets file with whatever a fixture had in its scene — the same " +
                "pollution as the run twin-save incident.");
        }

        // ── The guard that automatic saving makes necessary ──────────────────────

        [Test]
        public void AnEmptySceneNeverOverwritesAPopulatedFile()
        {
            string src = Read("Spawners", "SpawnerEditorManager.Modes.cs");

            Assert.IsTrue(src.Contains("ABORTING save"),
                "Saving automatically is only safe with this guard. SpawnerInstanceLoader has " +
                "several paths that log and return — no catalog, no ZoneManager, a parse " +
                "error — and every one of them leaves zero instances in the scene. Without the " +
                "refusal, closing the editor after a failed load erases every spawner ever " +
                "authored. That is the Buildings save-collapse incident, in a second editor.");

            Assert.IsTrue(src.Contains("all.Length == 0 && FileHasEntries(path)"),
                "The guard must compare the scene against what is actually on disk. Refusing " +
                "every empty save would make clearing a map impossible.");
        }

        [Test]
        public void SaveReportsWhetherItWrote()
        {
            string src = Read("Spawners", "SpawnerEditorManager.Modes.cs");

            Assert.IsTrue(src.Contains("public bool SaveInstancesToJson()"),
                "A save that can refuse must say so. A void signature leaves callers unable to " +
                "tell a successful write from a blocked one.");
        }

        [Test]
        public void AnUnreadableFileBlocksTheSaveRatherThanAllowingIt()
        {
            string src = Read("Spawners", "SpawnerEditorManager.Modes.cs");

            Assert.IsTrue(src.Contains("Treating it as populated"),
                "If the existing file cannot be read, the safe assumption is that it holds " +
                "data. Defaulting the other way turns an I/O hiccup into deletion.");
        }

        // ── Where it writes ──────────────────────────────────────────────────────

        [Test]
        public void SaveAndLoadResolveTheSamePath()
        {
            // Both sides are map-slot aware, and they have to stay that way together: if the
            // writer routes per slot and the reader does not, spawners save successfully and
            // silently never come back — which is indistinguishable from not saving at all.
            string save = Read("Spawners", "SpawnerEditorManager.Modes.cs");
            Assert.IsTrue(save.Contains("MapEditorActiveSlot.DirForActiveSlot"),
                "The writer must route through the active map slot.");

            string loader = File.ReadAllText(Path.Combine(Application.dataPath, "_Project",
                "Scripts", "Gameplay", "Spawners", "SpawnerInstanceLoader.cs"));
            Assert.IsTrue(loader.Contains("JsonFileSpawnerInstanceRepository"),
                "The reader must go through the repository, which applies the same slot routing.");

            string repo = File.ReadAllText(Path.Combine(Application.dataPath, "_Project",
                "Scripts", "Infrastructure", "Persistence", "Repositories",
                "JsonFileSpawnerInstanceRepository.cs"));
            Assert.IsTrue(repo.Contains("IsMapSlotAware => true"),
                "Spawners are authored per map slot; editing one map must not overwrite another.");
        }
    }
}
