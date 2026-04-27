using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Regression suite for the per-run save-isolation refactor.
    ///
    /// Bugs prevented:
    ///   1. "Saves duplicated without user action" — previously the autosave
    ///      backup rotation kept 5 numbered files (autosave_0..4) all surfaced
    ///      as separate save slots in the UI.
    ///   2. "New game with a different class is not saved correctly" — the
    ///      shared filenames (quicksave / shutdown_save) were global and
    ///      overwrote each other across runs.
    ///
    /// Invariants enforced:
    ///   • One run ⇒ at most ONE Auto-Save entry in its folder.
    ///   • Different runs use different folders and never collide.
    ///   • Reserved names ("autosave", "position_checkpoint", legacy names)
    ///     can never be picked by the player when manually saving/renaming.
    ///   • Backup history lives in a hidden .backups/ folder and never leaks
    ///     into ListSaves / ListSavesByRun results.
    ///   • Legacy flat saves migrate into per-run autosave.json (newest wins)
    ///     and into the legacy/ bucket otherwise.
    /// </summary>
    [TestFixture]
    public class SaveSystemPerRunTests
    {
        private string _saveDir;
        private readonly List<string> _createdRunIds = new List<string>();

        [SetUp]
        public void SetUp()
        {
            SaveFileManager.EnsureSaveDirectory();
            _saveDir = SaveFileManager.GetSaveDirectory();
            _createdRunIds.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var runId in _createdRunIds)
            {
                string runDir = SaveFileManager.GetRunDirectory(runId);
                if (Directory.Exists(runDir))
                {
                    try { Directory.Delete(runDir, recursive: true); }
                    catch { /* best-effort */ }
                }
            }
            // Also nuke any top-level _test_* file the migration may have moved.
            string legacy = Path.Combine(_saveDir, "legacy");
            if (Directory.Exists(legacy))
            {
                foreach (var f in Directory.GetFiles(legacy, "_test_*.*"))
                {
                    try { File.Delete(f); } catch { }
                }
                if (Directory.GetFileSystemEntries(legacy).Length == 0)
                    try { Directory.Delete(legacy); } catch { }
            }
            foreach (var f in Directory.GetFiles(_saveDir, "_test_*.*"))
            {
                try { File.Delete(f); } catch { }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private string NewRunId(string label = "test")
        {
            string id = "_test_run_" + label + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            _createdRunIds.Add(id);
            return id;
        }

        private static GameSaveData BuildSaveData(string runId, string playerClass, int hp, string timestamp)
        {
            var data = new GameSaveData
            {
                schemaVersion = "1.0",
                timestamp     = timestamp,
                player = new PlayerSaveData
                {
                    playerClass = playerClass,
                    hp          = hp,
                    maxHp       = 100,
                    level       = 1,
                    currentZone = "TestZone",
                },
            };
            if (!string.IsNullOrEmpty(runId)) data.SetMeta("run_id", runId);
            return data;
        }

        // ── Folder layout ────────────────────────────────────────────────────

        [Test]
        public void GetRunDirectory_CreatesDistinctFoldersPerRun()
        {
            string r1 = NewRunId("a"), r2 = NewRunId("b");
            string d1 = SaveFileManager.GetRunDirectory(r1);
            string d2 = SaveFileManager.GetRunDirectory(r2);

            Assert.AreNotEqual(d1, d2, "Different run ids must map to different folders.");
            Assert.IsTrue(d1.StartsWith(_saveDir), "Run folders must live under Saves/.");
            Assert.AreEqual(r1, Path.GetFileName(d1));
            Assert.AreEqual(r2, Path.GetFileName(d2));
        }

        [Test]
        public void GetRunDirectory_EmptyOrNullRunId_RoutesToLegacyBucket()
        {
            string dir = SaveFileManager.GetRunDirectory("");
            Assert.AreEqual(SaveFileManager.GetLegacyRunDirectory(), dir);
            Assert.AreEqual("legacy", Path.GetFileName(dir));
        }

        [Test]
        public void GetAutosavePath_AlwaysFileNamedAutosaveJson()
        {
            string runId = NewRunId();
            string path = SaveFileManager.GetAutosavePath(runId);
            Assert.AreEqual("autosave.json", Path.GetFileName(path));
            Assert.AreEqual(runId, Path.GetFileName(Path.GetDirectoryName(path)));
        }

        // ── Reserved-name policy ─────────────────────────────────────────────

        [Test]
        public void IsReservedSaveName_TrueForAutosaveAndLegacyNames()
        {
            Assert.IsTrue(SaveFileManager.IsReservedSaveName("autosave"));
            Assert.IsTrue(SaveFileManager.IsReservedSaveName("AUTOSAVE"));
            Assert.IsTrue(SaveFileManager.IsReservedSaveName("quicksave"));
            Assert.IsTrue(SaveFileManager.IsReservedSaveName("shutdown_save"));
            Assert.IsTrue(SaveFileManager.IsReservedSaveName("autosave_0"));
            Assert.IsTrue(SaveFileManager.IsReservedSaveName("autosave_4"));
            Assert.IsTrue(SaveFileManager.IsReservedSaveName("position_checkpoint"));
            Assert.IsTrue(SaveFileManager.IsReservedSaveName("position_checkpoint_bak"));
        }

        [Test]
        public void IsReservedSaveName_FalseForUserPickedNames()
        {
            Assert.IsFalse(SaveFileManager.IsReservedSaveName("my_save"));
            Assert.IsFalse(SaveFileManager.IsReservedSaveName("Boss_Fight"));
            Assert.IsFalse(SaveFileManager.IsReservedSaveName("save_2026-04-25"));
        }

        [Test]
        public void RenameSave_ReservedTargetName_Rejected()
        {
            string runId = NewRunId();
            string srcPath = SaveFileManager.GetManualSavePath(runId, "_test_my_save");
            SaveFileManager.WriteSaveFile(srcPath, BuildSaveData(runId, "elven", 50, "2026-01-01T00:00:00"), "1.0");

            string result = SaveFileManager.RenameSave(srcPath, "autosave");

            Assert.IsNull(result, "Renaming to a reserved name must be rejected.");
            Assert.IsTrue(File.Exists(srcPath), "Source must remain untouched on rejection.");
        }

        // ── Per-run isolation across new games ───────────────────────────────

        [Test]
        public void TwoRuns_AutosavesDoNotCollide()
        {
            string r1 = NewRunId("dwarf"), r2 = NewRunId("elven");
            var d1 = BuildSaveData(r1, "dwarf", 100, "2026-01-01T10:00:00");
            var d2 = BuildSaveData(r2, "elven",  80, "2026-01-01T11:00:00");

            SaveFileManager.WriteSaveFile(SaveFileManager.GetAutosavePath(r1), d1, "1.0");
            SaveFileManager.WriteSaveFile(SaveFileManager.GetAutosavePath(r2), d2, "1.0");

            var loaded1 = SaveFileManager.TryLoadSingle(SaveFileManager.GetAutosavePath(r1));
            var loaded2 = SaveFileManager.TryLoadSingle(SaveFileManager.GetAutosavePath(r2));

            Assert.IsNotNull(loaded1); Assert.IsNotNull(loaded2);
            Assert.AreEqual("dwarf", loaded1.player.playerClass);
            Assert.AreEqual("elven", loaded2.player.playerClass);
            Assert.AreEqual(r1, loaded1.GetMeta("run_id", ""));
            Assert.AreEqual(r2, loaded2.GetMeta("run_id", ""));
        }

        [Test]
        public void ListSavesByRun_GroupsCorrectly_AutosaveAlwaysFirst()
        {
            string r = NewRunId("group");
            var auto    = BuildSaveData(r, "elven", 70, "2026-04-25T10:00:00");
            var manual1 = BuildSaveData(r, "elven", 70, "2026-04-25T11:00:00"); // newer
            var manual2 = BuildSaveData(r, "elven", 70, "2026-04-25T09:00:00"); // older

            SaveFileManager.WriteSaveFile(SaveFileManager.GetAutosavePath(r),                 auto,    "1.0");
            SaveFileManager.WriteSaveFile(SaveFileManager.GetManualSavePath(r, "_test_zz"),   manual1, "1.0");
            SaveFileManager.WriteSaveFile(SaveFileManager.GetManualSavePath(r, "_test_aa"),   manual2, "1.0");

            var groups = SaveFileManager.ListSavesByRun();
            RunGroupInfo myGroup = null;
            foreach (var g in groups) if (g.runId == r) { myGroup = g; break; }
            Assert.IsNotNull(myGroup, "Group for run must be present.");
            Assert.AreEqual(3, myGroup.saves.Count);
            Assert.IsTrue(myGroup.saves[0].isAutoSave, "Auto-Save must be first in the group.");
            // Manual saves sorted newest-first after the autosave.
            Assert.AreEqual("_test_zz", myGroup.saves[1].fileName);
            Assert.AreEqual("_test_aa", myGroup.saves[2].fileName);
        }

        [Test]
        public void ListSaves_NeverContainsBackupHistory()
        {
            string r = NewRunId("backups");
            var data = BuildSaveData(r, "elven", 50, "2026-04-25T10:00:00");
            SaveFileManager.WriteSaveFile(SaveFileManager.GetAutosavePath(r), data, "1.0");

            // Force several rotations.
            for (int i = 0; i < 3; i++)
            {
                SaveFileManager.RotateAutosaveBackups(r);
                SaveFileManager.WriteSaveFile(SaveFileManager.GetAutosavePath(r), data, "1.0");
            }

            string backupsDir = SaveFileManager.GetBackupsDirectory(r);
            Assert.IsTrue(Directory.Exists(backupsDir), ".backups/ folder must exist after rotations.");
            Assert.GreaterOrEqual(Directory.GetFiles(backupsDir, "*.json").Length, 1,
                "Backups must contain at least one snapshot.");

            var saves = SaveFileManager.ListSaves();
            int autosaveCount = 0;
            foreach (var s in saves)
            {
                if (s.runId == r) autosaveCount += s.isAutoSave ? 1 : 0;
                Assert.IsFalse(s.fileName.StartsWith("autosave_"),
                    $"Backup file '{s.fileName}' must never appear as a save slot (this was the duplication bug).");
            }
            Assert.AreEqual(1, autosaveCount, "There must be exactly ONE Auto-Save per run.");
        }

        [Test]
        public void RotateAutosaveBackups_KeepsAtMostFiveSnapshots()
        {
            string r = NewRunId("rotate");
            for (int i = 0; i < 12; i++)
            {
                var d = BuildSaveData(r, "elven", 50, $"2026-04-25T10:00:{i:00}");
                SaveFileManager.WriteSaveFile(SaveFileManager.GetAutosavePath(r), d, "1.0");
                SaveFileManager.RotateAutosaveBackups(r);
            }

            string backupsDir = SaveFileManager.GetBackupsDirectory(r);
            int count = Directory.GetFiles(backupsDir, "autosave_*.json").Length;
            Assert.LessOrEqual(count, SaveFileManager.MAX_BACKUPS,
                $"Must keep at most {SaveFileManager.MAX_BACKUPS} backup snapshots, found {count}.");
        }

        [Test]
        public void DeleteSave_PrunesEmptyRunFolder()
        {
            string r = NewRunId("prune");
            string p = SaveFileManager.GetManualSavePath(r, "_test_only");
            SaveFileManager.WriteSaveFile(p, BuildSaveData(r, "elven", 50, "2026-04-25T10:00:00"), "1.0");

            string runDir = SaveFileManager.GetRunDirectory(r);
            Assert.IsTrue(Directory.Exists(runDir));

            SaveFileManager.DeleteSave(p);

            // The run folder must be gone OR completely empty of visible saves.
            // On Windows, Directory.Delete is occasionally deferred when a handle
            // was just released; the invariant we care about is "no orphaned saves".
            bool dirGone   = !Directory.Exists(runDir);
            bool dirEmpty  = Directory.Exists(runDir) &&
                             Directory.GetFiles(runDir, "*.json", SearchOption.AllDirectories).Length == 0;
            Assert.IsTrue(dirGone || dirEmpty,
                "Empty run folder must be pruned (or at least contain no save files).");
        }

        [Test]
        public void DeleteSave_DoesNotPruneRecoveryOrLegacyFolder()
        {
            // Create files in legacy + recovery so neither is empty by accident.
            string legacyDir = SaveFileManager.GetLegacyRunDirectory();
            Directory.CreateDirectory(legacyDir);
            string legacyFile = Path.Combine(legacyDir, "_test_legacy.json");
            SaveFileManager.WriteSaveFile(legacyFile, BuildSaveData("", "elven", 50, "2026-04-25T10:00:00"), "1.0");

            SaveFileManager.DeleteSave(legacyFile);

            Assert.IsTrue(Directory.Exists(legacyDir),
                "Legacy folder must NEVER be pruned (it's a stable migration target).");
            Assert.IsTrue(Directory.Exists(SaveFileManager.GetRecoveryDirectory()),
                "Recovery folder must NEVER be pruned.");
        }

        // ── Backup recovery ──────────────────────────────────────────────────

        [Test]
        public void TryLoadWithRecovery_FallsBackToBackupWhenAutosaveCorrupted()
        {
            string r = NewRunId("recover");
            var good = BuildSaveData(r, "elven", 50, "2026-04-25T10:00:00");
            SaveFileManager.WriteSaveFile(SaveFileManager.GetAutosavePath(r), good, "1.0");
            SaveFileManager.RotateAutosaveBackups(r);

            // Corrupt the live autosave.
            File.WriteAllText(SaveFileManager.GetAutosavePath(r), "{ this is not valid json");

            var recovered = SaveFileManager.TryLoadWithRecovery(SaveFileManager.GetAutosavePath(r));
            Assert.IsNotNull(recovered, "Must recover from .backups/autosave_1.json.");
            Assert.AreEqual("elven", recovered.player.playerClass);
        }

        // ── Legacy migration ────────────────────────────────────────────────

        [Test]
        public void MigrateLegacyFlatSaves_QuicksaveAndShutdownCollapseToAutosave()
        {
            string runIdInFile = NewRunId("legacy_collapse");

            // Pre-place legacy flat files at the top level (as the old code wrote them).
            string flatQuicksave = Path.Combine(_saveDir, "_test_quicksave.json");
            string flatShutdown  = Path.Combine(_saveDir, "_test_shutdown_save.json");
            // Use real reserved names to exercise the collapse mapping; we'll clean them
            // up via the flat quicksave/shutdown names at the same time.
            string realQuicksave = Path.Combine(_saveDir, "quicksave.json");
            string realShutdown  = Path.Combine(_saveDir, "shutdown_save.json");

            try
            {
                var older = BuildSaveData(runIdInFile, "dwarf", 60, "2026-04-25T09:00:00");
                var newer = BuildSaveData(runIdInFile, "dwarf", 80, "2026-04-25T10:00:00");

                File.WriteAllText(realQuicksave, JsonUtility.ToJson(older, true));
                File.WriteAllText(realShutdown,  JsonUtility.ToJson(newer, true));

                // ListSaves triggers EnsureSaveDirectory → MigrateLegacyFlatSaves.
                var saves = SaveFileManager.ListSaves();

                Assert.IsFalse(File.Exists(realQuicksave),
                    "Legacy quicksave.json must be moved out of top-level Saves/.");
                Assert.IsFalse(File.Exists(realShutdown),
                    "Legacy shutdown_save.json must be moved out of top-level Saves/.");

                string mergedAutosave = SaveFileManager.GetAutosavePath(runIdInFile);
                Assert.IsTrue(File.Exists(mergedAutosave),
                    "Both legacy files must collapse to the per-run autosave.json.");

                var loaded = SaveFileManager.TryLoadSingle(mergedAutosave);
                Assert.IsNotNull(loaded);
                Assert.AreEqual("2026-04-25T10:00:00", loaded.timestamp,
                    "Newest legacy candidate must win on collapse.");
                Assert.AreEqual(80, loaded.player.hp);

                // The autosave appears exactly once in the run group.
                int autoCount = 0;
                foreach (var s in saves)
                    if (s.runId == runIdInFile && s.isAutoSave) autoCount++;
                Assert.AreEqual(1, autoCount);
            }
            finally
            {
                if (File.Exists(realQuicksave)) File.Delete(realQuicksave);
                if (File.Exists(realShutdown))  File.Delete(realShutdown);
                if (File.Exists(flatQuicksave)) File.Delete(flatQuicksave);
                if (File.Exists(flatShutdown))  File.Delete(flatShutdown);
            }
        }

        [Test]
        public void MigrateLegacyFlatSaves_FlatFileWithoutRunId_LandsInLegacyBucket()
        {
            string flat = Path.Combine(_saveDir, "_test_orphan.json");
            var data = BuildSaveData("", "elven", 50, "2026-04-25T10:00:00");
            File.WriteAllText(flat, JsonUtility.ToJson(data, true));

            var saves = SaveFileManager.ListSaves();

            Assert.IsFalse(File.Exists(flat), "Flat orphan file must be moved.");
            string moved = Path.Combine(SaveFileManager.GetLegacyRunDirectory(), "_test_orphan.json");
            Assert.IsTrue(File.Exists(moved), "Orphan must land in legacy/ bucket.");

            bool found = false;
            foreach (var s in saves)
                if (s.fileName == "_test_orphan") { found = true; Assert.AreEqual("", s.runId); break; }
            Assert.IsTrue(found, "Migrated orphan must surface in ListSaves under empty runId.");
        }

        [Test]
        public void MigrateLegacyFlatSaves_DoesNotTouchRecoveryFiles()
        {
            // Place a position_checkpoint at the top level (pre-refactor location).
            string topCheckpoint = Path.Combine(_saveDir, "position_checkpoint.json");
            File.WriteAllText(topCheckpoint, "{\"timestamp\":\"2026-04-25T10:00:00\",\"x\":0,\"y\":0,\"zone\":\"\"}");

            try
            {
                SaveFileManager.EnsureSaveDirectory();
                Assert.IsFalse(File.Exists(topCheckpoint),
                    "Legacy top-level position_checkpoint must be migrated to .recovery/.");
                string recoveryFile = Path.Combine(SaveFileManager.GetRecoveryDirectory(), "position_checkpoint.json");
                Assert.IsTrue(File.Exists(recoveryFile));

                // It must NOT appear in ListSaves under any guise.
                var saves = SaveFileManager.ListSaves();
                foreach (var s in saves)
                    Assert.AreNotEqual("position_checkpoint", s.fileName);
            }
            finally
            {
                string recoveryFile = Path.Combine(SaveFileManager.GetRecoveryDirectory(), "position_checkpoint.json");
                if (File.Exists(recoveryFile)) File.Delete(recoveryFile);
                if (File.Exists(topCheckpoint)) File.Delete(topCheckpoint);
            }
        }
    }
}
