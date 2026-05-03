using System;
using System.IO;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    public static partial class SaveFileManager
    {
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
            PruneEmptyRunFolders();
        }

        /// <summary>
        /// Removes per-run subfolders that contain no visible save files.
        /// These are leftovers from sessions that got interrupted before any
        /// save was written, or from older builds before the autosave-on-quit
        /// pruning was wired up. Safe by construction — never touches
        /// <c>.recovery</c>, <c>legacy</c>, or any folder that still has JSON
        /// files at its top level.
        /// </summary>
        private static void PruneEmptyRunFolders()
        {
            string root = GetSaveDirectory();
            if (!Directory.Exists(root)) return;

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(root); }
            catch { return; }

            foreach (string runDir in subdirs)
            {
                string folder = Path.GetFileName(runDir);
                if (string.IsNullOrEmpty(folder)) continue;
                if (folder.StartsWith(".")) continue; // .recovery, etc.
                if (string.Equals(folder, LEGACY_SUBDIR, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    bool hasVisibleSaves = Directory.GetFiles(runDir, "*" + SAVE_EXTENSION,
                                                              SearchOption.TopDirectoryOnly).Length > 0;
                    if (hasVisibleSaves) continue;

                    Directory.Delete(runDir, recursive: true);
                    Debug.Log($"[SaveFileManager] Pruned empty run folder: {folder}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SaveFileManager] Could not prune {runDir}: {ex.Message}");
                }
            }
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
    }
}
