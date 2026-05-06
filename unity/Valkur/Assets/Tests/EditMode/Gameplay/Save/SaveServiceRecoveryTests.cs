using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

// EditMode does NOT fire Unity lifecycle methods (Awake/Start/OnEnable) on
// AddComponent. We invoke the singleton init manually via reflection — the
// same ForceSingletonInit pattern used by SaveServiceDirtyAndImmediateTests.
//
// Tests F/G/H use SaveService.Load(path), which internally calls
// SaveFileManager.TryLoadWithRecoveryDetailed and then GameStateRestorer.Restore.
// Restore exits early with a warning when EntityRegistry.Player is null (no player
// in EditMode), so LogAssert.ignoreFailingMessages suppresses that warning.

namespace Valkur.Tests.EditMode.Gameplay.Save
{
    /// <summary>
    /// Covers the Phase-3 additions to the save-recovery pipeline:
    ///   A) TryLoadWithRecoveryDetailed — healthy primary returns primary.
    ///   B) TryLoadWithRecoveryDetailed — corrupted primary falls back to backup 1.
    ///   C) TryLoadWithRecoveryDetailed — primary + backup 1 corrupted, falls back to backup 2.
    ///   D) TryLoadWithRecoveryDetailed — all files corrupted returns Empty.
    ///   E) TryLoadWithRecoveryDetailed — no .backups/ directory returns Empty.
    ///   F) SaveService.OnSaveRecovered — fires exactly once with correct data when primary corrupted.
    ///   G) SaveService.OnSaveRecovered — does NOT fire on a healthy primary.
    ///   H) SaveService.OnSaveRecovered — faulty subscriber does not prevent Load from returning true.
    /// </summary>
    [TestFixture]
    public class SaveServiceRecoveryTests
    {
        // ── Fields ─────────────────────────────────────────────────────────────

        private GameObject  _saveServiceGo;
        private SaveService _saveService;

        // Every temp dir created in a test is registered here and deleted in TearDown.
        private readonly List<string> _tempDirs = new List<string>();

        // Handler storage for OnSaveRecovered — unsubscribed in TearDown.
        private System.Action<SaveLoadResult> _recoveredHandler;

        // ── Reflection helpers ─────────────────────────────────────────────────

        private static void ForceSingletonInit(SaveService svc)
        {
            var baseType      = typeof(SaveService).BaseType;
            var instanceField = baseType?.GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            instanceField?.SetValue(null, svc);

            var onAwake = typeof(SaveService).GetMethod("OnSingletonAwake",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            onAwake?.Invoke(svc, null);
        }

        // ── Minimal valid GameSaveData factory ─────────────────────────────────

        private static GameSaveData MakeData(string tag = "test")
        {
            return new GameSaveData
            {
                timestamp     = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                schemaVersion = "1.1",
                player = new PlayerSaveData
                {
                    playerClass = tag,
                    hp          = 100,
                    maxHp       = 100,
                    level       = 1,
                    experience  = 0,
                    currentZone = "Zone_Test"
                }
            };
        }

        // ── Temp directory helpers ─────────────────────────────────────────────

        private string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ValkurRecoveryTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);
            return dir;
        }

        /// <summary>
        /// Write a valid autosave.json (with correct checksum sidecar) under <paramref name="runDir"/>.
        /// Returns the path.
        /// </summary>
        private static string WriteValidAutosave(string runDir, string tag = "valid")
        {
            string path = Path.Combine(runDir, "autosave.json");
            Directory.CreateDirectory(runDir);
            SaveFileManager.WriteSaveFile(path, MakeData(tag), "1.1");
            return path;
        }

        /// <summary>
        /// Write a valid autosave_N.json under <paramref name="backupsDir"/>.
        /// </summary>
        private static string WriteValidBackup(string backupsDir, int slot, string tag = "backup")
        {
            string path = Path.Combine(backupsDir, $"autosave_{slot}.json");
            Directory.CreateDirectory(backupsDir);
            SaveFileManager.WriteSaveFile(path, MakeData(tag), "1.1");
            return path;
        }

        /// <summary>
        /// Overwrite the JSON body of <paramref name="path"/> with garbage so the
        /// checksum sidecar (written by WriteSaveFile) no longer matches.
        /// This is the canonical corruption mode: sidecar is stale, file is invalid.
        /// </summary>
        private static void CorruptFile(string path)
        {
            // Overwrite with raw garbage; the .sha256 sidecar still holds the old hash.
            File.WriteAllText(path, "NOT_VALID_JSON_{{{{{{garbage}}}}}}");
        }

