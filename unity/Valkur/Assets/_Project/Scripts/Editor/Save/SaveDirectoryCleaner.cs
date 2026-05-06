using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Valkur.Editor.Save
{
    /// <summary>
    /// One-shot maintenance tool for Application.persistentDataPath/Saves/.
    ///
    /// Each gameplay session writes to <c>Saves/&lt;runId&gt;/autosave.json</c>;
    /// abandoned sessions (force-quit before any progression) leave behind
    /// run folders that the runtime <c>SaveService.PruneEmptyRunFolders</c>
    /// only catches when they are completely empty. Folders that still have
    /// a load-time autosave persist forever and clutter the Load Game menu.
    ///
    /// This tool:
    ///   • Lists every run folder under <c>Saves/</c>
    ///   • Sorts by autosave modification time, newest first
    ///   • Keeps the most recent <c>KeepMostRecent</c> runs
    ///   • Deletes the rest (folder + .backups/ + .sha256 sidecars)
    ///   • Skips reserved folders (<c>.recovery</c>, <c>legacy</c>)
    ///
    /// Always confirms via a modal dialog before deleting.
    /// </summary>
    public static class SaveDirectoryCleaner
    {
        private const int    KeepMostRecent  = 1;
        private const string SAVE_DIR        = "Saves";
        private const string LEGACY_SUBDIR   = "legacy";
        private const string AUTOSAVE_NAME   = "autosave";
        private const string SAVE_EXTENSION  = ".json";

        [MenuItem("Valkur/Save/Cleanup Abandoned Runs (delete duplicates)")]
        public static void Cleanup()
        {
            string saveDir = Path.Combine(Application.persistentDataPath, SAVE_DIR);
            if (!Directory.Exists(saveDir))
            {
                EditorUtility.DisplayDialog("Save Cleanup", $"No save directory at:\n{saveDir}", "OK");
                return;
            }

            var runs = ScanRuns(saveDir);
            if (runs.Count == 0)
            {
                EditorUtility.DisplayDialog("Save Cleanup", "No run folders found.", "OK");
                return;
            }

            // Newest first.
            runs.Sort((a, b) => b.AutosaveTimeUtc.CompareTo(a.AutosaveTimeUtc));
            var toKeep   = runs.GetRange(0, Mathf.Min(KeepMostRecent, runs.Count));
            var toDelete = runs.Count > KeepMostRecent
                ? runs.GetRange(KeepMostRecent, runs.Count - KeepMostRecent)
                : new List<RunInfo>();

            if (toDelete.Count == 0)
            {
                EditorUtility.DisplayDialog("Save Cleanup",
                    $"Only {runs.Count} run folder(s) found — within the keep-most-recent " +
                    $"threshold ({KeepMostRecent}). Nothing to delete.",
                    "OK");
                return;
            }

            string keepLabel = toKeep[0].RunId.Substring(0, 8) + "…";
            bool confirmed = EditorUtility.DisplayDialog(
                "Confirm Save Cleanup",
                $"Found {runs.Count} run folder(s) under:\n{saveDir}\n\n" +
                $"Will KEEP the {KeepMostRecent} most recent ({keepLabel}, " +
                $"saved {toKeep[0].AutosaveTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}).\n\n" +
                $"Will DELETE {toDelete.Count} older run(s) and all their backups.",
                "Yes, delete them",
                "Cancel");
            if (!confirmed) return;

            int deleted = 0, errors = 0;
            foreach (var r in toDelete)
            {
                try
                {
                    Directory.Delete(r.FolderPath, recursive: true);
                    deleted++;
                }
                catch (Exception ex)
                {
                    errors++;
                    Debug.LogError($"[SaveDirectoryCleaner] Could not delete {r.FolderPath}: {ex.Message}");
                }
            }

            string summary =
                $"Deleted {deleted} run folder(s).\n" +
                (errors > 0 ? $"{errors} folder(s) failed (see Console).\n" : "") +
                $"Kept the most recent {toKeep.Count} run(s).";
            Debug.Log("[SaveDirectoryCleaner] " + summary.Replace("\n", " | "));
            EditorUtility.DisplayDialog("Save Cleanup — Done", summary, "OK");
        }

        [MenuItem("Valkur/Save/Open Saves Folder in Explorer")]
        public static void OpenSavesFolder()
        {
            string saveDir = Path.Combine(Application.persistentDataPath, SAVE_DIR);
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
            EditorUtility.RevealInFinder(saveDir);
        }

        private static List<RunInfo> ScanRuns(string saveDir)
        {
            var result = new List<RunInfo>();
            string[] subs;
            try { subs = Directory.GetDirectories(saveDir); }
            catch { return result; }

            foreach (string dir in subs)
            {
                string folder = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(folder)) continue;
                if (folder.StartsWith(".")) continue;
                if (string.Equals(folder, LEGACY_SUBDIR, StringComparison.OrdinalIgnoreCase)) continue;

                string autosave = Path.Combine(dir, AUTOSAVE_NAME + SAVE_EXTENSION);
                DateTime mtime = File.Exists(autosave)
                    ? File.GetLastWriteTimeUtc(autosave)
                    : Directory.GetLastWriteTimeUtc(dir);

                result.Add(new RunInfo
                {
                    RunId           = folder,
                    FolderPath      = dir,
                    AutosaveTimeUtc = mtime,
                });
            }
            return result;
        }

        private struct RunInfo
        {
            public string   RunId;
            public string   FolderPath;
            public DateTime AutosaveTimeUtc;
        }
    }
}
