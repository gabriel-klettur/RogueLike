using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.MapEditor.Backups
{
    /// <summary>
    /// File-IO layer behind the Map Editor backup system. Each snapshot is a
    /// self-contained directory under <c>Application.persistentDataPath/MapBackups/</c>:
    /// <code>
    ///   MapBackups/
    ///     &lt;slot&gt;_yyyyMMdd_HHmmss/
    ///       manifest.json
    ///       persistent/
    ///         map_editor_zones.json
    ///         Maps/&lt;slot&gt;.zones.json
    ///         Maps/_active.txt
    ///         MapOverrides/&lt;zone&gt;.overlay.json
    ///       streaming/
    ///         Buildings/*.json
    ///         Spawners/*.json
    ///         Lights/*.json
    ///         Particles/*.json
    /// </code>
    /// Snapshots are atomic at the directory level: created in a "<c>.tmp</c>"
    /// suffix and renamed only after every file copy succeeds, so a half-written
    /// snapshot never appears in the listing.
    /// </summary>
    public class MapBackupStore
    {
        private const string DIR_NAME = "MapBackups";
        private const string TMP_SUFFIX = ".tmp";

        // ── Path helpers ─────────────────────────────────────────────────────────

        public string Root => Path.Combine(Application.persistentDataPath, DIR_NAME);
        private string PersistentRoot => Application.persistentDataPath;
        private string StreamingRoot  => Application.streamingAssetsPath;

        public MapBackupStore()
        {
            try { Directory.CreateDirectory(Root); }
            catch (Exception ex) { Debug.LogWarning($"[MapBackup] Cannot create {Root}: {ex.Message}"); }
        }

        // ── Listing ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns every valid snapshot, newest first. A directory is considered
        /// a valid snapshot if it contains a parseable <c>manifest.json</c>.
        /// </summary>
        public List<MapBackupManifest> ListBackups()
        {
            var list = new List<MapBackupManifest>();
            if (!Directory.Exists(Root)) return list;

            foreach (var dir in Directory.GetDirectories(Root))
            {
                if (dir.EndsWith(TMP_SUFFIX, StringComparison.OrdinalIgnoreCase))
                    continue;
                var manifest = TryLoadManifest(dir);
                if (manifest != null) list.Add(manifest);
            }
            list.Sort((a, b) => b.createdUnixSeconds.CompareTo(a.createdUnixSeconds));
            return list;
        }

        public MapBackupManifest LoadManifest(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return TryLoadManifest(Path.Combine(Root, id));
        }

        private MapBackupManifest TryLoadManifest(string snapshotDir)
        {
            string path = Path.Combine(snapshotDir, MapBackupSchema.ManifestFileName);
            if (!File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                var m = JsonUtility.FromJson<MapBackupManifest>(json);
                if (m == null) return null;
                if (string.IsNullOrEmpty(m.id))
                    m.id = Path.GetFileName(snapshotDir);
                return m;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapBackup] Bad manifest in {snapshotDir}: {ex.Message}");
                return null;
            }
        }

        // ── Snapshot creation ────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new snapshot of the named slot's data plus the StreamingAssets
        /// world content shared by all slots today. Returns the manifest of the
        /// freshly-created snapshot or <c>null</c> on failure.
        /// </summary>
        public MapBackupManifest CreateSnapshot(string slot, string label, string kind)
        {
            if (string.IsNullOrWhiteSpace(slot)) slot = "default";
            string sanitized = SanitizeSlot(slot);
            string stamp     = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string id        = $"{sanitized}_{stamp}";

            string finalDir  = Path.Combine(Root, id);
            string stagingDir = finalDir + TMP_SUFFIX;

            // Clean any leftover staging from a previously crashed run.
            try
            {
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
            }
            catch (Exception ex) { Debug.LogWarning($"[MapBackup] Cleanup of {stagingDir} failed: {ex.Message}"); }

            try { Directory.CreateDirectory(stagingDir); }
            catch (Exception ex) { Debug.LogError($"[MapBackup] Cannot create {stagingDir}: {ex.Message}"); return null; }

            var copied = new List<string>();
            long totalBytes = 0;

            try
            {
                long ToBytes(string sourceFile, string relInsideStaging)
                {
                    if (!File.Exists(sourceFile)) return 0;
                    string dst = Path.Combine(stagingDir, relInsideStaging);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? stagingDir);
                    File.Copy(sourceFile, dst, overwrite: true);
                    var fi = new FileInfo(dst);
                    copied.Add(relInsideStaging.Replace('\\', '/'));
                    return fi.Length;
                }

                // ── Persistent zone data (per-user) ──
                totalBytes += ToBytes(
                    Path.Combine(PersistentRoot, "map_editor_zones.json"),
                    "persistent/map_editor_zones.json");
                totalBytes += ToBytes(
                    Path.Combine(PersistentRoot, "map_editor_zones.json.bak"),
                    "persistent/map_editor_zones.json.bak");

                string mapsDir = Path.Combine(PersistentRoot, "Maps");
                totalBytes += ToBytes(
                    Path.Combine(mapsDir, sanitized + ".zones.json"),
                    $"persistent/Maps/{sanitized}.zones.json");
                totalBytes += ToBytes(
                    Path.Combine(mapsDir, "_active.txt"),
                    "persistent/Maps/_active.txt");

                // Tile overrides — every overlay file. They are not yet routed
                // per-slot (see MAP_EDITOR_MULTIMAP_ROADMAP), so capture all.
                string overridesDir = Path.Combine(PersistentRoot, "MapOverrides");
                if (Directory.Exists(overridesDir))
                {
                    foreach (var f in Directory.GetFiles(overridesDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        totalBytes += ToBytes(f, $"persistent/MapOverrides/{Path.GetFileName(f)}");
                    }
                }

                // ── StreamingAssets world content ──
                CopyDir(stagingDir, "streaming/Buildings", Path.Combine(StreamingRoot, "Buildings"),
                        copied, ref totalBytes, "*.json");
                CopyDir(stagingDir, "streaming/Spawners", Path.Combine(StreamingRoot, "Spawners"),
                        copied, ref totalBytes, "*.json");
                CopyDir(stagingDir, "streaming/Lights", Path.Combine(StreamingRoot, "Lights"),
                        copied, ref totalBytes, "*.json");
                CopyDir(stagingDir, "streaming/Particles", Path.Combine(StreamingRoot, "Particles"),
                        copied, ref totalBytes, "*.json");

                var manifest = new MapBackupManifest
                {
                    id                 = id,
                    slot               = sanitized,
                    createdLocalIso    = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    createdUnixSeconds = DateTimeOffset.Now.ToUnixTimeSeconds(),
                    kind               = string.IsNullOrEmpty(kind) ? MapBackupSchema.KindManual : kind,
                    label              = string.IsNullOrEmpty(label) ? "Snapshot" : label,
                    totalBytes         = totalBytes,
                    fileCount          = copied.Count,
                    files              = copied,
                };
                File.WriteAllText(
                    Path.Combine(stagingDir, MapBackupSchema.ManifestFileName),
                    JsonUtility.ToJson(manifest, prettyPrint: true));

                if (Directory.Exists(finalDir)) Directory.Delete(finalDir, recursive: true);
                Directory.Move(stagingDir, finalDir);
                Debug.Log($"[MapBackup] Created snapshot '{id}' ({manifest.fileCount} files, {FormatBytes(totalBytes)}).");
                return manifest;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapBackup] Snapshot failed for slot '{slot}': {ex.Message}");
                try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); }
                catch { /* swallow */ }
                return null;
            }
        }

        private static void CopyDir(string stagingDir, string relRoot, string srcDir,
                                    List<string> copied, ref long totalBytes, string searchPattern)
        {
            if (!Directory.Exists(srcDir)) return;
            foreach (var f in Directory.GetFiles(srcDir, searchPattern, SearchOption.TopDirectoryOnly))
            {
                string rel = $"{relRoot}/{Path.GetFileName(f)}";
                string dst = Path.Combine(stagingDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? stagingDir);
                File.Copy(f, dst, overwrite: true);
                totalBytes += new FileInfo(dst).Length;
                copied.Add(rel);
            }
        }

        // ── Delete ──────────────────────────────────────────────────────────────

        public bool DeleteBackup(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string dir = Path.Combine(Root, id);
            if (!Directory.Exists(dir)) return false;
            try
            {
                Directory.Delete(dir, recursive: true);
                Debug.Log($"[MapBackup] Deleted snapshot '{id}'.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapBackup] Failed to delete '{id}': {ex.Message}");
                return false;
            }
        }

        // ── Restore ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Restores a snapshot by copying every file in its manifest back to
        /// its original location. Caller is responsible for triggering a Map
        /// Editor reload afterwards (RefreshTilemapForActiveSlot,
        /// ReloadAllWorldContent, etc.) — this method is pure file IO.
        /// </summary>
        public bool RestoreBackup(string id)
        {
            var manifest = LoadManifest(id);
            if (manifest == null) return false;
            string dir = Path.Combine(Root, id);

            int restored = 0;
            try
            {
                foreach (var rel in manifest.files)
                {
                    string src = Path.Combine(dir, rel);
                    if (!File.Exists(src)) continue;
                    string dst = MapBackupRelToOriginalPath(rel);
                    if (string.IsNullOrEmpty(dst)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? string.Empty);
                    File.Copy(src, dst, overwrite: true);
                    restored++;
                }
                Debug.Log($"[MapBackup] Restored {restored}/{manifest.files.Count} files from '{id}'.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapBackup] Restore '{id}' failed: {ex.Message}");
                return false;
            }
        }

        private string MapBackupRelToOriginalPath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return null;
            rel = rel.Replace('\\', '/');
            if (rel.StartsWith("persistent/", StringComparison.Ordinal))
                return Path.Combine(PersistentRoot, rel.Substring("persistent/".Length).Replace('/', Path.DirectorySeparatorChar));
            if (rel.StartsWith("streaming/", StringComparison.Ordinal))
                return Path.Combine(StreamingRoot, rel.Substring("streaming/".Length).Replace('/', Path.DirectorySeparatorChar));
            return null;
        }

        // ── Misc helpers ────────────────────────────────────────────────────────

        public static string SanitizeSlot(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "default";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var ch in raw.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                    sb.Append(ch);
                else if (ch == ' ')
                    sb.Append('_');
            }
            string s = sb.ToString();
            return string.IsNullOrEmpty(s) ? "default" : s;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)            return $"{bytes} B";
            if (bytes < 1024L * 1024)    return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
