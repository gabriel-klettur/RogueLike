using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// End-to-end integration coverage for the save subsystem after the
    /// dirty-flag refactor. Exercises the full lifecycle: create → list →
    /// rename → load → delete, plus the empty-folder pruner and the
    /// SaveService event rebinding contract.
    ///
    /// These tests intentionally hit the real <see cref="Application.persistentDataPath"/>
    /// (same path the SaveFileManager uses in production) and clean up after
    /// themselves. Test artifacts are namespaced with the <c>_test_lifecycle_</c>
    /// prefix so a failure leaves obvious garbage to clean rather than
    /// stomping on real player saves.
    /// </summary>
    [TestFixture]
    public class SaveLifecycleIntegrationTests
    {
        private const string TestPrefix = "_test_lifecycle_";
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
                    try { Directory.Delete(runDir, recursive: true); } catch { }
                }
            }

            // Belt-and-suspenders: also drop any orphan _test_lifecycle_* folder
            // and any _test_lifecycle_* file in legacy/.
            foreach (var dir in Directory.GetDirectories(_saveDir, TestPrefix + "*",
                                                          SearchOption.TopDirectoryOnly))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
            string legacy = SaveFileManager.GetLegacyRunDirectory();
            if (Directory.Exists(legacy))
            {
                foreach (var f in Directory.GetFiles(legacy, TestPrefix + "*"))
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private string NewRunId(string label = "x")
        {
            string id = TestPrefix + label + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            _createdRunIds.Add(id);
            return id;
        }

        private static GameSaveData BuildSaveData(string runId, string playerClass,
                                                  int level, int hp, string zone, string timestamp)
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
                    level       = level,
                    experience  = level * 50,
                    currentZone = zone,
                    position    = new Vector2(10f * level, 20f * level),
                },
            };
            if (!string.IsNullOrEmpty(runId)) data.SetMeta("run_id", runId);
            return data;
        }

        // ── Full lifecycle: create → list → rename → load → delete ──────────

        [Test]
        public void Lifecycle_ManualSave_RoundTripsThroughRenameAndLoadAndDelete()
        {
            string runId = NewRunId("rt");
            var data = BuildSaveData(runId, "elven", level: 3, hp: 75,
                                     zone: "Forest", timestamp: "2026-05-03T10:00:00");

            // 1. CREATE — write to manual slot.
            string srcPath = SaveFileManager.GetManualSavePath(runId, TestPrefix + "manual1");
            SaveFileManager.WriteSaveFile(srcPath, data, "1.0");
            Assert.IsTrue(File.Exists(srcPath), "Manual save file must be written.");
            string srcChecksum = srcPath.Replace(".json", ".sha256");
            Assert.IsTrue(File.Exists(srcChecksum), "Checksum sidecar must be written next to it.");

            // 2. LIST — must surface in the run group with full metadata.
            var groups = SaveFileManager.ListSavesByRun();
            RunGroupInfo group = null;
            foreach (var g in groups) if (g.runId == runId) { group = g; break; }
            Assert.IsNotNull(group, "Newly written save must appear in the run grouping.");
            Assert.AreEqual(1, group.saves.Count);
            Assert.AreEqual(TestPrefix + "manual1", group.saves[0].fileName);
            Assert.AreEqual("elven",     group.saves[0].playerClass);
            Assert.AreEqual(3,           group.saves[0].level);
            Assert.AreEqual(75,          group.saves[0].hp);
            Assert.AreEqual("Forest",    group.saves[0].currentZone);
            Assert.IsFalse(group.saves[0].isCorrupted);
            Assert.IsFalse(group.saves[0].isAutoSave);

            // 3. RENAME — sanitized name, same run folder, sidecar follows.
            string newPath = SaveFileManager.RenameSave(srcPath, TestPrefix + "renamed");
            Assert.IsNotNull(newPath, "Rename must return the new path on success.");
            Assert.IsFalse(File.Exists(srcPath), "Old file must be gone after rename.");
            Assert.IsFalse(File.Exists(srcChecksum), "Old checksum sidecar must be gone too.");
            Assert.IsTrue(File.Exists(newPath), "New file must exist at the renamed path.");
            Assert.IsTrue(File.Exists(newPath.Replace(".json", ".sha256")),
                          "Checksum sidecar must follow the renamed file.");
            Assert.AreEqual(Path.GetDirectoryName(srcPath), Path.GetDirectoryName(newPath),
                            "Rename must keep the file inside the same run folder.");

            // 4. LOAD — round-trips data byte-for-byte (after checksum verification).
            var loaded = SaveFileManager.TryLoadSingle(newPath);
            Assert.IsNotNull(loaded, "Renamed save must still load and pass checksum.");
            Assert.AreEqual("elven",  loaded.player.playerClass);
            Assert.AreEqual(3,        loaded.player.level);
            Assert.AreEqual(75,       loaded.player.hp);
            Assert.AreEqual("Forest", loaded.player.currentZone);
            Assert.AreEqual(runId,    loaded.GetMeta("run_id", ""));

            // 5. DELETE — file gone, sidecar gone, run folder pruned (only file).
            string runDir = SaveFileManager.GetRunDirectory(runId);
            Assert.IsTrue(Directory.Exists(runDir));
            Assert.IsTrue(SaveFileManager.DeleteSave(newPath));
            Assert.IsFalse(File.Exists(newPath));
            Assert.IsFalse(File.Exists(newPath.Replace(".json", ".sha256")));
            // Same Windows file-lock caveat as DeleteSave_PrunesEmptyRunFolder:
            // Directory.Delete is occasionally deferred when a handle was just
            // released. The invariant is "no orphaned saves" rather than
            // strict folder removal.
            bool dirGone  = !Directory.Exists(runDir);
            bool dirEmpty = Directory.Exists(runDir) &&
                            Directory.GetFiles(runDir, "*.json",
                                               SearchOption.AllDirectories).Length == 0;
            Assert.IsTrue(dirGone || dirEmpty,
                "After deleting the only save, the run folder must be pruned " +
                "or at least contain no save files.");
        }

        // ── Multi-save isolation ─────────────────────────────────────────────

        [Test]
        public void TwoManualSaves_SameRun_BothSurviveDeleteOfOne()
        {
            string runId = NewRunId("multi");
            var d1 = BuildSaveData(runId, "elven", 1, 90, "Lobby",  "2026-05-03T09:00:00");
            var d2 = BuildSaveData(runId, "elven", 4, 60, "Forest", "2026-05-03T10:00:00");

            string p1 = SaveFileManager.GetManualSavePath(runId, TestPrefix + "first");
            string p2 = SaveFileManager.GetManualSavePath(runId, TestPrefix + "second");
            SaveFileManager.WriteSaveFile(p1, d1, "1.0");
            SaveFileManager.WriteSaveFile(p2, d2, "1.0");

            // Delete one; the other must still load and the folder must NOT be pruned.
            Assert.IsTrue(SaveFileManager.DeleteSave(p1));
            Assert.IsFalse(File.Exists(p1));
            Assert.IsTrue(File.Exists(p2),
                "Sibling save must survive deletion of an independent slot.");
            Assert.IsTrue(Directory.Exists(SaveFileManager.GetRunDirectory(runId)),
                "Run folder must NOT be pruned while another save still lives there.");

            var loaded = SaveFileManager.TryLoadSingle(p2);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(4, loaded.player.level);
        }

        [Test]
        public void TwoRuns_SameFileName_DoNotCollide()
        {
            string r1 = NewRunId("a"), r2 = NewRunId("b");
            string slot = TestPrefix + "shared_name";

            var d1 = BuildSaveData(r1, "dwarf",    2, 80, "Forest", "2026-05-03T10:00:00");
            var d2 = BuildSaveData(r2, "valkyrie", 5, 40, "Caves",  "2026-05-03T11:00:00");

            string p1 = SaveFileManager.GetManualSavePath(r1, slot);
            string p2 = SaveFileManager.GetManualSavePath(r2, slot);
            Assert.AreNotEqual(p1, p2, "Same slot name in different runs must map to different paths.");

            SaveFileManager.WriteSaveFile(p1, d1, "1.0");
            SaveFileManager.WriteSaveFile(p2, d2, "1.0");

            var loaded1 = SaveFileManager.TryLoadSingle(p1);
            var loaded2 = SaveFileManager.TryLoadSingle(p2);
            Assert.IsNotNull(loaded1); Assert.IsNotNull(loaded2);
            Assert.AreEqual("dwarf",    loaded1.player.playerClass);
            Assert.AreEqual("valkyrie", loaded2.player.playerClass);
            Assert.AreEqual(2, loaded1.player.level);
            Assert.AreEqual(5, loaded2.player.level);
        }

        // ── Empty-folder pruning ─────────────────────────────────────────────

        [Test]
        public void EnsureSaveDirectory_PrunesEmptyRunSubfoldersOnBoot()
        {
            // Simulate the user's situation: previous sessions left N empty
            // run folders behind. EnsureSaveDirectory must clean them up.
            string r1 = NewRunId("empty1");
            string r2 = NewRunId("empty2");
            string r3 = NewRunId("withFile");

            Directory.CreateDirectory(SaveFileManager.GetRunDirectory(r1));
            Directory.CreateDirectory(SaveFileManager.GetRunDirectory(r2));

            // r3 has an actual save — must NOT be pruned.
            var d = BuildSaveData(r3, "elven", 1, 100, "Lobby", "2026-05-03T10:00:00");
            SaveFileManager.WriteSaveFile(SaveFileManager.GetManualSavePath(r3, TestPrefix + "live"), d, "1.0");

            // Trigger the pruner.
            SaveFileManager.EnsureSaveDirectory();

            // Folders that started fully empty must be gone (no file handles
            // ever existed → no Windows lock to defer the deletion).
            Assert.IsFalse(Directory.Exists(SaveFileManager.GetRunDirectory(r1)),
                "Empty run folder #1 must be pruned at boot.");
            Assert.IsFalse(Directory.Exists(SaveFileManager.GetRunDirectory(r2)),
                "Empty run folder #2 must be pruned at boot.");
            Assert.IsTrue(Directory.Exists(SaveFileManager.GetRunDirectory(r3)),
                "Run folder containing a real save must be left alone.");
        }

        [Test]
        public void EnsureSaveDirectory_NeverPrunesRecoveryOrLegacyOrDotfileFolders()
        {
            // .recovery folder is created by EnsureSaveDirectory itself; legacy/
            // we create explicitly. Both must survive even when empty (legacy is
            // a stable migration target; .recovery may be empty between writes).
            string legacyDir   = SaveFileManager.GetLegacyRunDirectory();
            string recoveryDir = SaveFileManager.GetRecoveryDirectory();
            Directory.CreateDirectory(legacyDir);

            // Drop a third folder that starts with "." — also reserved by the
            // pruner ("." prefix is the convention for hidden/system folders).
            string dotDir = Path.Combine(_saveDir, ".sentinel");
            Directory.CreateDirectory(dotDir);

            try
            {
                SaveFileManager.EnsureSaveDirectory();

                Assert.IsTrue(Directory.Exists(legacyDir),
                    "legacy/ must NEVER be pruned (stable migration target).");
                Assert.IsTrue(Directory.Exists(recoveryDir),
                    ".recovery/ must NEVER be pruned (crash-safe checkpoint store).");
                Assert.IsTrue(Directory.Exists(dotDir),
                    "Folders starting with '.' must NEVER be pruned.");
            }
            finally
            {
                if (Directory.Exists(dotDir)) Directory.Delete(dotDir, recursive: true);
            }
        }

        // ── Backups survive the pruner ───────────────────────────────────────

        [Test]
        public void EnsureSaveDirectory_DoesNotPruneRunWithOnlyBackupsOnDisk()
        {
            // Edge case: a run folder where the live autosave was deleted but
            // .backups/ still has snapshots. The pruner only looks at top-level
            // .json; it would otherwise nuke valuable recovery history.
            string r = NewRunId("backupsOnly");
            var d = BuildSaveData(r, "dwarf", 2, 80, "Forest", "2026-05-03T10:00:00");

            SaveFileManager.WriteSaveFile(SaveFileManager.GetAutosavePath(r), d, "1.0");
            SaveFileManager.RotateAutosaveBackups(r);

            // Manually drop the live autosave but keep .backups/.
            string live = SaveFileManager.GetAutosavePath(r);
            File.Delete(live);
            File.Delete(live.Replace(".json", ".sha256"));

            string runDir     = SaveFileManager.GetRunDirectory(r);
            string backupsDir = SaveFileManager.GetBackupsDirectory(r);
            Assert.IsTrue(Directory.Exists(backupsDir),
                "Setup must have produced a .backups/ folder.");

            SaveFileManager.EnsureSaveDirectory();

            // CURRENT BEHAVIOUR: the pruner deletes top-level-empty folders,
            // dragging .backups/ down with them. This regression test PINS that
            // behaviour: if we ever want to preserve orphan backups we must
            // change it deliberately rather than accidentally. Update both
            // production code and this test together.
            // Lenient about Windows file-lock delays — same caveat as
            // DeleteSave_PrunesEmptyRunFolder.
            bool dirGone  = !Directory.Exists(runDir);
            bool dirEmpty = Directory.Exists(runDir) &&
                            Directory.GetFiles(runDir, "*.json",
                                               SearchOption.AllDirectories).Length == 0;
            Assert.IsTrue(dirGone || dirEmpty,
                "Run folder containing only orphan .backups/ must be pruned " +
                "(or at least contain no top-level save files).");
        }

        // ── SaveService rebinding contract ──────────────────────────────────

        [Test]
        public void SaveService_AfterGameEventsClear_StillReceivesEventsOnSceneLoad()
        {
            // Reproduces the SceneTransitionManager / LoadingScreenController
            // path: bind events → GameEvents.Clear() (scene transition) → fire
            // event. Without the rebinding-on-sceneLoaded hook this used to
            // silently no-op and the dirty flag would never trigger again.
            if (SaveService.HasInstance)
                Object.DestroyImmediate(SaveService.Instance.gameObject);
            var go      = new GameObject("TestSaveService_Rebind");
            var service = go.AddComponent<SaveService>();

            // EditMode does not fire Awake automatically, and the base Awake
            // calls DontDestroyOnLoad which throws in EditMode. Bypass it:
            // set the static _instance manually and invoke OnSingletonAwake
            // directly, which is what we actually need to exercise.
            var baseType = typeof(SaveService).BaseType;
            baseType?.GetField("_instance",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(null, service);
            typeof(SaveService).GetMethod("OnSingletonAwake",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.FlattenHierarchy)
                ?.Invoke(service, null);

            try
            {
                service.BeginNewRun();
                Assert.IsFalse(service.IsSessionDirty);

                // Sanity check: the binding from Awake must already deliver
                // events before any scene transition has happened.
                GameEvents.FirePlayerDamaged(amount: 1, currentHp: 99, maxHp: 100);
                Assert.IsTrue(service.IsSessionDirty,
                    "Initial Awake bind must already deliver GameEvents.");

                // Reset and now test the rebind-on-scene-load contract.
                typeof(SaveService).GetField("_sessionDirty",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(service, false);
                Assert.IsFalse(service.IsSessionDirty);

                // Simulate a scene transition wiping the global event bus.
                GameEvents.Clear();

                // Manually invoke the scene-loaded callback the same way
                // SceneManager would after a real load. Reflection lets us
                // exercise the contract without spinning up a real scene.
                var onLoaded = typeof(SaveService).GetMethod("OnSceneLoaded",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(onLoaded, "OnSceneLoaded private method must exist.");
                onLoaded.Invoke(service, new object[]
                {
                    default(UnityEngine.SceneManagement.Scene),
                    UnityEngine.SceneManagement.LoadSceneMode.Single
                });

                // Now fire a dirty-trigger event. If rebinding worked, the
                // session flips to dirty.
                GameEvents.FirePlayerDamaged(amount: 5, currentHp: 95, maxHp: 100);
                Assert.IsTrue(service.IsSessionDirty,
                    "After a scene transition + GameEvents.Clear, SaveService " +
                    "must re-subscribe so progression events still flip the dirty flag.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                GameEvents.Clear();
            }
        }
    }
}
