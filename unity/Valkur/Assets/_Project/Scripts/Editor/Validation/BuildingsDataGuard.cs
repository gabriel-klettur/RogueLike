#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Protects StreamingAssets/Buildings/buildings_instances.json from
    /// accidental deletion and auto-restores it if missing on editor startup.
    ///
    /// Two layers of protection:
    ///   1. OnWillDeleteAsset — blocks deletion from the Project window (asks confirmation).
    ///   2. [InitializeOnLoadMethod] — warns in the Console if the file is absent at startup.
    /// </summary>
    public class BuildingsDataGuard : UnityEditor.AssetModificationProcessor
    {
        // ── Relative paths from repo root ────────────────────────────────────────────
        private const string STREAMING_REL   = "unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json";
        private const string BACKUP_REL      = "unity/Valkur/Assets/_Project/Data/Backups/buildings_instances.json.bak";
        private const string GIT_RESTORE_CMD = "git checkout HEAD -- \"unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json\"";

        // ── 1. Block accidental deletion from the Project window ─────────────────────
        public static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
        {
            if (!assetPath.Replace('\\', '/').Contains("StreamingAssets/Buildings/buildings_instances.json"))
                return AssetDeleteResult.DidNotDelete;

            // OnWillDeleteAsset fires both when the user explicitly deletes from the Project
            // window AND when AssetDatabase.Refresh() detects the file has disappeared from
            // disk (e.g. after test teardown or git operations). In either case, attempt an
            // auto-restore from backup first and cancel the deletion silently.
            string streamingPath = GetStreamingPath();
            string backupPath    = GetBackupPath();
            if (File.Exists(backupPath))
            {
                string dir = Path.GetDirectoryName(streamingPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.Copy(backupPath, streamingPath, overwrite: true);
                AssetDatabase.Refresh();
                Debug.LogWarning("[BuildingsDataGuard] buildings_instances.json deletion blocked — " +
                                 "auto-restored from backup. If you intended to delete it use " +
                                 "File Explorer and run:  " + GIT_RESTORE_CMD);
                return AssetDeleteResult.FailedDelete;  // cancel the deletion
            }

            // No backup available — ask the user explicitly before allowing deletion.
            bool confirmed = EditorUtility.DisplayDialog(
                "⚠ Cannot delete buildings data",
                "buildings_instances.json contains the positions of all buildings.\n\n" +
                "No backup copy was found.\n\n" +
                "Deleting it will make ALL buildings disappear in-game.\n\n" +
                "Are you absolutely sure?",
                "Yes, delete it",
                "Cancel");

            if (!confirmed)
                return AssetDeleteResult.FailedDelete;

            Debug.LogWarning("[BuildingsDataGuard] buildings_instances.json was deleted by user (no backup). " +
                             $"Restore with:  {GIT_RESTORE_CMD}");
            return AssetDeleteResult.DidNotDelete;   // let Unity proceed
        }

        // ── 2. Warn on editor startup if file is absent ──────────────────────────────
        [InitializeOnLoadMethod]
        private static void CheckOnEditorLoad()
        {
            EditorApplication.delayCall += ValidateDataFile;
        }

        private static void ValidateDataFile()
        {
            string instancesPath = GetStreamingPath();
            if (!File.Exists(instancesPath))
            {
                // Attempt auto-restore from backup
                string backupPath = GetBackupPath();
                if (File.Exists(backupPath))
                {
                    string backupDir = Path.GetDirectoryName(instancesPath);
                    if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                    File.Copy(backupPath, instancesPath);
                    AssetDatabase.Refresh();
                    Debug.LogWarning("[BuildingsDataGuard] buildings_instances.json was MISSING — " +
                                     "auto-restored from backup copy. Check your scene.");
                }
                else
                {
                    Debug.LogError("[BuildingsDataGuard] *** CRITICAL *** buildings_instances.json is MISSING " +
                                   "and no backup exists.\n" +
                                   $"Restore with:  {GIT_RESTORE_CMD}");
                }
            }

            // Maintain backup whenever the live file is present
            else
            {
                RefreshBackup(instancesPath);
            }
        }

        // ── 3. Backup helpers (called externally by importers on successful write) ────
        /// <summary>
        /// Creates / refreshes the .bak copy from the live file.
        /// Call this after every successful write to buildings_instances.json.
        /// </summary>
        public static void RefreshBackup()
        {
            string src = GetStreamingPath();
            if (File.Exists(src)) RefreshBackup(src);
        }

        private static void RefreshBackup(string srcPath)
        {
            string dst = GetBackupPath();
            string dir = Path.GetDirectoryName(dst);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.Copy(srcPath, dst, overwrite: true);
        }

        // ── Path helpers ─────────────────────────────────────────────────────────────
        private static string GetStreamingPath()
            => Path.Combine(Application.streamingAssetsPath, "Buildings", "buildings_instances.json");

        private static string GetBackupPath()
        {
            // _Project/Data/Backups/ lives inside Assets but outside StreamingAssets,
            // so it is safe from any StreamingAssets-targeted cleanup.
            string dataPath = Application.dataPath; // .../Valkur/Assets
            return Path.Combine(dataPath, "_Project", "Data", "Backups", "buildings_instances.json.bak");
        }
    }
}
#endif
