using System;
using System.Threading.Tasks;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Save;

namespace Valkur.Gameplay
{
    public partial class SaveService
    {
        private Task _pendingWrite = Task.CompletedTask;
        private readonly object _pendingWriteLock = new object();

        // Chains a new autosave task off whatever previous write may still
        // be pending so that disk writes are strictly ordered (no thread
        // ever races to write/rotate the same files). Faulted tasks are
        // logged but never rethrown — the next save attempt is independent.
        // Records a SaveTelemetry entry for the HUD / diagnostics panel
        // when the task completes.
        private void EnqueueAsyncAutosave(string runId, GameSaveData data,
            SaveTelemetryEntry.SaveKind telemetryKind, string telemetryReason,
            System.Diagnostics.Stopwatch stopwatch)
        {
            // Resolve every Unity-API-bound value here on the main thread.
            // JsonUtility.ToJson and Application.persistentDataPath (used
            // transitively by GetAutosavePath / GetBackupsDirectory) are
            // documented main-thread-only; the previous implementation
            // invoked WriteAutosaveAsync inside a ContinueWith on the
            // thread pool, which raised "get_persistentDataPath can only
            // be called from the main thread" once per autosave tick.
            data.schemaVersion = SaveSchemaMigrator.CURRENT_SCHEMA;
            string json       = JsonUtility.ToJson(data, true);
            string targetPath = SaveFileManager.GetAutosavePath(runId);
            string backupsDir = SaveFileManager.GetBackupsDirectory(runId);
            string reason     = telemetryReason ?? telemetryKind.ToString();

            Task previous;
            lock (_pendingWriteLock) { previous = _pendingWrite; }

            // Chain the rotate+write off whatever previous write may still
            // be pending so disk operations are strictly ordered (no thread
            // races to write/rotate the same files). The continuation only
            // touches plain strings and System.IO — never a Unity API.
            Task next = previous.ContinueWith(prev =>
            {
                if (prev.IsFaulted)
                    Debug.LogError($"[SaveService] Previous async write faulted: " +
                                   $"{prev.Exception?.GetBaseException().Message}");
                SaveFileManager.RotateAutosaveBackupsByPath(targetPath, backupsDir);
                SaveFileManager.WriteSerializedJsonAtomic(targetPath, json);
            }, TaskScheduler.Default);

            // Attach final telemetry + error logger. ContinueWith runs on a
            // thread-pool thread so the SaveTelemetry buffer must be thread-
            // safe (it is — internal lock).
            next.ContinueWith(t =>
            {
                stopwatch.Stop();
                bool success = !t.IsFaulted;
                if (!success)
                    Debug.LogError($"[SaveService] Async autosave failed: " +
                                   $"{t.Exception?.GetBaseException().Message}");
                SaveTelemetry.Record(new SaveTelemetryEntry(
                    telemetryKind,
                    reason,
                    success: success,
                    sizeBytes: success ? SafeFileSize(targetPath) : 0,
                    durationMs: stopwatch.Elapsed.TotalMilliseconds,
                    path: targetPath,
                    wasAsync: true));
            }, TaskScheduler.Default);

            lock (_pendingWriteLock) { _pendingWrite = next; }
        }

        /// <summary>
        /// Block until any pending async autosave has flushed to disk.
        /// Used by Load (we must not read while a stale write is mid-flight),
        /// OnApplicationQuit, and tests. Waits at most
        /// <paramref name="timeoutSeconds"/> seconds (-1 = unbounded).
        /// </summary>
        public bool FlushPendingWrites(float timeoutSeconds = 5f)
        {
            Task pending;
            lock (_pendingWriteLock) { pending = _pendingWrite; }
            if (pending.IsCompleted) return true;
            try
            {
                if (timeoutSeconds < 0f) { pending.Wait(); return true; }
                return pending.Wait(System.TimeSpan.FromSeconds(timeoutSeconds));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveService] FlushPendingWrites errored: {ex.Message}");
                return false;
            }
        }

        private static long SafeFileSize(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return 0;
                return new System.IO.FileInfo(path).Length;
            }
            catch { return 0; }
        }
    }
}
