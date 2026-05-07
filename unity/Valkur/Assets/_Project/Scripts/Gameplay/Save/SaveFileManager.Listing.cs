using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    public static partial class SaveFileManager
    {
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

                int runOrdinal = 0;
                string ordinalStr = data?.GetMeta("run_ordinal", "");
                if (!string.IsNullOrEmpty(ordinalStr))
                    int.TryParse(ordinalStr, System.Globalization.NumberStyles.Integer,
                                 System.Globalization.CultureInfo.InvariantCulture, out runOrdinal);

                return new SaveSlotInfo
                {
                    path          = file,
                    fileName      = Path.GetFileNameWithoutExtension(file),
                    timestamp     = data?.timestamp ?? "",
                    schemaVersion = data?.schemaVersion ?? "unknown",
                    isCorrupted   = false,
                    isAutoSave    = isAutoSave,
                    runId         = runId,
                    runOrdinal    = runOrdinal,
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
                group.runOrdinal      = 0;
                foreach (var s in group.saves)
                {
                    if (s.level > group.maxLevel) group.maxLevel = s.level;
                    // The ordinal is per-run, so every save in the group should
                    // carry the same value. Take the first non-zero we find;
                    // pre-ordinal saves contribute 0 and the group falls back
                    // to "Run #?" — which the UI distinguishes from a real run.
                    if (group.runOrdinal == 0 && s.runOrdinal > 0)
                        group.runOrdinal = s.runOrdinal;
                }

                if (group.isLegacy)
                {
                    group.displayName = "Partidas antiguas";
                }
                else
                {
                    string cls  = string.IsNullOrEmpty(newest.playerClass) ? "?" : newest.playerClass;
                    string zone = string.IsNullOrEmpty(newest.currentZone) ? "—" : newest.currentZone;
                    string runTag = group.runOrdinal > 0 ? $"Run #{group.runOrdinal} · " : "";
                    group.displayName = $"{runTag}{cls} · {zone} · Lv.{group.maxLevel}";
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
    }
}
