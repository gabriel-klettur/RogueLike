using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Handles all file IO for the save system: read, write, checksum,
    /// backup rotation, directory management, and slot listing.
    /// Pure IO — no game state knowledge.
    ///
    /// Folder layout (post-refactor — per-run isolation):
    /// <code>
    ///   Saves/
    ///     .recovery/
    ///       position_checkpoint.json
    ///       position_checkpoint_bak.json
    ///     legacy/                         ← saves migrated without run_id
    ///       *.json
    ///     &lt;runId&gt;/
    ///       autosave.json                 ← single auto-save entry per run
    ///       &lt;manual_name&gt;.json           ← user-created manual saves
    ///       .backups/
    ///         autosave_1.json … autosave_5.json
    /// </code>
    /// All "Auto-Save" semantics (timer autosave, shutdown save, quicksave, exit save)
    /// collapse to the same per-run <c>autosave.json</c>. Manual saves are ANY save
    /// the player explicitly named via <see cref="SaveService.Save(string)"/>.
    /// </summary>
    public static class SaveFileManager
    {
        // ── Directory layout constants ───────────────────────────────────────
        private const string SAVE_DIR        = "Saves";
        private const string RECOVERY_SUBDIR = ".recovery";
        private const string BACKUPS_SUBDIR  = ".backups";
        private const string LEGACY_SUBDIR   = "legacy";

        public const string AUTOSAVE_NAME    = "autosave";
        public const string AUTOSAVE_DISPLAY = "Auto-Save";

        private const string SAVE_EXTENSION     = ".json";
        private const string CHECKSUM_EXTENSION = ".sha256";
        public  const int    MAX_BACKUPS        = 5;

        // Names that must never appear in a user-visible save list and that the
        // user is forbidden from picking when manually saving.
        private static readonly HashSet<string> ReservedSaveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "autosave",
            "position_checkpoint",
            "position_checkpoint_bak",
            // Legacy filenames — migrated into per-run autosave.json on boot.
            // Listed here so any leftover never leaks into the UI.
            "quicksave",
            "shutdown_save",
            "autosave_0", "autosave_1", "autosave_2", "autosave_3", "autosave_4",
        };

        // ── Path helpers ─────────────────────────────────────────────────────

        public static string GetSaveDirectory()      => Path.Combine(Application.persistentDataPath, SAVE_DIR);
        public static string GetRecoveryDirectory()  => Path.Combine(GetSaveDirectory(), RECOVERY_SUBDIR);
        public static string GetLegacyRunDirectory() => Path.Combine(GetSaveDirectory(), LEGACY_SUBDIR);

        /// <summary>Returns the per-run folder for <paramref name="runId"/>. Empty/null routes to the legacy folder.</summary>
        public static string GetRunDirectory(string runId)
            => GetRunDirectory(runId, WorldId.Base);

        /// <summary>
        /// Phase 1 per-world overload. <see cref="WorldId.Base"/> preserves
        /// the legacy flat layout (<c>Saves/&lt;runId&gt;/...</c>) so existing
        /// saves remain readable byte-for-byte. Non-base worlds nest under
        /// <c>Saves/&lt;runId&gt;/worlds/&lt;slug&gt;/...</c> from day one so a
        /// session that visited multiple dimensions does not collapse them
        /// into one save folder.
        /// </summary>
        public static string GetRunDirectory(string runId, WorldId worldId)
        {
            if (string.IsNullOrEmpty(runId)) return GetLegacyRunDirectory();
            string runRoot = Path.Combine(GetSaveDirectory(), SanitizeRunIdComponent(runId));
            if (worldId.Equals(WorldId.Base) || string.IsNullOrEmpty(worldId.Slug))
                return runRoot;
            return Path.Combine(runRoot, "worlds", SanitizeRunIdComponent(worldId.Slug));
        }

        public static string GetBackupsDirectory(string runId) =>
            GetBackupsDirectory(runId, WorldId.Base);

        public static string GetBackupsDirectory(string runId, WorldId worldId) =>
            Path.Combine(GetRunDirectory(runId, worldId), BACKUPS_SUBDIR);

        public static string GetAutosavePath(string runId) =>
            GetAutosavePath(runId, WorldId.Base);

        public static string GetAutosavePath(string runId, WorldId worldId) =>
            Path.Combine(GetRunDirectory(runId, worldId), AUTOSAVE_NAME + SAVE_EXTENSION);

        public static string GetManualSavePath(string runId, string slotName) =>
            GetManualSavePath(runId, slotName, WorldId.Base);

        public static string GetManualSavePath(string runId, string slotName, WorldId worldId) =>
            Path.Combine(GetRunDirectory(runId, worldId), slotName + SAVE_EXTENSION);

        public static bool IsReservedSaveName(string name) =>
            !string.IsNullOrEmpty(name) && ReservedSaveNames.Contains(name);

        // Defensive: don't allow directory traversal or path separators in run-id components.
        private static string SanitizeRunIdComponent(string runId)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(runId.Length);
            foreach (char c in runId)
                sb.Append(System.Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim('.', ' ');
            return string.IsNullOrEmpty(s) ? "_invalid" : s;
        }

        // ── Directory bootstrap ──────────────────────────────────────────────

        public static void EnsureSaveDirectory()
        {
            string dir = GetSaveDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[SaveFileManager] Created save directory: {dir}");
            }
            if (!Directory.Exists(GetRecoveryDirectory()))
                Directory.CreateDirectory(GetRecoveryDirectory());

            MigrateLegacyRecoveryFiles();
            MigrateLegacyFlatSaves();
        }

        /// <summary>One-shot migration of pre-refactor recovery files into <c>.recovery/</c>.</summary>
        private static void MigrateLegacyRecoveryFiles()
        {
            try
            {
                foreach (string reserved in new[] { "position_checkpoint", "position_checkpoint_bak" })
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
        /// One-shot migration: move pre-refactor flat save files
        /// (<c>Saves/*.json</c>) into per-run subfolders. Files mapped to
        /// <c>autosave.json</c>:  <c>quicksave</c>, <c>shutdown_save</c>,
        /// <c>autosave_0..4</c>. Manual saves keep their name. The newest
        /// candidate wins on collision (timestamp comparison).
        /// </summary>
        private static void MigrateLegacyFlatSaves()
        {
            string root = GetSaveDirectory();
            if (!Directory.Exists(root)) return;

            string[] files;
            try { files = Directory.GetFiles(root, "*" + SAVE_EXTENSION, SearchOption.TopDirectoryOnly); }
            catch { return; }

            foreach (string file in files)
            {
                string nameNoExt = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(nameNoExt, "position_checkpoint",     StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nameNoExt, "position_checkpoint_bak", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    string json = File.ReadAllText(file);
                    GameSaveData data = null;
                    try { data = JsonUtility.FromJson<GameSaveData>(json); } catch { /* corrupted */ }

                    string runId = data?.GetMeta("run_id", "") ?? "";
                    string targetDir = GetRunDirectory(runId);
                    Directory.CreateDirectory(targetDir);

                    string targetName = nameNoExt;
                    bool collapseToAutosave =
                        string.Equals(nameNoExt, "quicksave",     StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(nameNoExt, "shutdown_save", StringComparison.OrdinalIgnoreCase) ||
                        nameNoExt.StartsWith("autosave_", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(nameNoExt, "autosave",      StringComparison.OrdinalIgnoreCase);
                    if (collapseToAutosave) targetName = AUTOSAVE_NAME;

                    string dest = Path.Combine(targetDir, targetName + SAVE_EXTENSION);
                    string srcSidecar  = file.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
                    string destSidecar = dest.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);

                    if (File.Exists(dest))
                    {
                        // Newest wins
                        GameSaveData existing = null;
                        try { existing = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(dest)); } catch { }
                        bool destNewer = string.Compare(existing?.timestamp ?? "", data?.timestamp ?? "",
                                                       StringComparison.Ordinal) >= 0;
                        if (destNewer)
                        {
                            File.Delete(file);
                            if (File.Exists(srcSidecar)) File.Delete(srcSidecar);
                            continue;
                        }
                        File.Delete(dest);
                        if (File.Exists(destSidecar)) File.Delete(destSidecar);
                    }

                    File.Move(file, dest);
                    if (File.Exists(srcSidecar))
                    {
                        if (File.Exists(destSidecar)) File.Delete(destSidecar);
                        File.Move(srcSidecar, destSidecar);
                    }
                    Debug.Log($"[SaveFileManager] Migrated flat save '{nameNoExt}' → '{Path.GetFileName(targetDir)}/{targetName}'");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SaveFileManager] Could not migrate {file}: {ex.Message}");
                }
            }
        }

        // ── Write / read core ────────────────────────────────────────────────

        /// <summary>Write save data to disk with atomic rename and checksum sidecar.</summary>
        public static void WriteSaveFile(string path, GameSaveData data, string schemaVersion)
        {
            data.schemaVersion = schemaVersion;
            string json = JsonUtility.ToJson(data, true);

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);

            WriteChecksum(path, json);
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
        {
            var data = TryLoadSingle(path);
            if (data != null) return data;

            Debug.LogWarning($"[SaveFileManager] Primary save corrupted or missing: {path}. Trying backups…");

            string runDir = Path.GetDirectoryName(path) ?? "";
            string backupsDir = Path.Combine(runDir, BACKUPS_SUBDIR);
            if (Directory.Exists(backupsDir))
            {
                for (int i = 1; i <= MAX_BACKUPS; i++)
                {
                    string bp = Path.Combine(backupsDir, $"{AUTOSAVE_NAME}_{i}" + SAVE_EXTENSION);
                    data = TryLoadSingle(bp);
                    if (data != null)
                    {
                        Debug.Log($"[SaveFileManager] Recovered from backup: {bp}");
                        return data;
                    }
                }
            }

            return null;
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

        // ── Listing ──────────────────────────────────────────────────────────

        /// <summary>
        /// Flat list of every visible save (across all runs and the legacy folder).
        /// Used by callers that just want to know "are there any saves?" or
        /// build a flat picker (pause-menu Load).  Reserved/recovery files are
        /// always excluded.  Sorted newest-first.
        /// </summary>
        public static List<SaveSlotInfo> ListSaves()
        {
            EnsureSaveDirectory();
            var result = new List<SaveSlotInfo>();
            string root = GetSaveDirectory();
            if (!Directory.Exists(root)) return result;

            // Top-level *.json (legacy stragglers — should be empty post-migration)
            CollectSavesFrom(root, runId: "", result);

            // Per-run subfolders
            foreach (string runDir in Directory.GetDirectories(root))
            {
                string folder = Path.GetFileName(runDir);
                if (folder.StartsWith(".")) continue; // .recovery, .backups (shouldn't be here), etc.
                string runId = string.Equals(folder, LEGACY_SUBDIR, StringComparison.OrdinalIgnoreCase)
                    ? "" : folder;
                CollectSavesFrom(runDir, runId, result);
            }

            result.Sort((a, b) =>
            {
                // AutoSave first within same run; then by timestamp desc
                if (a.runId == b.runId && a.isAutoSave != b.isAutoSave) return a.isAutoSave ? -1 : 1;
                return string.Compare(b.timestamp, a.timestamp, StringComparison.Ordinal);
            });
            return result;
        }

        private static void CollectSavesFrom(string dir, string runId, List<SaveSlotInfo> result)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir, "*" + SAVE_EXTENSION, SearchOption.TopDirectoryOnly))
            {
                string nameNoExt = Path.GetFileNameWithoutExtension(file);

                // Defensive filter: position_checkpoint and other reserved auxiliaries
                // never appear, but autosave.json IS allowed (it's the per-run autosave).
                bool isAutoSave = string.Equals(nameNoExt, AUTOSAVE_NAME, StringComparison.OrdinalIgnoreCase);
                if (!isAutoSave && ReservedSaveNames.Contains(nameNoExt)) continue;

                var info = ReadSaveSlotInfo(file, runId, isAutoSave);
                result.Add(info);
            }
        }

        private static SaveSlotInfo ReadSaveSlotInfo(string file, string runIdHint, bool isAutoSave)
        {
            try
            {
                string json = File.ReadAllText(file);
                var data = JsonUtility.FromJson<GameSaveData>(json);
                string runId = data?.GetMeta("run_id", "") ?? "";
                if (string.IsNullOrEmpty(runId)) runId = runIdHint ?? "";

                return new SaveSlotInfo
                {
                    path          = file,
                    fileName      = Path.GetFileNameWithoutExtension(file),
                    timestamp     = data?.timestamp ?? "",
                    schemaVersion = data?.schemaVersion ?? "unknown",
                    isCorrupted   = false,
                    isAutoSave    = isAutoSave,
                    runId         = runId,
                    playerClass   = data?.player?.playerClass ?? "",
                    level         = data?.player?.level       ?? 0,
                    experience    = data?.player?.experience  ?? 0,
                    hp            = data?.player?.hp          ?? 0,
                    maxHp         = data?.player?.maxHp       ?? 0,
                    currentZone   = data?.player?.currentZone ?? "",
                };
            }
            catch
            {
                return new SaveSlotInfo
                {
                    path          = file,
                    fileName      = Path.GetFileNameWithoutExtension(file),
                    timestamp     = "corrupted",
                    schemaVersion = "unknown",
                    isCorrupted   = true,
                    isAutoSave    = isAutoSave,
                    runId         = runIdHint ?? "",
                };
            }
        }

        /// <summary>
        /// Returns saves grouped by run_id.  Within each group, the per-run
        /// <c>autosave.json</c> is always first, followed by manual saves
        /// sorted newest-first.  Saves without a run_id are collected in a
        /// single "legacy" group, displayed last.
        /// </summary>
        public static List<RunGroupInfo> ListSavesByRun()
        {
            var allSaves = ListSaves();
            var byRunId  = new Dictionary<string, RunGroupInfo>(StringComparer.Ordinal);
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

            var groups = new List<RunGroupInfo>(byRunId.Values);
            if (legacyGroup != null) groups.Add(legacyGroup);

            foreach (var group in groups)
            {
                // Within the group: AutoSave first, then manual saves newest-first.
                group.saves.Sort((a, b) =>
                {
                    if (a.isAutoSave != b.isAutoSave) return a.isAutoSave ? -1 : 1;
                    return string.Compare(b.timestamp, a.timestamp, StringComparison.Ordinal);
                });

                // Pick newest entry (autosave preferred since it sorts first) for display meta.
                var newest = group.saves[0];
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

            // Newest run first; legacy always last.
            groups.Sort((a, b) =>
            {
                if (a.isLegacy != b.isLegacy) return a.isLegacy ? 1 : -1;
                return string.Compare(b.latestTimestamp, a.latestTimestamp, StringComparison.Ordinal);
            });
            return groups;
        }

        // ── Position checkpoint ──────────────────────────────────────────────

        private const string POSITION_CHECKPOINT_FILE     = "position_checkpoint";
        private const string POSITION_CHECKPOINT_BAK_FILE = "position_checkpoint_bak";

        public static string GetPositionCheckpointPath() =>
            Path.Combine(GetRecoveryDirectory(), POSITION_CHECKPOINT_FILE + SAVE_EXTENSION);

        public static string GetPositionCheckpointBakPath() =>
            Path.Combine(GetRecoveryDirectory(), POSITION_CHECKPOINT_BAK_FILE + SAVE_EXTENSION);

        private static string GetLegacyPositionCheckpointPath() =>
            Path.Combine(GetSaveDirectory(), POSITION_CHECKPOINT_FILE + SAVE_EXTENSION);
        private static string GetLegacyPositionCheckpointBakPath() =>
            Path.Combine(GetSaveDirectory(), POSITION_CHECKPOINT_BAK_FILE + SAVE_EXTENSION);

        public static void WritePositionCheckpoint(PositionCheckpointData data)
        {
            EnsureSaveDirectory();
            string json = JsonUtility.ToJson(data, false);
            string path = GetPositionCheckpointPath();
            string tmp  = path + ".tmp";

            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);

            try { File.WriteAllText(GetPositionCheckpointBakPath(), json); }
            catch { /* backup is best-effort */ }
        }

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

        public static void DeletePositionCheckpoint()
        {
            try { if (File.Exists(GetPositionCheckpointPath()))    File.Delete(GetPositionCheckpointPath()); }    catch { }
            try { if (File.Exists(GetPositionCheckpointBakPath())) File.Delete(GetPositionCheckpointBakPath()); } catch { }
            try { if (File.Exists(GetLegacyPositionCheckpointPath()))    File.Delete(GetLegacyPositionCheckpointPath()); }    catch { }
            try { if (File.Exists(GetLegacyPositionCheckpointBakPath())) File.Delete(GetLegacyPositionCheckpointBakPath()); } catch { }
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

        private static bool IsPrunableRunDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            string parent = Path.GetDirectoryName(dir);
            if (!string.Equals(parent, GetSaveDirectory(), StringComparison.OrdinalIgnoreCase))
                return false;
            string folder = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(folder)) return false;
            if (folder.StartsWith(".")) return false; // never prune .recovery
            // Don't prune the legacy bucket — it's a stable migration target.
            if (string.Equals(folder, LEGACY_SUBDIR, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

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
