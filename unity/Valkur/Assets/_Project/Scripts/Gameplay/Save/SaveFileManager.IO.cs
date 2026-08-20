using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    public static partial class SaveFileManager
    {
        // ── Write / read core ────────────────────────────────────────────────

        /// <summary>Write save data to disk with atomic rename and checksum sidecar.</summary>
        public static void WriteSaveFile(string path, GameSaveData data, string schemaVersion)
        {
            data.schemaVersion = schemaVersion;
            string json = JsonUtility.ToJson(data, true);
            WriteSerializedJsonAtomic(path, json);
        }

        /// <summary>
        /// Async variant of <see cref="WriteSaveFile"/>. Serializes to JSON
        /// on the calling (main) thread — Unity's <c>JsonUtility</c> is not
        /// thread-safe — and pushes the actual disk I/O (atomic temp+rename
        /// + checksum sidecar) onto the .NET thread pool. Caller awaits the
        /// returned <see cref="Task"/> when ordering / durability matters
        /// (OnApplicationQuit, Load, …).
        /// </summary>
        public static Task WriteSaveFileAsync(string path, GameSaveData data, string schemaVersion)
        {
            data.schemaVersion = schemaVersion;
            string json = JsonUtility.ToJson(data, true);
            return Task.Run(() => WriteSerializedJsonAtomic(path, json));
        }

        /// <summary>
        /// Async write that ALSO rotates the per-run backup chain before
        /// writing the new autosave, all inside a single background task so
        /// the rotation and the new write cannot interleave with each other.
        /// Returns a <see cref="Task"/> that completes when the new file has
        /// reached the disk and its checksum sidecar is in place.
        ///
        /// Important: <see cref="UnityEngine.Application.persistentDataPath"/>
        /// is documented as MAIN-THREAD-ONLY, so every path that depends on
        /// it (autosave path + backups directory) is resolved here before
        /// the task starts. The continuation only sees plain strings.
        /// </summary>
        public static Task WriteAutosaveAsync(string runId, GameSaveData data, string schemaVersion)
        {
            data.schemaVersion = schemaVersion;
            string json       = JsonUtility.ToJson(data, true);
            string target     = GetAutosavePath(runId);
            string backupsDir = GetBackupsDirectory(runId);
            return Task.Run(() =>
            {
                RotateAutosaveBackupsByPath(target, backupsDir);
                WriteSerializedJsonAtomic(target, json);
            });
        }

        /// <summary>
        /// Thread-safe variant of <see cref="RotateAutosaveBackups"/>: takes
        /// the already-resolved autosave path and backups directory so it
        /// never touches Unity APIs (which are not safe off the main thread).
        /// </summary>
        internal static void RotateAutosaveBackupsByPath(string srcAutosavePath, string backupsDir)
        {
            if (!File.Exists(srcAutosavePath)) return;
            Directory.CreateDirectory(backupsDir);

            // Shift N-1 → N, N-2 → N-1, …, 1 → 2 (drop the oldest)
            for (int i = MAX_BACKUPS; i >= 2; i--)
            {
                string from = Path.Combine(backupsDir, $"{AUTOSAVE_NAME}_{i - 1}" + SAVE_EXTENSION);
                string to   = Path.Combine(backupsDir, $"{AUTOSAVE_NAME}_{i}"     + SAVE_EXTENSION);
                if (!File.Exists(from)) continue;
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }

            // Copy current autosave → autosave_1
            string firstBackup = Path.Combine(backupsDir, $"{AUTOSAVE_NAME}_1" + SAVE_EXTENSION);
            if (File.Exists(firstBackup)) File.Delete(firstBackup);
            File.Copy(srcAutosavePath, firstBackup, overwrite: true);
        }

        // Temp-write + rename + checksum. Pure file IO, safe to call from any thread.
        // The name says "Atomic" and the rename is not — see SwapIntoPlace for what this
        // does and does not guarantee.
        internal static void WriteSerializedJsonAtomic(string path, string json)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Unique per write. The temp used to be "<path>.tmp" for EVERY writer of the
            // same file, so two writes in flight at once opened the same temp handle and
            // the loser died with "Access to the path is denied" — which is how this was
            // found, one error on leaving Play. SaveService chains its autosaves through
            // _pendingWrite, but WriteAutosaveAsync's Task.Run does not join that chain,
            // so two writers really can overlap.
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                File.WriteAllText(tempPath, json);
                SwapIntoPlace(tempPath, path);
            }
            catch
            {
                // Never leave a half-written temp behind to accumulate in the save folder.
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
                throw;
            }

            WriteChecksum(path, json);
        }

        /// <summary>
        /// Rename a finished temp file onto its target.
        ///
        /// What this DOES buy: a reader never sees a half-written save, because the bytes
        /// land in the temp and only a rename is visible at the target.
        ///
        /// What it does NOT buy, despite the method's name: a gap-free swap. Measured in
        /// this runtime over 200 rewrites with a reader spinning on File.Exists,
        /// File.Replace left the target momentarily absent 3715 times and delete-then-move
        /// 4327 — Mono's File.Replace is not Win32 ReplaceFile. Replace is kept because it
        /// is the narrower window of the two, but the thing that actually makes a run
        /// survive a crash in that window is the rotating backups and the checksum, not
        /// this rename. Do not add a claim here that this is atomic.
        ///
        /// Retried because two writers race: SaveService serialises its own autosaves, but
        /// WriteAutosaveAsync does not join that chain, so the destination can appear or
        /// vanish between the existence check and the swap.
        /// </summary>
        private static void SwapIntoPlace(string tempPath, string path)
        {
            const int MAX_ATTEMPTS = 8;

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(tempPath, path, null);
                    else File.Move(tempPath, path);
                    return;
                }
                catch (IOException) when (attempt < MAX_ATTEMPTS)
                {
                    // The other writer got there first, either way round: Move found the
                    // name taken, or Replace found it gone. Both are worth another look
                    // rather than failing a save over a race we can just retry out of.
                    // UnauthorizedAccessException is deliberately NOT caught — that one is
                    // a real permissions problem and must surface.
                    System.Threading.Thread.Sleep(2);
                }
            }
        }

        public static GameSaveData TryLoadSingle(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                if (!ValidateChecksum(path, json))
                {
                    Debug.LogWarning($"[SaveFileManager] Checksum mismatch for: {path}");
                    return null;
                }

                var data = JsonUtility.FromJson<GameSaveData>(json);
                if (data == null || data.player == null)
                {
                    Debug.LogWarning($"[SaveFileManager] Invalid save structure in: {path}");
                    return null;
                }

                return data;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveFileManager] Failed to read {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Try the requested file, then the per-run <c>.backups/</c> folder
        /// (<c>autosave_1..N.json</c>) if the requested file was an autosave.
        /// </summary>
        public static GameSaveData TryLoadWithRecovery(string path)
            => TryLoadWithRecoveryDetailed(path).Data;

        /// <summary>
        /// Detailed variant of <see cref="TryLoadWithRecovery"/> that reports
        /// whether the primary file was used or a backup slot kicked in. The
        /// boot pipeline / load UI uses this to surface a user-visible toast
        /// when the player's main save was corrupted.
        /// </summary>
        public static SaveLoadResult TryLoadWithRecoveryDetailed(string path)
        {
            var primary = TryLoadSingle(path);
            if (primary != null)
                return new SaveLoadResult(primary, recoveredFromBackup: false, -1, path);

            Debug.LogWarning($"[SaveFileManager] Primary save corrupted or missing: {path}. Trying backups…");

            string runDir     = Path.GetDirectoryName(path) ?? "";
            string backupsDir = Path.Combine(runDir, BACKUPS_SUBDIR);
            if (Directory.Exists(backupsDir))
            {
                for (int i = 1; i <= MAX_BACKUPS; i++)
                {
                    string bp = Path.Combine(backupsDir, $"{AUTOSAVE_NAME}_{i}" + SAVE_EXTENSION);
                    var data = TryLoadSingle(bp);
                    if (data != null)
                    {
                        Debug.Log($"[SaveFileManager] Recovered from backup: {bp}");
                        return new SaveLoadResult(data, recoveredFromBackup: true, i, bp);
                    }
                }
            }

            return SaveLoadResult.Empty;
        }

        // ── Backup rotation ──────────────────────────────────────────────────

        /// <summary>
        /// Rotates the per-run autosave history.  The current
        /// <c>autosave.json</c> is copied to <c>.backups/autosave_1.json</c>
        /// (after shifting the existing slots).  Call BEFORE writing a fresh
        /// <c>autosave.json</c>.
        /// </summary>
        public static void RotateAutosaveBackups(string runId)
        {
            string srcAutosave = GetAutosavePath(runId);
            if (!File.Exists(srcAutosave)) return;

            string backupsDir = GetBackupsDirectory(runId);
            Directory.CreateDirectory(backupsDir);

            // Shift N-1 → N, N-2 → N-1, …, 1 → 2 (drop the oldest)
            for (int i = MAX_BACKUPS; i >= 2; i--)
            {
                string from = Path.Combine(backupsDir, $"{AUTOSAVE_NAME}_{i - 1}" + SAVE_EXTENSION);
                string to   = Path.Combine(backupsDir, $"{AUTOSAVE_NAME}_{i}"     + SAVE_EXTENSION);
                if (!File.Exists(from)) continue;
                if (i == MAX_BACKUPS && File.Exists(to)) File.Delete(to);
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }

            // Copy current autosave → autosave_1
            string firstBackup = Path.Combine(backupsDir, $"{AUTOSAVE_NAME}_1" + SAVE_EXTENSION);
            if (File.Exists(firstBackup)) File.Delete(firstBackup);
            File.Copy(srcAutosave, firstBackup, overwrite: true);
        }

        // ── Delete / rename ──────────────────────────────────────────────────

        /// <summary>
        /// Delete a save file (and its checksum sidecar) from disk.
        /// If the deletion empties a run folder, the folder (and its
        /// hidden <c>.backups/</c>) is pruned as well.
        /// </summary>
        public static bool DeleteSave(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;

                File.Delete(path);
                string checksumPath = path.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
                if (File.Exists(checksumPath)) File.Delete(checksumPath);
                Debug.Log($"[SaveFileManager] Deleted save: {path}");

                // Prune empty run folder (do NOT touch the top-level Saves/, .recovery/, etc.)
                try
                {
                    string runDir = Path.GetDirectoryName(path);
                    if (IsPrunableRunDirectory(runDir))
                    {
                        bool hasVisibleSaves = Directory.GetFiles(runDir, "*" + SAVE_EXTENSION,
                                                                   SearchOption.TopDirectoryOnly).Length > 0;
                        if (!hasVisibleSaves)
                        {
                            // Drop the .backups subfolder along with the run folder.
                            Directory.Delete(runDir, recursive: true);
                            Debug.Log($"[SaveFileManager] Pruned empty run folder: {runDir}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SaveFileManager] Could not prune empty run folder: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveFileManager] Delete failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Rename a save slot.  The new name MUST NOT be a reserved name
        /// (e.g. "autosave") — those are returned as null.  Renames both the
        /// .json and the .sha256 sidecar.  Stays inside the same run folder.
        /// </summary>
        public static string RenameSave(string currentPath, string newName)
        {
            try
            {
                if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
                {
                    Debug.LogWarning($"[SaveFileManager] Rename: source missing: {currentPath}");
                    return null;
                }

                string sanitized = SanitizeSaveName(newName);
                if (sanitized == null)
                {
                    Debug.LogWarning("[SaveFileManager] Rename: invalid name.");
                    return null;
                }

                if (IsReservedSaveName(sanitized))
                {
                    Debug.LogWarning($"[SaveFileManager] Rename: '{sanitized}' is a reserved save name.");
                    return null;
                }

                string dir = Path.GetDirectoryName(currentPath);
                string newPath = Path.Combine(dir, sanitized + SAVE_EXTENSION);

                if (string.Equals(newPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    return currentPath;

                if (File.Exists(newPath))
                {
                    Debug.LogWarning($"[SaveFileManager] Rename: target already exists: {newPath}");
                    return null;
                }

                File.Move(currentPath, newPath);

                string oldChecksum = currentPath.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
                string newChecksum = newPath.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
                if (File.Exists(oldChecksum))
                {
                    if (File.Exists(newChecksum)) File.Delete(newChecksum);
                    File.Move(oldChecksum, newChecksum);
                }

                Debug.Log($"[SaveFileManager] Renamed save: {Path.GetFileName(currentPath)} → {Path.GetFileName(newPath)}");
                return newPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveFileManager] Rename failed: {ex.Message}");
                return null;
            }
        }

        // ── Checksum helpers ─────────────────────────────────────────────────

        private static void WriteChecksum(string savePath, string json)
        {
            try
            {
                string hash = ComputeSha256(json);
                string checksumPath = savePath.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
                File.WriteAllText(checksumPath, hash);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveFileManager] Failed to write checksum: {ex.Message}");
            }
        }

        private static bool ValidateChecksum(string savePath, string json)
        {
            string checksumPath = savePath.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
            if (!File.Exists(checksumPath)) return true; // legacy — accept

            try
            {
                string storedHash   = File.ReadAllText(checksumPath).Trim();
                string computedHash = ComputeSha256(json);
                return string.Equals(storedHash, computedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        private static string ComputeSha256(string input)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(64);
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
