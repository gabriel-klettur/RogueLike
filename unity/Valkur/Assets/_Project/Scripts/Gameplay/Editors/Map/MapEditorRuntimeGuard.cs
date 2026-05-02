using System;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Runtime counterpart to <c>MapEditorDataGuard</c> (which is Editor-only).
    /// Runs before the first scene loads in standalone builds AND in the
    /// Editor, ensuring the persistence file is in a healthy state by the
    /// time <c>MapEditorManager.Start</c> calls <c>LoadZonesFromDisk</c>.
    ///
    /// Defenses provided in build (where the Editor guard cannot help):
    ///   1. Sweep stale <c>*.test_backup_*</c> orphans (defensive — these
    ///      shouldn't exist in a build, but a developer who builds while a
    ///      cancelled test left orphans behind would otherwise ship them).
    ///   2. If the primary file is missing or unparseable, promote the
    ///      sidecar <c>.bak</c> (the second copy <c>PersistZonesToDisk</c>
    ///      atomic-replace produces on every save) to primary. This means
    ///      the user opens the build with their last saved zone set even
    ///      if the most recent write was interrupted mid-flight.
    ///   3. Quarantine a corrupt primary into <c>.corrupt</c> with a UTC
    ///      timestamp so post-mortem analysis is possible without losing
    ///      the recovered state.
    ///
    /// This guard never touches a healthy primary. It is a pure
    /// recovery mechanism, not a write path.
    /// </summary>
    public static class MapEditorRuntimeGuard
    {
        private const string FILE_NAME       = "map_editor_zones.json";
        private const string ORPHAN_PREFIX   = FILE_NAME + ".test_backup_";
        private static readonly TimeSpan ORPHAN_AGE_THRESHOLD = TimeSpan.FromMinutes(15);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnRuntimeLoad()
        {
            try { ValidateAndRecover(); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditorRuntimeGuard] Recovery pass failed (non-fatal): {ex.Message}");
            }
        }

        private static void ValidateAndRecover()
        {
            string primary = GetPrimaryPath();
            string sidecar = primary + ".bak";

            if (!HasUsableContent(primary))
                AdoptStaleOrphan(primary);

            if (HasUsableContent(primary) && !LooksParseable(primary))
            {
                Quarantine(primary);
            }

            if (!HasUsableContent(primary) && HasUsableContent(sidecar) && LooksParseable(sidecar))
            {
                try
                {
                    File.Copy(sidecar, primary, overwrite: true);
                    Debug.LogWarning($"[MapEditorRuntimeGuard] Promoted sidecar to primary — recovered last-saved zones.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MapEditorRuntimeGuard] Could not promote sidecar to primary: {ex.Message}");
                }
            }
        }

        // ── Orphan adoption ──────────────────────────────────────────────────────

        private static void AdoptStaleOrphan(string primary)
        {
            string dir = Path.GetDirectoryName(primary);
            if (!Directory.Exists(dir)) return;

            string[] orphans;
            try { orphans = Directory.GetFiles(dir, ORPHAN_PREFIX + "*"); }
            catch { return; }
            if (orphans.Length == 0) return;

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
                Debug.LogWarning($"[MapEditorRuntimeGuard] Recovered orphaned '{Path.GetFileName(adopt)}' " +
                                 $"as primary (test crash residue). Sweeping {orphans.Length - 1} stale sibling(s).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditorRuntimeGuard] Could not adopt orphan '{adopt}': {ex.Message}");
                return;
            }

            foreach (var path in orphans)
            {
                if (path == adopt) continue;
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
            }
        }

        // ── Quarantine corrupt primary ───────────────────────────────────────────

        private static void Quarantine(string primary)
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string quarantinePath = $"{primary}.corrupt_{stamp}";
            try
            {
                File.Move(primary, quarantinePath);
                Debug.LogWarning($"[MapEditorRuntimeGuard] Primary '{FILE_NAME}' is unparseable — " +
                                 $"quarantined to '{Path.GetFileName(quarantinePath)}'. Will attempt sidecar recovery.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditorRuntimeGuard] Could not quarantine corrupt primary: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string GetPrimaryPath()
            => Path.Combine(Application.persistentDataPath, FILE_NAME);

        private static bool HasUsableContent(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var fi = new FileInfo(path);
                return fi.Length > 16;
            }
            catch { return false; }
        }

        // Cheap parseability probe: must contain the canonical top-level
        // fields. Avoids JsonUtility (which silently returns null on
        // unrecognized shapes — useless for early detection).
        private static bool LooksParseable(string path)
        {
            try
            {
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return false;
                if (text.IndexOf("nextZoneIndex", StringComparison.Ordinal) < 0) return false;
                if (text.IndexOf("zones", StringComparison.Ordinal) < 0) return false;
                int braceOpen  = 0, braceClose = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '{') braceOpen++;
                    else if (text[i] == '}') braceClose++;
                }
                return braceOpen > 0 && braceOpen == braceClose;
            }
            catch { return false; }
        }
    }
}
