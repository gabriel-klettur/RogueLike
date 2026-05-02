#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Protects <c>persistentDataPath/map_editor_zones.json</c> against the
    /// three loss vectors the user reports as "the map resets sometimes":
    ///
    ///   1. <b>Test orphans.</b> MapEditorPersistenceIntegrationTests SetUp does
    ///      <c>File.Move(map_editor_zones.json, *.test_backup_&lt;UUID&gt;)</c>
    ///      and the matching TearDown restores it. If the test crashes or is
    ///      cancelled between the two, the primary is gone forever and the
    ///      backup sits orphaned next to it. On every Editor load this guard
    ///      sweeps stale <c>*.test_backup_*</c> files, restoring the most
    ///      recent one if the primary is missing.
    ///
    ///   2. <b>Crash mid-write.</b> Runtime persistence now writes atomically
    ///      and keeps a sidecar <c>.bak</c> in persistentDataPath; this guard
    ///      adds a second-tier copy inside <c>_Project/Data/Backups/</c> so
    ///      even a wiped LocalLow folder can be recovered from the project.
    ///
    ///   3. <b>Manual deletion.</b> If both the primary and the sidecar are
    ///      gone but the project-side backup exists, restore from it.
    ///
    /// Mirrors <see cref="BuildingsDataGuard"/> conceptually but cannot use the
    /// AssetModificationProcessor hooks because the file lives in
    /// persistentDataPath, not in Assets/StreamingAssets.
    /// </summary>
    public static class MapEditorDataGuard
    {
        private const string FILE_NAME       = "map_editor_zones.json";
        private const string BACKUP_REL      = "_Project/Data/Backups/map_editor_zones.json.bak";
        private const string ORPHAN_PREFIX   = FILE_NAME + ".test_backup_";
        // An orphan younger than this is probably an in-flight test. Older =
        // the test ran, crashed, and never restored. We adopt it.
        private static readonly TimeSpan ORPHAN_AGE_THRESHOLD = TimeSpan.FromMinutes(15);

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            EditorApplication.delayCall += ValidateAndRecover;
        }

        private static void ValidateAndRecover()
        {
            string primary  = GetPrimaryPath();
            string sidecar  = primary + ".bak";
            string projBak  = GetProjectBackupPath();

            // 1) Adopt orphaned test_backup files if the primary is gone. Don't
            //    touch them while the primary is healthy (a test might be running).
            if (!HasUsableContent(primary))
                AdoptStaleOrphan(primary);

            // 2) If primary still missing/empty, fall back to sidecar.
            if (!HasUsableContent(primary) && HasUsableContent(sidecar))
            {
                File.Copy(sidecar, primary, overwrite: true);
                Debug.LogWarning($"[MapEditorDataGuard] Restored '{FILE_NAME}' from sidecar '.bak'.");
            }

            // 3) If still missing, fall back to project-side backup.
            if (!HasUsableContent(primary) && HasUsableContent(projBak))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(primary));
                File.Copy(projBak, primary, overwrite: true);
                Debug.LogWarning($"[MapEditorDataGuard] Restored '{FILE_NAME}' from project backup '{projBak}'.");
            }

            // 4) Regression check: primary exists but is poorer than the
            //    project backup. This is the test-induced loss vector — a
            //    test ran PersistZonesToDisk on a manager seeded with one
            //    zone, then LoadZonesFromDisk on next boot dropped it as a
            //    DB collision and saved a DB-only file. The project backup
            //    still has the user's full set; promote it back.
            if (HasUsableContent(primary) && HasUsableContent(projBak) &&
                IsPrimaryPoorerThanBackup(primary, projBak))
            {
                File.Copy(projBak, primary, overwrite: true);
                Debug.LogWarning($"[MapEditorDataGuard] Primary persistence appears regressed " +
                                 $"(fewer user zones than project backup) — restored from '{projBak}'.");
            }

            // 5) If primary now exists with custom-zone content, keep the
            //    project backup fresh so the next "open after closing Unity"
            //    has something to recover from.
            if (HasUsableContent(primary) && PersistenceContainsUserZones(primary))
            {
                RefreshProjectBackup(primary, projBak);
            }
        }

        // Cheap zone-count-based regression detector: if the project backup
        // has a strictly higher nextZoneIndex than the primary, the primary
        // has lost user-created zones. nextZoneIndex monotonically grows on
        // every ConfirmAddZone, so a drop is impossible under correct usage.
        private static bool IsPrimaryPoorerThanBackup(string primary, string projBak)
        {
            int pIdx = ReadNextZoneIndex(primary);
            int bIdx = ReadNextZoneIndex(projBak);
            return bIdx > pIdx && bIdx > 1;
        }

        private static int ReadNextZoneIndex(string path)
        {
            try
            {
                string text = File.ReadAllText(path);
                int idx = text.IndexOf("nextZoneIndex", StringComparison.Ordinal);
                if (idx < 0) return 1;
                int colon = text.IndexOf(':', idx);
                if (colon < 0) return 1;
                int end = text.IndexOfAny(new[] { ',', '\n', '\r', '}' }, colon);
                if (end < 0) end = text.Length;
                string val = text.Substring(colon + 1, end - colon - 1).Trim();
                return int.TryParse(val, out int n) ? n : 1;
            }
            catch { return 1; }
        }

        // ── Orphan handling ──────────────────────────────────────────────────────────

        private static void AdoptStaleOrphan(string primary)
        {
            string dir = Path.GetDirectoryName(primary);
            if (!Directory.Exists(dir)) return;

            string[] orphans;
            try { orphans = Directory.GetFiles(dir, ORPHAN_PREFIX + "*"); }
            catch { return; }
            if (orphans.Length == 0) return;

            // Pick the most recent orphan that's older than the safety
            // threshold (avoids racing with an in-flight test).
            DateTime now = DateTime.UtcNow;
            string adopt = null;
            DateTime adoptTime = DateTime.MinValue;
            foreach (var path in orphans)
            {
                DateTime mt;
                try { mt = File.GetLastWriteTimeUtc(path); } catch { continue; }
                if (now - mt < ORPHAN_AGE_THRESHOLD) continue;
                if (mt > adoptTime) { adopt = path; adoptTime = mt; }
            }

            if (adopt == null) return;
            try
            {
                File.Move(adopt, primary);
                Debug.LogWarning($"[MapEditorDataGuard] Recovered orphaned test backup '{Path.GetFileName(adopt)}' " +
                                 $"as '{FILE_NAME}' (test crashed without restoring). " +
                                 $"Sweeping {orphans.Length - 1} other orphan(s).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditorDataGuard] Could not adopt orphan '{adopt}': {ex.Message}");
                return;
            }

            // Sweep the rest — they're stale by definition now.
            foreach (var path in orphans)
            {
                if (path == adopt) continue;
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
            }
        }

        // ── Project-side backup refresh ──────────────────────────────────────────────

        private static void RefreshProjectBackup(string primary, string projBak)
        {
            try
            {
                string dir = Path.GetDirectoryName(projBak);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.Copy(primary, projBak, overwrite: true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditorDataGuard] Failed to refresh project backup '{projBak}': {ex.Message}");
            }
        }

        // ── Path helpers ─────────────────────────────────────────────────────────────

        private static string GetPrimaryPath()
            => Path.Combine(Application.persistentDataPath, FILE_NAME);

        private static string GetProjectBackupPath()
        {
            string dataPath = Application.dataPath; // .../Valkur/Assets
            return Path.Combine(dataPath, BACKUP_REL.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool HasUsableContent(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var fi = new FileInfo(path);
                return fi.Length > 16; // empty / truncated files are useless
            }
            catch { return false; }
        }

        // Heuristic: a file is "user-modified" if it has more zones than the DB
        // baseline OR a non-default nextZoneIndex. We don't actually parse — a
        // strings-level check is enough to avoid overwriting a fresh DB-only
        // copy on top of a richer prior backup.
        private static bool PersistenceContainsUserZones(string path)
        {
            try
            {
                string text = File.ReadAllText(path);
                // Default fresh state has nextZoneIndex == 1; any larger value
                // proves the user added at least one zone via F11.
                int idx = text.IndexOf("nextZoneIndex", StringComparison.Ordinal);
                if (idx < 0) return false;
                int colon = text.IndexOf(':', idx);
                if (colon < 0) return false;
                int end = text.IndexOfAny(new[] { ',', '\n', '\r', '}' }, colon);
                if (end < 0) end = text.Length;
                string val = text.Substring(colon + 1, end - colon - 1).Trim();
                if (int.TryParse(val, out int n) && n > 1) return true;
                // Fallback: very rough zone count — if the file is bigger than
                // a vanilla DB dump, treat as user-modified. The DB has 24
                // zones; assume >25 zoneName occurrences means user added some.
                int count = 0, from = 0;
                while ((from = text.IndexOf("\"zoneName\"", from, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    from += 10;
                    if (count > 25) return true;
                }
                return false;
            }
            catch { return false; }
        }
    }
}
#endif
