using System;
using System.Collections.Generic;

namespace Valkur.Gameplay.MapEditor.Backups
{
    /// <summary>
    /// On-disk metadata sidecar that lives next to the snapshotted JSON files.
    /// Lets the backup browser show timestamp + slot + size without scanning
    /// the whole tree, and identifies which files belong to a snapshot when
    /// time comes to restore or delete.
    /// </summary>
    [Serializable]
    public class MapBackupManifest
    {
        public string schemaVersion = MapBackupSchema.CurrentVersion;

        // Stable id == directory name == "<slot>_<yyyyMMdd_HHmmss>".
        public string id;

        public string slot;

        // ISO 8601 with offset, e.g. "2026-05-06T10:54:46+02:00".
        public string createdLocalIso;

        // Unix seconds for sort/compare (the ISO field is only for display).
        public long createdUnixSeconds;

        // "Manual", "AutoBeforeDelete", "AutoBeforeNew" so the UI can label
        // automatic snapshots differently from explicit ones.
        public string kind = "Manual";

        // Human-readable note shown in the browser.
        public string label = "Snapshot";

        public long totalBytes;
        public int  fileCount;

        // Relative paths inside the snapshot directory (e.g. "persistent/Maps/default.zones.json").
        public List<string> files = new List<string>();
    }

    public static class MapBackupSchema
    {
        public const string CurrentVersion = "1.0";

        public const string ManifestFileName = "manifest.json";

        public const string KindManual            = "Manual";
        public const string KindAutoBeforeDelete  = "AutoBeforeDelete";
        public const string KindAutoBeforeNew     = "AutoBeforeNew";
        public const string KindAutoBeforeRestore = "AutoBeforeRestore";
    }
}
