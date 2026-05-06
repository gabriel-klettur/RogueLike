using System;
using System.Collections.Generic;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Single recorded save event — what kind of save fired, what triggered
    /// it, how long the disk write took, the file size on disk, and whether
    /// it succeeded. The diagnostic HUD reads this buffer so the player /
    /// developer can see the save log without grepping the Unity console.
    /// </summary>
    public readonly struct SaveTelemetryEntry
    {
        public enum SaveKind
        {
            Autosave,        // periodic timer fired
            DebounceFlush,   // dirty-debounce settled and triggered a write
            QuickSave,       // user-driven explicit save
            Immediate,       // SaveImmediately(reason) — milestone events
            ManualNamed,     // Save("named slot") via UI
            QuitFlush        // OnApplicationQuit / OnApplicationPause
        }

        public readonly DateTime Timestamp;
        public readonly SaveKind Kind;
        public readonly string   Reason;
        public readonly bool     Success;
        public readonly long     SizeBytes;
        public readonly double   DurationMs;
        public readonly string   Path;
        public readonly bool     WasAsync;

        public SaveTelemetryEntry(SaveKind kind, string reason, bool success,
                                  long sizeBytes, double durationMs,
                                  string path, bool wasAsync)
        {
            Timestamp  = DateTime.Now;
            Kind       = kind;
            Reason     = reason ?? string.Empty;
            Success    = success;
            SizeBytes  = sizeBytes;
            DurationMs = durationMs;
            Path       = path ?? string.Empty;
            WasAsync   = wasAsync;
        }
    }

    /// <summary>
    /// In-memory ring buffer of the most recent save attempts. Static so any
    /// caller (the diagnostic HUD, tests, post-mortem dumps) can inspect it
    /// without holding a reference to the SaveService instance. Capacity is
    /// fixed at <see cref="Capacity"/> entries — older records drop off.
    /// </summary>
    public static class SaveTelemetry
    {
        public const int Capacity = 64;

        private static readonly Queue<SaveTelemetryEntry> _entries = new Queue<SaveTelemetryEntry>(Capacity);
        private static readonly object _lock = new object();

        /// <summary>
        /// Fires whenever <see cref="Record"/> appends a new entry. The HUD
        /// subscribes to this so it can refresh without polling every frame.
        /// </summary>
        public static event Action<SaveTelemetryEntry> OnEntryRecorded;

        /// <summary>Total entries recorded since process start (not bounded by capacity).</summary>
        public static int TotalRecorded { get; private set; }

        /// <summary>
        /// Snapshot of the current ring buffer, oldest → newest. Returns a
        /// copy so the caller can iterate safely while new entries are being
        /// recorded on another thread.
        /// </summary>
        public static List<SaveTelemetryEntry> Snapshot()
        {
            lock (_lock) { return new List<SaveTelemetryEntry>(_entries); }
        }

        public static void Record(SaveTelemetryEntry entry)
        {
            lock (_lock)
            {
                if (_entries.Count >= Capacity) _entries.Dequeue();
                _entries.Enqueue(entry);
                TotalRecorded++;
            }
            // Fire the event OUTSIDE the lock so subscribers can take their
            // own time without blocking the next save.
            try { OnEntryRecorded?.Invoke(entry); }
            catch { /* swallow — telemetry must not break the save path */ }
        }

        /// <summary>Test-only reset. Clears the buffer and the lifetime counter.</summary>
        public static void ResetForTests()
        {
            lock (_lock)
            {
                _entries.Clear();
                TotalRecorded = 0;
            }
        }

        /// <summary>
        /// Clears all subscribers from <see cref="OnEntryRecorded"/>. Called
        /// from <see cref="SaveTelemetryHUD"/> via
        /// <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c> so
        /// that stale HUD delegates from a previous Play session (Domain Reload
        /// OFF) don't accumulate and fire against destroyed objects.
        /// </summary>
        internal static void ClearEntryRecordedListeners() => OnEntryRecorded = null;
    }
}