        // ── SetUp / TearDown ───────────────────────────────────────────────────

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Recovery path logs warnings (primary corrupted, backup attempted).
            // Suppress all failing log messages so they don't fail tests.
            LogAssert.ignoreFailingMessages = true;
        }

        [SetUp]
        public void SetUp()
        {
            if (SaveService.HasInstance)
                UnityEngine.Object.DestroyImmediate(SaveService.Instance.gameObject);

            _saveServiceGo = new GameObject("TestSaveService_Recovery");
            _saveService   = _saveServiceGo.AddComponent<SaveService>();
            ForceSingletonInit(_saveService);
            _saveService.BeginNewRun();
        }

        [TearDown]
        public void TearDown()
        {
            // Unsubscribe any registered handler to prevent cross-test leaks.
            // OnSaveRecovered is a static event, so stale subscriptions survive
            // between test instances.
            if (_recoveredHandler != null)
            {
                SaveService.OnSaveRecovered -= _recoveredHandler;
                _recoveredHandler = null;
            }

            if (_saveServiceGo != null)
                UnityEngine.Object.DestroyImmediate(_saveServiceGo);

            GameEvents.Clear();

            foreach (string dir in _tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, recursive: true);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[SaveServiceRecoveryTests] TearDown could not delete '{dir}': {ex.Message}");
                }
            }
            _tempDirs.Clear();
        }

        // ======================================================================
        // A) Healthy primary — returns primary path, not recovered
        // ======================================================================

        [Test]
        public void TryLoadWithRecoveryDetailed_PrimaryHealthy_ReturnsPrimary()
        {
            // Arrange
            string runDir      = NewTempDir();
            string primaryPath = WriteValidAutosave(runDir, "primary_healthy");

            // Act
            SaveLoadResult result = SaveFileManager.TryLoadWithRecoveryDetailed(primaryPath);

            // Assert
            Assert.IsTrue(result.IsSuccess,
                "Should succeed when primary file is valid.");
            Assert.IsFalse(result.RecoveredFromBackup,
                "RecoveredFromBackup must be false when primary loaded successfully.");
            Assert.AreEqual(-1, result.BackupSlotIndex,
                "BackupSlotIndex must be -1 when primary was used.");
            Assert.AreEqual(primaryPath, result.SourcePath,
                "SourcePath must equal the primary file path.");
            Assert.AreEqual("primary_healthy", result.Data.player.playerClass,
                "Data must match what was written to the primary file.");
        }

        // ======================================================================
        // B) Corrupted primary — falls back to backup 1
        // ======================================================================

        [Test]
        public void TryLoadWithRecoveryDetailed_PrimaryCorrupted_FallsBackToBackup1()
        {
            // Arrange
            string runDir      = NewTempDir();
            string backupsDir  = Path.Combine(runDir, ".backups");
            string primaryPath = WriteValidAutosave(runDir, "should_not_see");

            // Corrupt primary AFTER checksum was written
            CorruptFile(primaryPath);

            // Write a valid backup slot 1
            WriteValidBackup(backupsDir, slot: 1, tag: "from_backup_1");

            // Act
            SaveLoadResult result = SaveFileManager.TryLoadWithRecoveryDetailed(primaryPath);

            // Assert
            Assert.IsTrue(result.IsSuccess,
                "Should succeed when backup 1 is valid and primary is corrupted.");
            Assert.IsTrue(result.RecoveredFromBackup,
                "RecoveredFromBackup must be true when backup was used.");
            Assert.AreEqual(1, result.BackupSlotIndex,
                "BackupSlotIndex must be 1 when autosave_1.json was used.");
            Assert.IsTrue(result.SourcePath.EndsWith("autosave_1.json", StringComparison.OrdinalIgnoreCase),
                $"SourcePath must end with 'autosave_1.json'. Actual: {result.SourcePath}");
            Assert.AreEqual("from_backup_1", result.Data.player.playerClass,
                "Data must come from backup slot 1.");
        }

        // ======================================================================
        // C) Primary + backup 1 corrupted — falls back to backup 2
        // ======================================================================

        [Test]
        public void TryLoadWithRecoveryDetailed_PrimaryAndFirstBackupCorrupted_FallsBackToBackup2()
        {
            // Arrange
            string runDir      = NewTempDir();
            string backupsDir  = Path.Combine(runDir, ".backups");
            string primaryPath = WriteValidAutosave(runDir, "primary_corrupted");
            CorruptFile(primaryPath);

            string backup1Path = WriteValidBackup(backupsDir, slot: 1, tag: "backup1_corrupted");
            CorruptFile(backup1Path);

            WriteValidBackup(backupsDir, slot: 2, tag: "from_backup_2");

            // Act
            SaveLoadResult result = SaveFileManager.TryLoadWithRecoveryDetailed(primaryPath);

            // Assert
            Assert.IsTrue(result.IsSuccess,
                "Should succeed when backup 2 is valid.");
            Assert.IsTrue(result.RecoveredFromBackup,
                "RecoveredFromBackup must be true.");
            Assert.AreEqual(2, result.BackupSlotIndex,
                "BackupSlotIndex must be 2 when autosave_2.json was used.");
            Assert.AreEqual("from_backup_2", result.Data.player.playerClass,
                "Data must come from backup slot 2.");
        }

        // ======================================================================
        // D) All corrupted — returns Empty
        // ======================================================================

        [Test]
        public void TryLoadWithRecoveryDetailed_AllCorrupted_ReturnsEmpty()
        {
            // Arrange — corrupt primary + all MAX_BACKUPS slots
            string runDir      = NewTempDir();
            string backupsDir  = Path.Combine(runDir, ".backups");
            string primaryPath = WriteValidAutosave(runDir, "all_corrupt_primary");
            CorruptFile(primaryPath);

            for (int i = 1; i <= SaveFileManager.MAX_BACKUPS; i++)
            {
                string bp = WriteValidBackup(backupsDir, slot: i, tag: $"corrupt_{i}");
                CorruptFile(bp);
                // Also corrupt the checksum sidecar to be sure (overwrite with wrong hash)
                string sidecar = bp.Replace(".json", ".sha256");
                if (File.Exists(sidecar)) File.WriteAllText(sidecar, "00000000000000000000000000000000");
            }

            // Act
            SaveLoadResult result = SaveFileManager.TryLoadWithRecoveryDetailed(primaryPath);

            // Assert
            Assert.IsFalse(result.IsSuccess,
                "IsSuccess must be false when every candidate is corrupted.");
            Assert.IsNull(result.Data,
                "Data must be null (Empty result) when all files are corrupted.");
        }

        // ======================================================================
        // E) No .backups/ directory — returns Empty
        // ======================================================================

        [Test]
        public void TryLoadWithRecoveryDetailed_NoBackupsDir_ReturnsEmpty()
        {
            // Arrange — corrupted primary, no .backups/ directory at all
            string runDir      = NewTempDir();
            string primaryPath = WriteValidAutosave(runDir, "no_backups_primary");
            CorruptFile(primaryPath);

            // Verify .backups was never created
            string backupsDir = Path.Combine(runDir, ".backups");
            Assert.IsFalse(Directory.Exists(backupsDir),
                "Precondition: .backups directory must not exist for this test.");

            // Act
            SaveLoadResult result = SaveFileManager.TryLoadWithRecoveryDetailed(primaryPath);

            // Assert
            Assert.IsFalse(result.IsSuccess,
                "IsSuccess must be false when primary is corrupted and no .backups/ exists.");
            Assert.IsNull(result.Data,
                "Data must be null (Empty result).");
        }

        // ======================================================================
        // F) OnSaveRecovered fires exactly once when primary is corrupted
        // ======================================================================

        [Test]
        public void SaveService_OnSaveRecovered_FiresWhenPrimaryCorrupted()
        {
            // Arrange: build the save under the run-id path that SaveService.Load
            // will naturally read (GetAutosavePath uses persistentDataPath/Saves/<runId>/).
            // We use a unique runId per test so no state bleeds across tests.
            string runId      = "recovtest_F_" + Guid.NewGuid().ToString("N");
            string saveDir    = Path.Combine(Application.persistentDataPath, "Saves");
            string runDir     = Path.Combine(saveDir, runId);
            string backupsDir = Path.Combine(runDir, ".backups");
            string autosave   = Path.Combine(runDir, "autosave.json");
            _tempDirs.Add(runDir); // cleaned up in TearDown

            // Write valid primary, then corrupt it
            WriteValidAutosave(runDir, "primary_f");
            CorruptFile(autosave);

            // Write a valid backup
            WriteValidBackup(backupsDir, slot: 1, tag: "backup_f");

            // Subscribe to OnSaveRecovered
            int    fireCount    = 0;
            bool   recoveredFlag = false;
            int    slotSeen      = -99;

            _recoveredHandler = r =>
            {
                fireCount++;
                recoveredFlag = r.RecoveredFromBackup;
                slotSeen      = r.BackupSlotIndex;
            };
            SaveService.OnSaveRecovered += _recoveredHandler;

            // Act: SaveService.Load resolves path via GetAutosavePath(runId)
            // We pass the exact path it would use.
            string primaryPath = SaveFileManager.GetAutosavePath(runId);
            bool   loadResult  = _saveService.Load(primaryPath);

            // Assert
            Assert.IsTrue(loadResult,
                "Load must return true when backup recovery succeeds.");
            Assert.AreEqual(1, fireCount,
                "OnSaveRecovered must fire exactly once.");
            Assert.IsTrue(recoveredFlag,
                "RecoveredFromBackup must be true in the fired event.");
            Assert.AreEqual(1, slotSeen,
                "BackupSlotIndex in the event must be 1.");
        }

        // ======================================================================
        // G) OnSaveRecovered does NOT fire on a healthy primary
        // ======================================================================

        [Test]
        public void SaveService_OnSaveRecovered_DoesNotFireOnHealthyLoad()
        {
            // Arrange: healthy primary, no corruption
            string runId   = "recovtest_G_" + Guid.NewGuid().ToString("N");
            string saveDir = Path.Combine(Application.persistentDataPath, "Saves");
            string runDir  = Path.Combine(saveDir, runId);
            _tempDirs.Add(runDir);

            WriteValidAutosave(runDir, "primary_g");

            bool fired = false;
            _recoveredHandler = _ => { fired = true; };
            SaveService.OnSaveRecovered += _recoveredHandler;

            // Act
            string primaryPath = SaveFileManager.GetAutosavePath(runId);
            bool   loadResult  = _saveService.Load(primaryPath);

            // Assert
            Assert.IsTrue(loadResult,
                "Load must return true for a healthy primary.");
            Assert.IsFalse(fired,
                "OnSaveRecovered must NOT fire when the primary load succeeds.");
        }

        // ======================================================================
        // H) Faulty subscriber does not break Load
        // ======================================================================

        [Test]
        public void SaveService_OnSaveRecovered_FaultySubscriberDoesNotBreakLoad()
        {
            // Arrange: corrupted primary + valid backup (same fixture as F)
            string runId      = "recovtest_H_" + Guid.NewGuid().ToString("N");
            string saveDir    = Path.Combine(Application.persistentDataPath, "Saves");
            string runDir     = Path.Combine(saveDir, runId);
            string backupsDir = Path.Combine(runDir, ".backups");
            string autosave   = Path.Combine(runDir, "autosave.json");
            _tempDirs.Add(runDir);

            WriteValidAutosave(runDir, "primary_h");
            CorruptFile(autosave);
            WriteValidBackup(backupsDir, slot: 1, tag: "backup_h");

            // Subscribe a handler that throws to verify the try/catch in SaveService.Load
            _recoveredHandler = _ => throw new InvalidOperationException("Simulated faulty subscriber");
            SaveService.OnSaveRecovered += _recoveredHandler;

            // SaveService.Load logs a Debug.LogError when a subscriber throws.
            // That Debug.LogError is the *evidence* that the try/catch worked,
            // so we explicitly expect it instead of treating it as a failure.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("OnSaveRecovered subscriber threw"));

            // Act: Load must not propagate the subscriber exception
            string primaryPath = SaveFileManager.GetAutosavePath(runId);
            bool   loadResult  = false;
            Assert.DoesNotThrow(
                () => loadResult = _saveService.Load(primaryPath),
                "SaveService.Load must not throw even when OnSaveRecovered subscriber throws.");

            // The load itself succeeded (the backup was valid)
            Assert.IsTrue(loadResult,
                "Load must return true when the backup is valid, even with a faulty subscriber.");
        }
    }
}
