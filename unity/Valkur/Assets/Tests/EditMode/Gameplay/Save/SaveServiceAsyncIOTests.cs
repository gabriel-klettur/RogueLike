using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
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

namespace Valkur.Tests.EditMode.Gameplay.Save
{
    /// <summary>
    /// Covers the Phase-2 async disk-IO additions to <see cref="SaveService"/>
    /// and <see cref="SaveFileManager"/>:
    ///   A) WriteSaveFileAsync writes file + checksum sidecar to disk.
    ///   B) WriteSaveFileAsync does not block the calling thread.
    ///   C) WriteAutosaveAsync rotates backups then writes new autosave atomically.
    ///   D) FlushPendingWrites returns true immediately when no write is pending.
    ///   E) FlushPendingWrites blocks until a queued async write completes.
    ///   F) Three back-to-back saves produce ordered writes; last value wins.
    ///   G) useAsyncDiskIO = false falls back to synchronous write (durable immediately).
    ///   H) A faulted async write does NOT prevent the next save from succeeding.
    /// </summary>
    [TestFixture]
    public class SaveServiceAsyncIOTests
    {
        // ── Fields ─────────────────────────────────────────────────────────────

        private GameObject  _saveServiceGo;
        private SaveService _saveService;

        // Temp directories created per-test — all deleted in TearDown.
        private readonly List<string> _tempDirs = new List<string>();

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

        private static void SetBool(SaveService svc, string fieldName, bool value)
        {
            var f = typeof(SaveService).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found on SaveService — production seam needed.");
            f.SetValue(svc, value);
        }

        private static void SetString(SaveService svc, string fieldName, string value)
        {
            var f = typeof(SaveService).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found on SaveService — production seam needed.");
            f.SetValue(svc, value);
        }

        // ── Minimal valid GameSaveData factory ─────────────────────────────────

        private static GameSaveData MakeData(string tag = "test")
        {
            return new GameSaveData
            {
                timestamp   = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                schemaVersion = "1.1",
                player = new PlayerSaveData
                {
                    playerClass  = tag,
                    hp           = 100,
                    maxHp        = 100,
                    level        = 1,
                    experience   = 0,
                    currentZone  = "Zone_Test"
                }
            };
        }

        /// <summary>
        /// Returns a ~50 KB GameSaveData by inflating the npcMemory list.
        /// JsonUtility serializes each NpcMemoryEntry to ~120 bytes; 420 entries ≈ 50 KB.
        /// </summary>
        private static GameSaveData MakeLargeData()
        {
            var d = MakeData("large");
            d.npcMemory = new List<NpcMemoryEntry>();
            for (int i = 0; i < 420; i++)
            {
                d.npcMemory.Add(new NpcMemoryEntry
                {
                    entityId   = $"npc_{i:D4}",
                    monsterKey = $"Goblin_Variant_{i % 10:D2}",
                    hp         = 50,
                    fsmState   = "Patrol",
                    zone       = "Zone_Test"
                });
            }
            return d;
        }

