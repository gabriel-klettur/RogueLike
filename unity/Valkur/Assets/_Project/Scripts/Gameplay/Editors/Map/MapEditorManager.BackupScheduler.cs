using UnityEngine;
using Valkur.Gameplay.MapEditor.Backups;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// F11-side callbacks for the backup browser + manual-snapshot buttons
    /// that live in the MAPS panel. Implementations are kept here so the
    /// rest of MapEditorManager doesn't have to know about the browser UI
    /// type.
    /// </summary>
    /// <summary>
    /// Wires <see cref="MapBackupScheduler"/> into the Map Editor lifecycle.
    /// Created once in <see cref="Start"/> (alongside the rest of the
    /// runtime), subscribes to <see cref="Valkur.Gameplay.World.ZoneManager.OnZonesChanged"/>
    /// so any zone CRUD marks the scheduler dirty, and exposes a
    /// <see cref="CreateManualSnapshot"/> API the F11 toolbar button calls.
    /// Snapshot creation routes through the same <see cref="MapBackupStore"/>
    /// as the existing event-triggered <c>TryAutoSnapshot</c> helper, so
    /// every code path lands in the same backups directory and shows up in
    /// the browser UI.
    /// </summary>
    public partial class MapEditorManager
    {
        private MapBackupScheduler _backupScheduler;

        /// <summary>
        /// Result of a manual snapshot request from the UI. Lets the caller
        /// surface success / failure to the user without round-tripping
        /// through the manifest object directly.
        /// </summary>
        public struct ManualSnapshotResult
        {
            public bool Success;
            public string Label;
            public int FileCount;
            public string Error;
        }

        private void EnsureBackupScheduler()
        {
            if (_backupScheduler != null) return;
            var go = new GameObject("MapBackupScheduler");
            go.transform.SetParent(transform, false);
            _backupScheduler = go.AddComponent<MapBackupScheduler>();
            _backupScheduler.Configure(BackupStore, () => ActiveMapSlot);
        }

        private void HandleZonesChangedForBackup()
        {
            // Cheap: just bumps the dirty flag + timestamp. The scheduler
            // decides whether to snapshot based on its idle + interval gates.
            _backupScheduler?.MarkDirty();
        }

        // ── F11 toolbar callbacks ──────────────────────────────────────────

        /// <summary>
        /// Spawn the backup browser from inside F11 without forcing the user
        /// to close the Map Editor and re-route through General Editor.
        /// The browser closes back to gameplay on its own (no special wiring
        /// needed — F11 is still active under it).
        /// </summary>
        private void OnOpenBackupBrowserFromF11()
        {
            var browser = Valkur.Gameplay.MapEditor.Backups.MapBackupBrowserUI.Open();
            if (browser == null)
            {
                _ui?.SetStatus("Could not open the backups browser.");
                return;
            }
            _ui?.SetStatus("Backups browser open — ESC to close.");
        }

        /// <summary>
        /// F11 "Snapshot now" callback. Returns a status string the UI shows
        /// in the status bar — empty / null is treated as silent success.
        /// </summary>
        private string OnCreateBackupNowFromF11()
        {
            var result = CreateManualSnapshot();
            if (result.Success)
            {
                string status = $"Snapshot saved ({result.FileCount} file(s)).";
                _ui?.SetStatus(status);
                return status;
            }
            string failMsg = string.IsNullOrEmpty(result.Error)
                ? "Snapshot failed (see console)."
                : $"Snapshot failed: {result.Error}";
            _ui?.SetStatus(failMsg);
            return failMsg;
        }

        /// <summary>
        /// Public entrypoint for the F11 toolbar's "Snapshot now" button.
        /// Creates a manual backup of the currently-active slot using the
        /// supplied label (falls back to a sensible default when empty).
        /// Returns a result struct the UI can read for status feedback.
        /// </summary>
        public ManualSnapshotResult CreateManualSnapshot(string label = null)
        {
            string finalLabel = string.IsNullOrWhiteSpace(label)
                ? "Manual snapshot from F11"
                : label.Trim();
            try
            {
                var manifest = BackupStore.CreateSnapshot(
                    ActiveMapSlot, finalLabel, MapBackupSchema.KindManual);
                if (manifest == null)
                {
                    return new ManualSnapshotResult
                    {
                        Success = false,
                        Label   = finalLabel,
                        Error   = "Snapshot returned null — check console for details.",
                    };
                }
                // Manual snapshot also resets the scheduler's dirty / idle
                // state so an auto-snap doesn't fire seconds later on top
                // of this one for the same edit burst.
                _backupScheduler?.FlushIfDirty(
                    MapBackupSchema.KindManual, finalLabel);
                return new ManualSnapshotResult
                {
                    Success   = true,
                    Label     = finalLabel,
                    FileCount = manifest.fileCount,
                };
            }
            catch (System.Exception ex)
            {
                return new ManualSnapshotResult
                {
                    Success = false,
                    Label   = finalLabel,
                    Error   = ex.Message,
                };
            }
        }
    }
}
