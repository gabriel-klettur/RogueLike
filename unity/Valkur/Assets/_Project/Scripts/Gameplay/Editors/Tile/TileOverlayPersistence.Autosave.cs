using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Deferred, debounced, background-thread autosave for
    /// <see cref="TileOverlayPersistence"/>.
    /// <para>
    /// Two distinct save paths coexist by design:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Immediate / guaranteed</b> — <see cref="TileOverlayPersistence.SaveAllDirty"/>
    /// and <see cref="TileOverlayPersistence.SaveZone"/>. Unconditionally synchronous:
    /// the file is fully written before the call returns. Used by the Save
    /// button, the Map Editor slot switch, closing the Tile Editor (F8), and
    /// every EditMode test — none of those can tolerate a delayed write.</item>
    /// <item><b>Deferred / automatic</b> — armed by <c>MarkCellDirty</c> /
    /// <c>MarkBatchDirty</c> whenever <c>Application.isPlaying</c> is true.
    /// After <see cref="TileEditorConstants.AutosaveDebounceSeconds"/> of quiet
    /// (no further edits), it captures a snapshot of the dirty zones on the
    /// MAIN thread (Tilemap API isn't thread-safe), then serializes the JSON
    /// and writes it to disk on a background <see cref="Task"/>. A burst of
    /// several quick strokes that each mark cells dirty without ALSO forcing
    /// an immediate flush right after coalesces into a single background
    /// write instead of one per stroke.</item>
    /// </list>
    /// <para>
    /// The two paths are kept safe together by <see cref="WaitForInFlightAutosave"/>,
    /// which every immediate-flush entry point calls first: if a deferred
    /// write is currently running, it blocks until that write's bytes are
    /// confirmed on disk before doing anything else. This is the "el diferido
    /// se fuerza a completarse antes de cualquier inmediato" requirement —
    /// ordering is never left to chance.
    /// </para>
    /// <para>
    /// <b>Why <see cref="TileOverlayPersistence.SaveAllDirty"/> itself is never
    /// made asynchronous:</b> it is called from the exact same call site
    /// pattern (<c>_persistence?.SaveAllDirty()</c>) by both the per-stroke
    /// auto-save handlers AND the two explicit-save call sites (Save button,
    /// slot-switch flush) in <c>TileEditorManager.cs</c> — there is no way to
    /// tell those callers apart from inside this class, and several EditMode
    /// tests call it directly and assert <c>File.Exists(...)</c> immediately
    /// after with zero player-loop ticks in between. Weakening its contract
    /// would either break those tests or risk losing an author's edits on
    /// close. So today, with those call sites unchanged, every one of them
    /// still pays the full synchronous cost on every call — this pump's
    /// coalescing only takes effect for a caller that marks cells dirty
    /// WITHOUT also calling <c>SaveAllDirty()</c> immediately after (which is
    /// not how the current 11 auto-save call sites are wired). The
    /// infrastructure is fully wired and ready to absorb that call-site
    /// simplification the moment it happens.
    /// </para>
    /// </summary>
    public partial class TileOverlayPersistence
    {
        // ── Per-instance debounce state (main-thread only — Tilemap API and
        //    Application.isPlaying/realtimeSinceStartup are main-thread-only). ──
        private float _lastAutosaveEditRealtime;
        private bool _autosaveLoopRunning;

        /// <summary>
        /// The background <see cref="Task"/> currently serializing + writing a
        /// deferred flush, or null when none is in flight. Set (main thread)
        /// before the background work starts, read by <see cref="WaitForInFlightAutosave"/>
        /// from any immediate-flush call site, cleared (main thread, via the
        /// async continuation) once the write completes.
        /// </summary>
        private Task _inFlightAutosaveTask;

        // ── Cross-instance lifecycle safety (Domain Reload is OFF) ──────────
        //
        // TileEditorManager.RebindToWorld constructs a NEW TileOverlayPersistence
        // on every map-slot switch without disposing the old one, so "the
        // instance" a hard-quit needs to flush isn't singular. Track every live
        // instance via a WeakReference (never prevents GC of an abandoned one)
        // and flush whichever are still alive when the process is about to go
        // away. The static registry + hook-installed flag are reset once per
        // Play session via SubsystemRegistration — required because Domain
        // Reload is off, so plain static fields would otherwise carry stale
        // state (and duplicate event subscriptions) across Play sessions.

        private static readonly List<WeakReference<TileOverlayPersistence>> _liveAutosaveInstances =
            new List<WeakReference<TileOverlayPersistence>>();
        private static bool _autosaveHooksInstalled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAutosaveStaticState()
        {
            _liveAutosaveInstances.Clear();
            _autosaveHooksInstalled = false;
        }

        private static void EnsureAutosaveLifecycleHooksInstalled()
        {
            if (_autosaveHooksInstalled) return;
            _autosaveHooksInstalled = true;

            // Idempotent subscribe: unsubscribe first so a stray leftover
            // subscription from a prior Play session (possible only if this
            // guard itself were ever bypassed) can never double-fire.
            Application.quitting -= FlushAllLiveAutosaveInstances;
            Application.quitting += FlushAllLiveAutosaveInstances;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChangedStatic;
            UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeStateChangedStatic;
#endif
        }

#if UNITY_EDITOR
        private static void OnEditorPlayModeStateChangedStatic(UnityEditor.PlayModeStateChange change)
        {
            // Exiting Play covers "salir de Play" from the invariant — this is
            // the one explicit-save scenario NOT already wired through an
            // existing SaveAllDirty() call site (TileEditorManager.OnDestroy
            // does not call it), so it needs its own hook.
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                FlushAllLiveAutosaveInstances();
        }
#endif

        private static void FlushAllLiveAutosaveInstances()
        {
            for (int i = _liveAutosaveInstances.Count - 1; i >= 0; i--)
            {
                if (_liveAutosaveInstances[i].TryGetTarget(out var instance) && instance != null)
                {
                    try { instance.SaveAllDirty(); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[TileOverlayPersistence] Force-flush on teardown failed: {ex}");
                    }
                }
                else
                {
                    _liveAutosaveInstances.RemoveAt(i); // GC'd — prune.
                }
            }
        }

        /// <summary>Called once from every constructor overload.</summary>
        private void RegisterForAutosaveLifecycleTracking()
        {
            // Opportunistic prune: EditMode tests construct a fresh instance per
            // test and never invoke FlushAllLiveAutosaveInstances (which is the
            // only other place this list shrinks), so a long test session would
            // otherwise accumulate one dead WeakReference per test forever.
            for (int i = _liveAutosaveInstances.Count - 1; i >= 0; i--)
            {
                if (!_liveAutosaveInstances[i].TryGetTarget(out _))
                    _liveAutosaveInstances.RemoveAt(i);
            }

            _liveAutosaveInstances.Add(new WeakReference<TileOverlayPersistence>(this));
            EnsureAutosaveLifecycleHooksInstalled();
        }

        // ── Debounce arm (called from MarkCellDirty / MarkBatchDirty) ───────

        /// <summary>
        /// Refresh the "last edit" timestamp and, if no debounce loop is
        /// already counting down, start one. No-op outside Play mode so
        /// EditMode tests never observe any background activity from this
        /// class — <c>Application.isPlaying</c> is always false there.
        /// </summary>
        private void ArmAutosaveTimer()
        {
            if (!Application.isPlaying) return;

            _lastAutosaveEditRealtime = Time.realtimeSinceStartup;
            if (_autosaveLoopRunning) return; // already counting down / flushing; it will re-check.

            _autosaveLoopRunning = true;
            _ = DebounceLoopAsync();
        }

        /// <summary>
        /// Waits out the quiet period, flushes, and — if further edits landed
        /// while the flush was writing in the background — loops back around
        /// and debounces those too. Never runs two flushes concurrently: the
        /// next cycle only starts after the previous one's background write
        /// has fully completed.
        /// </summary>
        private async Task DebounceLoopAsync()
        {
            try
            {
                while (true)
                {
                    while (Application.isPlaying)
                    {
                        float remaining = TileEditorConstants.AutosaveDebounceSeconds -
                                          (Time.realtimeSinceStartup - _lastAutosaveEditRealtime);
                        if (remaining <= 0f) break;
                        await Task.Delay(TimeSpan.FromSeconds(remaining));
                    }
                    if (!Application.isPlaying) break; // Play stopped mid-wait — the exit hook already flushed.

                    float editTimeBeforeFlush = _lastAutosaveEditRealtime;
                    await PerformDeferredFlushAsync();

                    // Nothing new arrived while we were writing → done. If new
                    // edits DID land (marked dirty during the write), they
                    // never got their own loop spawned (_autosaveLoopRunning
                    // was still true) — loop back and debounce them too.
                    if (editTimeBeforeFlush == _lastAutosaveEditRealtime) break;
                }
            }
            catch (Exception ex)
            {
                // This runs fire-and-forget (`_ = DebounceLoopAsync();`) — an
                // unhandled exception here would become an unobserved-task
                // fault instead of a normal console error. PerformDeferredFlushAsync
                // already catches every per-zone failure internally, so reaching
                // here means something outside that (e.g. the snapshot capture
                // itself) — surface it instead of losing it silently.
                Debug.LogError($"[TileOverlayPersistence] Deferred autosave loop aborted: {ex}");
            }
            finally
            {
                _autosaveLoopRunning = false;
            }
        }

        /// <summary>
        /// The actual deferred flush: capture every dirty zone's data on the
        /// main thread (Tilemap reads), then hand the pure-data snapshot off
        /// to a background <see cref="Task"/> for JSON serialization + disk
        /// write — both of which touch no Unity API and are safe off-thread.
        /// </summary>
        private async Task PerformDeferredFlushAsync()
        {
            if (_zones == null || _grid == null) return;
            if (_dirtyZones.Count == 0) return;

            // ── Main thread: snapshot capture only. ─────────────────────────
            var zoneNames = new List<string>(_dirtyZones);
            var pending = new List<(string zoneName, ZoneSnapshot snapshot)>(zoneNames.Count);
            for (int i = 0; i < zoneNames.Count; i++)
            {
                if (_zones.TryGetZone(zoneNames[i], out var zone))
                    pending.Add((zoneNames[i], CaptureZoneSnapshot(zone)));
            }

            // Optimistic clear, matching SaveAllDirty's own semantics (it also
            // clears every zone it attempted, not just the ones that succeeded).
            // Safe because WaitForInFlightAutosave() makes every immediate-flush
            // entry point block until the task below has actually written the
            // bytes before anyone can observe HasUnsavedChanges == false and
            // trust it.
            _dirtyZones.Clear();
            OnDirtyChanged?.Invoke();

            // ── Background thread: pure C# (StringBuilder) + pure .NET IO. ──
            // SerializeOverlay touches no Unity API. JsonFileTileOverrideRepository.Write
            // caches Application.persistentDataPath in its constructor (main
            // thread, at TileOverlayPersistence construction time) and never
            // re-reads it per call, so it's safe to call from here too.
            var writeTask = Task.Run(() =>
            {
                var results = new List<(string zoneName, bool ok, Exception error)>(pending.Count);
                for (int i = 0; i < pending.Count; i++)
                {
                    string zoneName = pending[i].zoneName;
                    try
                    {
                        var snap = pending[i].snapshot;
                        string json = SerializeOverlay(snap.PerLayer, snap.TerrainMatrix,
                            snap.CollisionTagMatrix, snap.LayerJumpsMatrix, snap.Width, snap.Height);
                        _repository.Write(_worldId, zoneName, json);
                        results.Add((zoneName, true, null));
                    }
                    catch (Exception ex)
                    {
                        results.Add((zoneName, false, ex));
                    }
                }
                return results;
            });

            _inFlightAutosaveTask = writeTask;
            List<(string zoneName, bool ok, Exception error)> results;
            try
            {
                results = await writeTask;
            }
            finally
            {
                _inFlightAutosaveTask = null;
            }

            // ── Back on the main thread: fire the same events SaveAllDirty does. ──
            for (int i = 0; i < results.Count; i++)
            {
                var (zoneName, ok, error) = results[i];
                if (ok)
                {
                    Debug.Log($"[TileOverlayPersistence] Auto-saved zone '{zoneName}' (debounced, background thread).");
                    OnZoneSaved?.Invoke(zoneName);
                }
                else
                {
                    Debug.LogError($"[TileOverlayPersistence] Deferred auto-save failed for zone '{zoneName}': {error}");
                    OnSaveFailed?.Invoke(zoneName, error);
                }
            }
        }

        /// <summary>
        /// Blocks the calling (main) thread until any currently in-flight
        /// deferred background write has actually finished. Waits on the raw
        /// <see cref="Task.Run"/> task, not on the wrapping async method —
        /// that task completes purely on the ThreadPool and needs no main-
        /// thread pump to finish, so this cannot deadlock against Unity's
        /// SynchronizationContext even though it's called from the main
        /// thread. Called first by every immediate-flush entry point.
        /// </summary>
        private void WaitForInFlightAutosave()
        {
            var task = _inFlightAutosaveTask;
            if (task == null) return;
            try { task.Wait(); }
            catch (AggregateException)
            {
                // Per-zone failures are already reported via OnSaveFailed
                // inside PerformDeferredFlushAsync — nothing further to do.
            }
        }
    }
}
