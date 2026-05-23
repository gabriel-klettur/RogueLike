using System;
using UnityEngine;

namespace Valkur.Gameplay.MapEditor.Backups
{
    /// <summary>
    /// Idle-time + lifecycle backup scheduler for the Map Editor. Lives as a
    /// child component of <see cref="MapEditorManager"/>; reads the active
    /// slot through callbacks supplied by the manager and pushes snapshots
    /// via <see cref="MapBackupStore.CreateSnapshot"/>.
    ///
    /// ── Coverage strategy ─────────────────────────────────────────────────
    /// Existing event triggers cover destructive operations (BeginNewMap,
    /// DeleteMapSlot, DeleteZone, biome regen). This scheduler fills the
    /// remaining gap: a session of many small edits never triggers an event
    /// snapshot but still ends with significant work at risk. So:
    ///
    ///   • <b>Dirty-marking</b>: any zone CRUD or biome op marks the
    ///     scheduler dirty via <see cref="MarkDirty"/>. The manager wires
    ///     this from its <c>OnZonesChanged</c> event + biome generation.
    ///   • <b>Idle tick</b>: <see cref="MinIdleInterval"/> after the last
    ///     dirty-mark AND <see cref="MinSnapshotInterval"/> since the last
    ///     snapshot, fire an auto snapshot. The double gate prevents two
    ///     classes of waste: snapshots in the middle of a rapid edit burst,
    ///     and snapshots stacking too tightly across burst boundaries.
    ///   • <b>Quit / focus-loss</b>: <see cref="OnApplicationQuit"/> and
    ///     <see cref="OnApplicationPause"/> flush a snapshot if dirty
    ///     regardless of timers. Captures the "user just closed the game"
    ///     state without relying on the next idle tick.
    ///
    /// All snapshot calls are wrapped in try/catch — a failed backup never
    /// blocks the user from continuing to work.
    /// </summary>
    public sealed class MapBackupScheduler : MonoBehaviour
    {
        /// <summary>Idle window after the last dirty-mark before a tick can fire.</summary>
        public const float MinIdleInterval = 90f;  // 1.5 min
        /// <summary>Lower bound between two auto snapshots, even across edit bursts.</summary>
        public const float MinSnapshotInterval = 900f;  // 15 min
        /// <summary>Polling cadence — cheap; the gates above decide when to fire.</summary>
        private const float TickInterval = 30f;

        private MapBackupStore _store;
        private Func<string> _getActiveSlot;
        private bool _dirty;
        private float _lastDirtyTime;
        private float _lastSnapshotTime;
        private float _nextPollTime;

        /// <summary>
        /// Wires the scheduler to the live <see cref="MapBackupStore"/> and
        /// the slot resolver. Must be called once after <c>AddComponent</c>;
        /// before this runs the scheduler is inert (no ticks, no snapshots).
        /// </summary>
        public void Configure(MapBackupStore store, Func<string> getActiveSlot)
        {
            _store = store;
            _getActiveSlot = getActiveSlot;
            // Reset the snapshot timer to "just snapshotted" so a freshly-
            // booted session waits a full interval before its first auto-
            // snap. Otherwise the very first dirty-mark after start would
            // satisfy both gates and snapshot too eagerly.
            _lastSnapshotTime = Time.realtimeSinceStartup;
            _nextPollTime     = Time.realtimeSinceStartup + TickInterval;
        }

        /// <summary>
        /// Called by <see cref="MapEditorManager"/> whenever the live zone
        /// state changes (any add / rename / move / toggle / delete / biome
        /// op). Multiple marks inside the same idle window collapse into one
        /// pending snapshot — cheap.
        /// </summary>
        public void MarkDirty()
        {
            _dirty = true;
            _lastDirtyTime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (_store == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextPollTime) return;
            _nextPollTime = now + TickInterval;
            TryTakeIdleSnapshot(now);
        }

        private void TryTakeIdleSnapshot(float now)
        {
            if (!_dirty) return;
            if (now - _lastDirtyTime < MinIdleInterval) return;
            if (now - _lastSnapshotTime < MinSnapshotInterval) return;
            CommitSnapshot(MapBackupSchema.KindAutoIdle, "Auto idle snapshot");
        }

        /// <summary>
        /// Forces an immediate snapshot regardless of timers, used by the
        /// quit / focus-loss hooks below. No-op when nothing is dirty so we
        /// don't pile up empty-state backups every time the user alt-tabs.
        /// </summary>
        public void FlushIfDirty(string kind, string label)
        {
            if (!_dirty) return;
            if (_store == null) return;
            CommitSnapshot(kind, label);
        }

        private void CommitSnapshot(string kind, string label)
        {
            string slot = (_getActiveSlot != null ? _getActiveSlot() : null) ?? "default";
            try
            {
                var manifest = _store.CreateSnapshot(slot, label, kind);
                if (manifest != null)
                {
                    _dirty = false;
                    _lastSnapshotTime = Time.realtimeSinceStartup;
                    Debug.Log($"[MapBackupScheduler] Auto snapshot created: " +
                              $"slot='{slot}', kind='{kind}', files={manifest.fileCount}.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapBackupScheduler] Snapshot failed: {ex.Message}");
            }
        }

        // Unity lifecycle hooks — best-effort capture of the final state when
        // the user steps away or quits. OnApplicationFocus(false) fires on
        // alt-tab; OnApplicationPause(true) on minimise. Both are common
        // "user is about to lose interest" signals and worth a snapshot.
        private void OnApplicationQuit()
            => FlushIfDirty(MapBackupSchema.KindAutoQuit, "Auto quit snapshot");

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                FlushIfDirty(MapBackupSchema.KindAutoFocusLoss, "Auto focus-loss snapshot");
        }
    }
}
