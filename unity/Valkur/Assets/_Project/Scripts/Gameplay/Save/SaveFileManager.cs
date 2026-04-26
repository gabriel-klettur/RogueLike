using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Handles all file IO for the save system: read, write, checksum,
    /// backup rotation, directory management, and slot listing.
    /// Pure IO — no game state knowledge.
    /// </summary>
    public static class SaveFileManager
    {
        private const string SAVE_DIR = "Saves";
        private const string RECOVERY_SUBDIR = ".recovery";
        private const string SAVE_EXTENSION = ".json";
        private const string CHECKSUM_EXTENSION = ".sha256";
        private const int MAX_BACKUPS = 5;

        // Reserved names that must never appear in the user-visible save list.
        // Defensive filter — even after files are moved to the .recovery subdir,
        // legacy installs may still have these in the top-level Saves folder.
        private static readonly HashSet<string> ReservedSaveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "position_checkpoint",
            "position_checkpoint_bak",
        };

        public static string GetSaveDirectory()
        {
            return Path.Combine(Application.persistentDataPath, SAVE_DIR);
        }

        public static string GetRecoveryDirectory()
        {
            return Path.Combine(GetSaveDirectory(), RECOVERY_SUBDIR);
        }

        public static string GetSavePath(string fileName)
        {
            return Path.Combine(GetSaveDirectory(), fileName + SAVE_EXTENSION);
        }

        public static void EnsureSaveDirectory()
        {
            string dir = GetSaveDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[SaveFileManager] Created save directory: {dir}");
            }
            string recoveryDir = GetRecoveryDirectory();
            if (!Directory.Exists(recoveryDir))
            {
                Directory.CreateDirectory(recoveryDir);
            }
            MigrateLegacyRecoveryFiles();
        }

        /// <summary>
        /// One-shot migration: move pre-refactor recovery files
        /// (Saves/position_checkpoint*.json) into the new hidden subfolder
        /// (Saves/.recovery/). If a destination file already exists, the legacy
        /// file is deleted instead of overwriting (the new one is more recent).
        /// Safe to call on every boot — no-op when nothing to migrate.
        /// </summary>
        private static void MigrateLegacyRecoveryFiles()
        {
            try
            {
                foreach (string reserved in ReservedSaveNames)
                {
                    string legacy = Path.Combine(GetSaveDirectory(), reserved + SAVE_EXTENSION);
                    if (!File.Exists(legacy)) continue;

                    string dest = Path.Combine(GetRecoveryDirectory(), reserved + SAVE_EXTENSION);
                    try
                    {
                        if (File.Exists(dest)) File.Delete(legacy);
                        else                   File.Move(legacy, dest);
                        Debug.Log($"[SaveFileManager] Migrated legacy recovery file: {reserved}{SAVE_EXTENSION}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[SaveFileManager] Could not migrate {legacy}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveFileManager] Recovery migration failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Write save data to disk with atomic rename and checksum sidecar.
        /// </summary>
        public static void WriteSaveFile(string path, GameSaveData data, string schemaVersion)
        {
            data.schemaVersion = schemaVersion;
            string json = JsonUtility.ToJson(data, true);

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);

            WriteChecksum(path, json);
        }

        /// <summary>
        /// Try to load and validate a single save file. Returns null on any failure.
        /// </summary>
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
        /// Try primary path, then numbered backups, then shutdown save.
        /// </summary>
        public static GameSaveData TryLoadWithRecovery(string path)
        {
            var data = TryLoadSingle(path);
            if (data != null) return data;

            Debug.LogWarning($"[SaveFileManager] Primary save corrupted or missing: {path}. Attempting backup recovery...");

            for (int i = 0; i < MAX_BACKUPS; i++)
            {
                string backupPath = GetSavePath($"autosave_{i}");
                if (backupPath == path) continue;

                data = TryLoadSingle(backupPath);
                if (data != null)
                {
                    Debug.Log($"[SaveFileManager] Recovered from backup: {backupPath}");
                    return data;
                }
            }

            string shutdownPath = GetSavePath("shutdown_save");
            if (shutdownPath != path)
            {
                data = TryLoadSingle(shutdownPath);
                if (data != null)
                {
                    Debug.Log($"[SaveFileManager] Recovered from shutdown save: {shutdownPath}");
                    return data;
                }
            }

            return null;
        }

        /// <summary>
        /// Rotate numbered backups: N-1 → N, N-2 → N-1, ..., delete oldest.
        /// </summary>
        public static void RotateBackups(string prefix)
        {
            for (int i = MAX_BACKUPS - 1; i >= 0; i--)
            {
                string current = GetSavePath($"{prefix}_{i}");
                if (!File.Exists(current)) continue;

                if (i == MAX_BACKUPS - 1)
                {
                    File.Delete(current);
                }
                else
                {
                    string next = GetSavePath($"{prefix}_{i + 1}");
                    if (File.Exists(next)) File.Delete(next);
                    File.Move(current, next);
                }
            }
        }

        /// <summary>
        /// List all save slots with metadata for UI display.
        /// </summary>
        public static List<SaveSlotInfo> ListSaves()
        {
            var saves = new List<SaveSlotInfo>();
            string dir = GetSaveDirectory();

            if (!Directory.Exists(dir)) return saves;

            // Top-level only — recovery files live in the .recovery subdir and must
            // never appear here. SearchOption.TopDirectoryOnly is the default but we
            // pass it explicitly for safety.
            foreach (string file in Directory.GetFiles(dir, $"*{SAVE_EXTENSION}", SearchOption.TopDirectoryOnly))
            {
                // Defensive filter for any legacy file that wasn't migrated yet.
                string nameNoExt = Path.GetFileNameWithoutExtension(file);
                if (ReservedSaveNames.Contains(nameNoExt)) continue;

                try
                {
                    string json = File.ReadAllText(file);
                    var data = JsonUtility.FromJson<GameSaveData>(json);

                    saves.Add(new SaveSlotInfo
                    {
                        path          = file,
                        fileName      = Path.GetFileNameWithoutExtension(file),
                        timestamp     = data?.timestamp ?? "",
                        schemaVersion = data?.schemaVersion ?? "unknown",
                        isCorrupted   = false,
                        runId         = data?.GetMeta("run_id", "") ?? "",
                        playerClass   = data?.player?.playerClass  ?? "",
                        level         = data?.player?.level        ?? 0,
                        experience    = data?.player?.experience   ?? 0,
                        hp            = data?.player?.hp           ?? 0,
                        maxHp         = data?.player?.maxHp        ?? 0,
                        currentZone   = data?.player?.currentZone  ?? "",
                    });
                }
                catch
                {
                    saves.Add(new SaveSlotInfo
                    {
                        path          = file,
                        fileName      = Path.GetFileNameWithoutExtension(file),
                        timestamp     = "corrupted",
                        schemaVersion = "unknown",
                        isCorrupted   = true,
                    });
                }
            }

            saves.Sort((a, b) => string.Compare(b.timestamp, a.timestamp, StringComparison.Ordinal));
            return saves;
        }

        /// <summary>
        /// Returns saves grouped by run_id, sorted newest group first.
        /// Saves without a run_id are collected in a single legacy group.
        /// </summary>
        public static List<RunGroupInfo> ListSavesByRun()
        {
            var allSaves = ListSaves(); // already sorted newest-first

            var byRunId    = new Dictionary<string, RunGroupInfo>(StringComparer.Ordinal);
            RunGroupInfo legacyGroup = null;

            foreach (var save in allSaves)
            {
                if (string.IsNullOrEmpty(save.runId))
                {
                    if (legacyGroup == null)
                        legacyGroup = new RunGroupInfo { runId = "", isLegacy = true };
                    legacyGroup.saves.Add(save);
                }
                else
                {
                    if (!byRunId.TryGetValue(save.runId, out var group))
                    {
                        group = new RunGroupInfo { runId = save.runId, isLegacy = false };
                        byRunId[save.runId] = group;
                    }
                    group.saves.Add(save);
                }
            }

            var result = new List<RunGroupInfo>(byRunId.Values);
            if (legacyGroup != null) result.Add(legacyGroup);

            // Populate display fields from each group’s newest save
            foreach (var group in result)
            {
                var newest = group.saves.Count > 0 ? group.saves[0] : default;
                group.playerClass     = newest.playerClass;
                group.latestTimestamp = newest.timestamp;
                group.maxLevel        = 0;
                foreach (var s in group.saves)
                    if (s.level > group.maxLevel) group.maxLevel = s.level;

                if (group.isLegacy)
                {
                    group.displayName = "Partidas antiguas";
                }
                else
                {
                    string cls  = string.IsNullOrEmpty(newest.playerClass) ? "?" : newest.playerClass;
                    string zone = string.IsNullOrEmpty(newest.currentZone) ? "—" : newest.currentZone;
                    group.displayName = $"{cls} · {zone} · Lv.{group.maxLevel}";
                }
            }

            // Sort groups by latest timestamp descending
            result.Sort((a, b) => string.Compare(b.latestTimestamp, a.latestTimestamp, StringComparison.Ordinal));
            return result;
        }

        // ── Position checkpoint ──────────────────────────────────────────────

        private const string POSITION_CHECKPOINT_FILE     = "position_checkpoint";
        private const string POSITION_CHECKPOINT_BAK_FILE = "position_checkpoint_bak";

        public static string GetPositionCheckpointPath() =>
            Path.Combine(GetRecoveryDirectory(), POSITION_CHECKPOINT_FILE + SAVE_EXTENSION);

        public static string GetPositionCheckpointBakPath() =>
            Path.Combine(GetRecoveryDirectory(), POSITION_CHECKPOINT_BAK_FILE + SAVE_EXTENSION);

        // Legacy paths (pre-refactor — files lived in the top-level Saves/ folder
        // alongside user saves). Kept only so DeletePositionCheckpoint can clean
        // any stragglers on installs that haven't yet been migrated.
        private static string GetLegacyPositionCheckpointPath() =>
            Path.Combine(GetSaveDirectory(), POSITION_CHECKPOINT_FILE + SAVE_EXTENSION);
        private static string GetLegacyPositionCheckpointBakPath() =>
            Path.Combine(GetSaveDirectory(), POSITION_CHECKPOINT_BAK_FILE + SAVE_EXTENSION);

        /// <summary>
        /// Atomically write a position checkpoint and keep a backup copy.
        /// Safe to call every few seconds — tiny file, no checksum overhead.
        /// </summary>
        public static void WritePositionCheckpoint(PositionCheckpointData data)
        {
            EnsureSaveDirectory();
            string json = JsonUtility.ToJson(data, false);
            string path = GetPositionCheckpointPath();
            string tmp  = path + ".tmp";

            // Atomic primary write (temp → rename)
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);

            // Backup copy (non-atomic; primary is already committed)
            try { File.WriteAllText(GetPositionCheckpointBakPath(), json); }
            catch { /* backup failure is non-fatal */ }
        }

        /// <summary>
        /// Read the most recent valid position checkpoint.
        /// Falls back to the backup copy on any read error.
        /// Returns null if no checkpoint exists.
        /// </summary>
        public static PositionCheckpointData ReadPositionCheckpoint() =>
            TryReadPositionCheckpoint(GetPositionCheckpointPath())
            ?? TryReadPositionCheckpoint(GetPositionCheckpointBakPath());

        private static PositionCheckpointData TryReadPositionCheckpoint(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var data = JsonUtility.FromJson<PositionCheckpointData>(json);
                return (data != null && !string.IsNullOrEmpty(data.timestamp)) ? data : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Delete both position checkpoint files (call on "New Game" to prevent
        /// a stale checkpoint from a previous session being applied).
        /// </summary>
        public static void DeletePositionCheckpoint()
        {
            try { if (File.Exists(GetPositionCheckpointPath()))    File.Delete(GetPositionCheckpointPath()); }    catch { /* ignored */ }
            try { if (File.Exists(GetPositionCheckpointBakPath())) File.Delete(GetPositionCheckpointBakPath()); } catch { /* ignored */ }
            // Legacy locations (kept defensively until everyone has migrated)
            try { if (File.Exists(GetLegacyPositionCheckpointPath()))    File.Delete(GetLegacyPositionCheckpointPath()); }    catch { /* ignored */ }
            try { if (File.Exists(GetLegacyPositionCheckpointBakPath())) File.Delete(GetLegacyPositionCheckpointBakPath()); } catch { /* ignored */ }
        }

        /// <summary>
        /// Delete a save file (and its checksum sidecar) from disk.
        /// </summary>
        public static bool DeleteSave(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    string checksumPath = path.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
                    if (File.Exists(checksumPath)) File.Delete(checksumPath);
                    Debug.Log($"[SaveFileManager] Deleted save: {path}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveFileManager] Delete failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sanitize an arbitrary user-entered string into a safe save filename
        /// (without extension). Replaces invalid characters with underscores.
        /// Returns null/empty if the input cannot produce a valid name.
        /// </summary>
        public static string SanitizeSaveName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw.Trim())
                sb.Append(System.Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string name = sb.ToString().Trim('.', ' ');
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// Rename a save slot. Renames both the .json and the .sha256 sidecar.
        /// Returns the new full path on success, or null on failure
        /// (invalid name, target already exists, or IO error).
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

                string dir = Path.GetDirectoryName(currentPath);
                string newPath = Path.Combine(dir, sanitized + SAVE_EXTENSION);

                if (string.Equals(newPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    return currentPath; // no change

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

        // ── Checksum helpers ──

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
            if (!File.Exists(checksumPath))
                return true; // Legacy save without checksum — accept

            try
            {
                string storedHash = File.ReadAllText(checksumPath).Trim();
                string computedHash = ComputeSha256(json);
                return string.Equals(storedHash, computedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true; // Fail open on checksum read errors
            }
        }

        private static string ComputeSha256(string input)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(64);
                foreach (byte b in bytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