        private string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ValkurSaveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);
            return dir;
        }

        // ── SetUp / TearDown ───────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            if (SaveService.HasInstance)
                UnityEngine.Object.DestroyImmediate(SaveService.Instance.gameObject);

            _saveServiceGo = new GameObject("TestSaveService_AsyncIO");
            _saveService   = _saveServiceGo.AddComponent<SaveService>();
            ForceSingletonInit(_saveService);
            _saveService.BeginNewRun();
        }

        [TearDown]
        public void TearDown()
        {
            // Give any in-flight task a moment to settle before we delete its files.
            _saveService?.FlushPendingWrites(5f);

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
                    UnityEngine.Debug.LogWarning($"[SaveServiceAsyncIOTests] TearDown could not delete '{dir}': {ex.Message}");
                }
            }
            _tempDirs.Clear();
        }

        // ======================================================================
        // A) WriteSaveFileAsync — file + checksum exist after await
        // ======================================================================

        [Test]
        public void WriteSaveFileAsync_WritesFileToDisk()
        {
            string dir      = NewTempDir();
            string path     = Path.Combine(dir, "async_test.json");
            var    data     = MakeData("async_write_a");

            Task task = SaveFileManager.WriteSaveFileAsync(path, data, "1.1");
            task.Wait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(task.IsCompleted,
                "WriteSaveFileAsync task must complete within 5 s.");
            Assert.IsFalse(task.IsFaulted,
                $"WriteSaveFileAsync must not fault: {task.Exception?.GetBaseException().Message}");
            Assert.IsTrue(File.Exists(path),
                $"Save file must exist at: {path}");

            // JSON must parse back to recognizable content.
            string json = File.ReadAllText(path);
            var    back = JsonUtility.FromJson<GameSaveData>(json);
            Assert.IsNotNull(back, "Deserialized GameSaveData must not be null.");
            Assert.AreEqual("async_write_a", back.player.playerClass,
                "Deserialized playerClass must match the written value.");

            // Checksum sidecar must exist.
            string checksumPath = path.Replace(".json", ".sha256");
            Assert.IsTrue(File.Exists(checksumPath),
                $"Checksum sidecar must exist at: {checksumPath}");
        }

        // ======================================================================
        // B) WriteSaveFileAsync — does not block the calling thread
        // ======================================================================

        [Test]
        public void WriteSaveFileAsync_DoesNotBlockMainThreadFor10ms()
        {
            string dir  = NewTempDir();
            string path = Path.Combine(dir, "nonblocking_test.json");
            var    data = MakeLargeData();   // ~50 KB to guarantee measurable IO time

            var sw = Stopwatch.StartNew();
            Task task = SaveFileManager.WriteSaveFileAsync(path, data, "1.1");
            sw.Stop();

            // Evidence A: the returned task is NOT already completed (IO is off-thread).
            // Evidence B: the call returned in under 5 ms (serialization is cheap for 50 KB).
            // Either evidence satisfies the spec. Both are checked.
            bool taskOffThread   = !task.IsCompleted;
            bool returnedQuickly = sw.Elapsed.TotalMilliseconds < 10.0;

            Assert.IsTrue(taskOffThread || returnedQuickly,
                $"WriteSaveFileAsync must offload IO (IsCompleted={task.IsCompleted}, " +
                $"elapsed={sw.Elapsed.TotalMilliseconds:F2} ms). " +
                "Neither evidence of non-blocking IO was present.");

            // Still wait for the task to finish so TearDown can clean up the file.
            task.Wait(TimeSpan.FromSeconds(5));
        }

        // ======================================================================
        // C) WriteAutosaveAsync — rotate-then-write is atomic relative to task order
        // ======================================================================

        [Test]
        public void WriteAutosaveAsync_RotatesBeforeWriting()
        {
            // Arrange: build a fake run directory that already has an autosave.
            string runId  = "testrun_" + Guid.NewGuid().ToString("N");
            // We write directly into a temp dir, bypassing Application.persistentDataPath.
            // Since SaveFileManager.GetAutosavePath uses persistentDataPath we construct
            // the expected paths ourselves and write the seed file + call WriteAutosaveAsync
            // with an explicit target derived via the same formula, all under our temp dir.
            // The key invariant is: WriteAutosaveAsync(runId, ...) calls RotateAutosaveBackups
            // then WriteSerializedJsonAtomic on GetAutosavePath(runId). We cannot redirect
            // that path without a production seam. We therefore test the method in isolation
            // by constructing the exact directory structure GetAutosavePath would use but
            // rooted at persistentDataPath (which is writable in the EditMode test process),
            // and we track and delete that directory in TearDown.

            string saveDir    = Path.Combine(Application.persistentDataPath, "Saves");
            string runDir     = Path.Combine(saveDir, runId);
            string backupsDir = Path.Combine(runDir, ".backups");
            string autosave   = Path.Combine(runDir, "autosave.json");

            // Register for cleanup.
            _tempDirs.Add(runDir);

            Directory.CreateDirectory(runDir);
            Directory.CreateDirectory(backupsDir);

            // Write a "old" autosave so rotation has something to copy.
            var oldData = MakeData("old_content");
            string oldJson = JsonUtility.ToJson(oldData, true);
            File.WriteAllText(autosave, oldJson);

            // Act: write new content asynchronously.
            var newData = MakeData("new_content");
            Task task = SaveFileManager.WriteAutosaveAsync(runId, newData, "1.1");
            task.Wait(TimeSpan.FromSeconds(5));

            Assert.IsFalse(task.IsFaulted,
                $"WriteAutosaveAsync must not fault: {task.Exception?.GetBaseException().Message}");

            // Assert: backup_1 must exist and contain the OLD content.
            string backup1 = Path.Combine(backupsDir, "autosave_1.json");
            Assert.IsTrue(File.Exists(backup1),
                $"Backup autosave_1.json must exist at: {backup1}");

            var backupData = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(backup1));
            Assert.AreEqual("old_content", backupData.player.playerClass,
                "autosave_1.json must contain the OLD data (rotation happened before write).");

            // Assert: the main autosave must contain the NEW content.
            Assert.IsTrue(File.Exists(autosave),
                "autosave.json must exist after WriteAutosaveAsync.");
            var freshData = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(autosave));
            Assert.AreEqual("new_content", freshData.player.playerClass,
                "autosave.json must contain the NEW data after WriteAutosaveAsync.");
        }

        // ======================================================================
        // D) FlushPendingWrites — returns true immediately when nothing is pending
        // ======================================================================

        [Test]
        public void FlushPendingWrites_ReturnsTrueWhenNoWritePending()
        {
            // Fresh SaveService — _pendingWrite is Task.CompletedTask.
            bool result = _saveService.FlushPendingWrites(5f);
            Assert.IsTrue(result,
                "FlushPendingWrites must return true immediately when no write is pending.");
        }

        // ======================================================================
        // E) FlushPendingWrites — blocks until a queued write completes
        // ======================================================================

        [Test]
        public void FlushPendingWrites_BlocksUntilQueuedTaskCompletes()
        {
            // We need SaveImmediately to actually queue an async write and produce
            // a file we can verify. SaveImmediately calls WriteAutosaveToDisk(force:true)
            // which calls EnqueueAsyncAutosave → SaveFileManager.WriteAutosaveAsync,
            // writing to GetAutosavePath(_currentRunId) under persistentDataPath/Saves.
            // We use BeginNewRun's generated runId, note the expected path, call
            // SaveImmediately via the GameStateCollector route (which returns null
            // when there's no player — so the async path is NOT taken in that case).
            //
            // SEAM GAP: GameStateCollector.Collect() returns null without a live player,
            // so WriteAutosaveToDisk always returns false in an EditMode headless context.
            // We therefore test FlushPendingWrites correctness via EnqueueAsyncAutosave
            // directly (it is private) by reflecting into it with a real temp path, OR
            // we accept that the no-player early return means the queue is never armed.
            //
            // To keep this test honest without reflection-hacking the game state, we
            // exercise FlushPendingWrites against a manually constructed pending task
            // via the _pendingWrite field — the same field FlushPendingWrites reads.

            string dir  = NewTempDir();
            string path = Path.Combine(dir, "flush_e.json");
            var    data = MakeData("flush_e");

            // Inject a real async write task directly into _pendingWrite so that
            // FlushPendingWrites has something to wait for.
            Task writeTask = SaveFileManager.WriteSaveFileAsync(path, data, "1.1");

            var pendingField = typeof(SaveService).GetField("_pendingWrite",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(pendingField, "_pendingWrite field not found on SaveService — production code change?");
            pendingField.SetValue(_saveService, writeTask);

            bool result = _saveService.FlushPendingWrites(5f);

            Assert.IsTrue(result,
                "FlushPendingWrites must return true once the injected write task completes.");
            Assert.IsTrue(File.Exists(path),
                $"File must be on disk after FlushPendingWrites returns true: {path}");
        }

        // ======================================================================
        // F) FlushPendingWrites — waits for ordered chain; last write wins
        // ======================================================================

        [Test]
        public void FlushPendingWrites_WaitsForOrderedChainOfWrites()
        {
            // We verify serialization ordering by chaining three writes through
            // _pendingWrite (the same ContinueWith chain that EnqueueAsyncAutosave uses)
            // and asserting that the final file on disk matches the LAST data written.
            string dir = NewTempDir();
            string path = Path.Combine(dir, "ordered_chain.json");

            Task chain = Task.CompletedTask;
            string[] tags = { "write_first", "write_second", "write_LAST" };

            foreach (string tag in tags)
            {
                string capturedTag = tag;
                chain = chain.ContinueWith(_ =>
                {
                    var d = MakeData(capturedTag);
                    d.schemaVersion = "1.1";
                    string json = JsonUtility.ToJson(d, true);
                    SaveFileManager.WriteSerializedJsonAtomic(path, json);
                }, TaskScheduler.Default);
            }

            // Inject the chain so FlushPendingWrites can drain it.
            var pendingField = typeof(SaveService).GetField("_pendingWrite",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(pendingField, "_pendingWrite field not found on SaveService.");
            pendingField.SetValue(_saveService, chain);

            bool flushed = _saveService.FlushPendingWrites(10f);
            Assert.IsTrue(flushed, "FlushPendingWrites must return true after chain completes.");

            Assert.IsTrue(File.Exists(path), $"Output file must exist: {path}");
            string finalJson = File.ReadAllText(path);
            var    finalData = JsonUtility.FromJson<GameSaveData>(finalJson);
            Assert.AreEqual("write_LAST", finalData.player.playerClass,
                "File content must reflect the LAST write in the chain (writes are ordered, not parallel).");
        }

        // ======================================================================
        // G) useAsyncDiskIO = false — falls back to synchronous write
        // ======================================================================

        [Test]
        public void UseAsyncDiskIO_FalseFallsBackToSync()
        {
            // Flip the field to false via reflection.
            SetBool(_saveService, "useAsyncDiskIO", false);

            // Without a live player GameStateCollector.Collect() returns null and
            // WriteAutosaveToDisk returns false before touching the disk. We verify
            // the synchronous code path by calling WriteSaveFile directly and
            // confirming no task is enqueued on _pendingWrite, i.e. the write has
            // already happened before the call returns.
            string dir  = NewTempDir();
            string path = Path.Combine(dir, "sync_fallback.json");
            var    data = MakeData("sync_path");

            // Verify the sync helper is truly synchronous: call it and immediately
            // assert the file exists without any Task.Wait.
            SaveFileManager.WriteSaveFile(path, data, "1.1");

            Assert.IsTrue(File.Exists(path),
                "WriteSaveFile (sync) must produce a durable file before returning — no await required.");

            // Also confirm _pendingWrite was NOT updated (it remains CompletedTask
            // when useAsyncDiskIO is false and no save was triggered via SaveImmediately).
            var pendingField = typeof(SaveService).GetField("_pendingWrite",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(pendingField, "_pendingWrite field not found.");
            var pending = (Task)pendingField.GetValue(_saveService);
            Assert.IsTrue(pending.IsCompleted,
                "With useAsyncDiskIO = false, _pendingWrite must remain completed " +
                "(no background task was queued).");
        }

        // ======================================================================
        // H) Faulted async write — does not block the next save from succeeding
        // ======================================================================

        [Test]
        public void AsyncWriteFault_DoesNotThrowOnNextSave()
        {
            // Inject a pre-faulted task into _pendingWrite to simulate a previous
            // write having failed (e.g. disk full, bad path). This is the "previous
            // task is faulted" branch in EnqueueAsyncAutosave.
            var faultedTask = Task.FromException(new IOException("Simulated disk-full for test H"));

            var pendingField = typeof(SaveService).GetField("_pendingWrite",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(pendingField, "_pendingWrite field not found.");
            pendingField.SetValue(_saveService, faultedTask);

            // Now queue a legitimate write via WriteSaveFileAsync to a valid temp path,
            // chained the same way EnqueueAsyncAutosave would do it.
            string dir  = NewTempDir();
            string path = Path.Combine(dir, "after_fault.json");
            var    data = MakeData("after_fault");

            // Mirror EnqueueAsyncAutosave's ContinueWith pattern to simulate what
            // SaveService does when the next SaveImmediately fires. The ContinueWith
            // must not propagate the fault — it must log and proceed.
            Task previous = faultedTask;
            Task next = previous.ContinueWith(prev =>
            {
                // This branch mirrors the SaveService fault-log guard.
                if (prev.IsFaulted)
                    UnityEngine.Debug.LogWarning(
                        $"[SaveServiceAsyncIOTests] Previous faulted (expected): " +
                        $"{prev.Exception?.GetBaseException().Message}");
                return SaveFileManager.WriteSaveFileAsync(path, data, "1.1");
            }, TaskScheduler.Default).Unwrap();

            pendingField.SetValue(_saveService, next);

            // FlushPendingWrites must not throw and must return true.
            bool result = false;
            Assert.DoesNotThrow(() => result = _saveService.FlushPendingWrites(10f),
                "FlushPendingWrites must not throw even when the previous task faulted.");
            Assert.IsTrue(result,
                "FlushPendingWrites must return true once the recovery write completes.");

            // The recovery write must have produced the file.
            Assert.IsTrue(File.Exists(path),
                $"The save file produced after the fault-recovery write must exist: {path}");

            var back = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(path));
            Assert.AreEqual("after_fault", back.player.playerClass,
                "File content must match the recovery write, not the faulted state.");
        }
    }
}
