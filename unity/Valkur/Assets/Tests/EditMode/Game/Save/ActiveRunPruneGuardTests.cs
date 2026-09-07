using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Game.Save
{
    /// <summary>
    /// The live run's folder must survive every maintenance pass.
    ///
    /// What shipped: <c>WriteAutosaveToDisk</c> queues the autosave onto the thread
    /// pool and then, on the SAME tick, writes the position checkpoint — which called
    /// <c>EnsureSaveDirectory</c>, whose <c>PruneEmptyRunFolders</c> found a run folder
    /// with no autosave.json in it yet (the thread pool had not got there) and deleted
    /// it recursively out from under the write in flight. Measured in the editor log:
    /// "Pruned empty run folder: e61d48d7…" immediately before every "QuickSave queued"
    /// for that same id, and the write then died with either "Access to the path is
    /// denied" (the delete caught the temp mid-write) or "Could not find file …tmp"
    /// (the delete won). The run never reached disk at all.
    ///
    /// Both halves are asserted here because either alone passes on broken code. A
    /// pruner that skips the active run is no use if the checkpoint still triggers the
    /// pass on every write, and a checkpoint that stops triggering it is no use once
    /// somebody legitimately prunes while a run is live — which the main menu does,
    /// with no argument, on every return to it.
    ///
    /// Emptiness is not evidence a run is dead while a writer is on its way to it, so
    /// the guard is a REGISTRATION rather than anything inferred from the file system.
    /// </summary>
    [TestFixture]
    public class ActiveRunPruneGuardTests
    {
        private const string TestPrefix = "_test_activerun_";
        private readonly List<string> _createdRunIds = new List<string>();
        private string _previousActiveRunId;

        [SetUp]
        public void SetUp()
        {
            _previousActiveRunId = SaveFileManager.ActiveRunId;
            SaveFileManager.EnsureSaveDirectory();
            _createdRunIds.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // The guard is process-wide state on a static class with Domain Reload OFF:
            // leaving a test's run id registered would silently protect it from every
            // pruning test that runs afterwards.
            SaveFileManager.SetActiveRunId(_previousActiveRunId);

            foreach (var runId in _createdRunIds)
            {
                string runDir = SaveFileManager.GetRunDirectory(runId);
                if (Directory.Exists(runDir))
                {
                    try { Directory.Delete(runDir, recursive: true); } catch { }
                }
            }
        }

        private string NewRunId(string label)
        {
            string id = TestPrefix + label + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            _createdRunIds.Add(id);
            return id;
        }

        /// <summary>An empty run folder — exactly what a run looks like between the
        /// autosave being queued and the thread pool writing it.</summary>
        private string MakeEmptyRunFolder(string label)
        {
            string runId = NewRunId(label);
            Directory.CreateDirectory(SaveFileManager.GetRunDirectory(runId));
            return runId;
        }

        // ── The pruners ──────────────────────────────────────────────────────

        [Test]
        public void EnsureSaveDirectory_DoesNotPruneTheActiveRunsEmptyFolder()
        {
            string active = MakeEmptyRunFolder("active");
            string dead   = MakeEmptyRunFolder("dead");

            SaveFileManager.SetActiveRunId(active);
            SaveFileManager.EnsureSaveDirectory();

            Assert.IsTrue(Directory.Exists(SaveFileManager.GetRunDirectory(active)),
                "The live run's folder was deleted. Its autosave may be a thread-pool " +
                "task away from existing, and the write then dies on the missing directory.");
            Assert.IsFalse(Directory.Exists(SaveFileManager.GetRunDirectory(dead)),
                "Guarding the active run must not stop the pruner doing its job on the rest — " +
                "a guard that protects everything is the same as no pruner at all.");
        }

        [Test]
        public void PrunePhantomRuns_WithNoArgument_StillPreservesTheActiveRun()
        {
            // MainMenuUI prunes with no argument on every return to the menu.
            string active = NewRunId("phantom_active");
            WritePhantomAutosave(active);

            SaveFileManager.SetActiveRunId(active);
            SaveFileManager.PrunePhantomRuns();

            Assert.IsTrue(Directory.Exists(SaveFileManager.GetRunDirectory(active)),
                "A caller that names no run must not wipe the live one. Remembering to pass " +
                "the argument is exactly what the one caller in the project does not do.");
        }

        [Test]
        public void PrunePhantomRuns_StillPrunesAPhantomThatIsNotActive()
        {
            string active  = MakeEmptyRunFolder("guarded");
            string phantom = NewRunId("phantom_dead");
            WritePhantomAutosave(phantom);

            SaveFileManager.SetActiveRunId(active);
            SaveFileManager.PrunePhantomRuns();

            Assert.IsFalse(Directory.Exists(SaveFileManager.GetRunDirectory(phantom)),
                "The guard must name ONE run, not disable phantom pruning.");
        }

        // ── The composition: who triggers a pruning pass ─────────────────────

        [Test]
        public void WritePositionCheckpoint_DoesNotRunTheMaintenancePass()
        {
            // This is the half that actually fired. The checkpoint is mirrored on every
            // full save and written several times a second besides, so routing it
            // through the full EnsureSaveDirectory made a recursive delete pass part of
            // the save path itself.
            string orphan = MakeEmptyRunFolder("untouched_by_checkpoint");

            SaveFileManager.SetActiveRunId(null);
            SaveFileManager.WritePositionCheckpoint(new PositionCheckpointData
            {
                timestamp = "2026-09-07T00:00:00",
                x = 1f,
                y = 2f,
            });

            Assert.IsTrue(Directory.Exists(SaveFileManager.GetRunDirectory(orphan)),
                "Writing a position checkpoint pruned a run folder. The checkpoint needs its " +
                "directory to exist and nothing else; dragging migration and pruning along " +
                "with it put a recursive delete inside the save path.");
        }

        [Test]
        public void WritePositionCheckpoint_StillProducesAReadableCheckpoint()
        {
            // Narrowing what the write does must not narrow what it writes: the
            // checkpoint moved onto the shared atomic writer at the same time.
            var written = new PositionCheckpointData
            {
                timestamp = "2026-09-07T01:02:03",
                x = 12.5f,
                y = -7.25f,
            };

            SaveFileManager.WritePositionCheckpoint(written);
            var read = SaveFileManager.ReadPositionCheckpoint();

            Assert.IsNotNull(read, "The checkpoint did not come back at all.");
            Assert.AreEqual(written.timestamp, read.timestamp);
            Assert.AreEqual(written.x, read.x, 0.0001f);
            Assert.AreEqual(written.y, read.y, 0.0001f);
            Assert.IsTrue(File.Exists(SaveFileManager.GetPositionCheckpointBakPath()),
                "The .bak twin is the only copy left when the primary is caught mid-swap.");
        }

        [Test]
        public void WritePositionCheckpoint_LeavesNoTempBehind()
        {
            string dir = SaveFileManager.GetRecoveryDirectory();
            SaveFileManager.WritePositionCheckpoint(new PositionCheckpointData
            {
                timestamp = "2026-09-07T02:00:00",
                x = 0f,
                y = 0f,
            });

            Assert.IsEmpty(Directory.GetFiles(dir, "*.tmp", SearchOption.TopDirectoryOnly),
                "A temp left in .recovery/ accumulates on a path that writes several times a " +
                "second, and the old hand-rolled write used one fixed name for every writer.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>A single Lv.1 / 0-XP autosave with no run_ordinal — the shape
        /// <see cref="SaveFileManager.IsPhantomRun"/> exists to recognise.</summary>
        private static void WritePhantomAutosave(string runId)
        {
            var data = new GameSaveData
            {
                schemaVersion = "1.0",
                timestamp     = "2026-09-07T00:00:00",
                player = new PlayerSaveData
                {
                    playerClass = "dwarf",
                    hp          = 100,
                    maxHp       = 100,
                    level       = 1,
                    experience  = 0,
                    currentZone = "Lobby",
                    position    = new Vector2(0f, 0f),
                },
            };
            data.SetMeta("run_id", runId);
            SaveFileManager.WriteSaveFile(SaveFileManager.GetAutosavePath(runId), data, "1.0");
        }
    }
}
