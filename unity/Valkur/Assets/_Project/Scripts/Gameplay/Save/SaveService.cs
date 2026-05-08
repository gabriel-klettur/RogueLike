using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Save;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Thin MonoBehaviour coordinator for the save system.
    /// Delegates IO to SaveFileManager, state collection to GameStateCollector,
    /// state restoration to GameStateRestorer, and migration to SaveSchemaMigrator.
    /// </summary>
    public partial class SaveService : SingletonMonoBehaviour<SaveService>
    {
        // [RunTwinSave-Diag] When true, every mutation of _currentRunId /
        // _currentRunOrdinal logs an entry tagged "[RunTwinSave-Diag]". Intent:
        // capture the call chain that produces a duplicate Saves/<runId>/ folder
        // with identical body but different meta.run_id (incident
        // .github/incidents/RUN_TWIN_SAVE.md). Root cause was identified as
        // EditMode test pollution; the production guard `RefuseWriteOutsidePlayMode`
        // now blocks recurrence so the diag is dormant. Flip back to true if a
        // recurrence ever shows up in a real player profile.
        private const bool DIAG_RUN_TWIN_SAVE = false;

        // All auto-save semantics (timer autosave, shutdown save, quicksave-on-exit)
        // collapse to the same per-run autosave file via SaveFileManager.GetAutosavePath.
        // Manual saves use SaveFileManager.GetManualSavePath inside the same run folder.

        [Header("Autosave")]
        [SerializeField] private bool autosaveEnabled = true;
        // 45 seconds is the production default for sandbox-style worlds:
        // long enough that combat ticks don't thrash the disk, short enough
        // that an unexpected crash never costs more than ~45 s of progression.
        // Critical milestones (boss kill, quest completed, zone change, level
        // up, player death) ALSO call SaveImmediately so this timer is the
        // safety floor, not the primary save trigger.
        [SerializeField] private float autosaveIntervalSeconds = 45f;

        [Header("Position Checkpoint")]
        [Tooltip("Write a lightweight position-only file this often. Protects position against crashes.")]
        [SerializeField] private bool positionCheckpointEnabled = true;
        [SerializeField] private float positionCheckpointIntervalSeconds = 10f;

        private float _autosaveTimer;
        private float _positionCheckpointTimer;
        private string _currentSavePath;
        private string _lastLoadedTimestamp;
        private string _currentRunId = "";
        // Monotonic per-profile run ordinal (1, 2, 3, …) — minted by
        // ProfileTelemetrySystem at run start and propagated here via
        // SetRunOrdinal so every save written by this service carries it
        // in meta.run_ordinal. Zero = "not assigned yet" (e.g. between
        // BeginNewRun and the bootstrap's StartTelemetryRun call).
        private int _currentRunOrdinal;

        // Debounce window after a MarkDirty so a rapid burst of dirty events
        // (combat sequence, mass loot pickup) coalesces into a single save
        // instead of spamming the disk. The autosave timer remains the long-
        // term safety net; this window is the short-term coalescer.
        [SerializeField] private float dirtyDebounceSeconds = 2f;
        private float _dirtyDebounceTimer = -1f;
        private bool  _dirtyDebouncePending;

        // Phase 2 — async I/O. The autosave write is serialized to JSON on
        // the main thread (JsonUtility is not thread-safe) and the actual
        // disk I/O runs on the .NET thread pool. _pendingWrite chains every
        // new write off the previous one so writes never interleave on disk
        // even under rapid SaveImmediately bursts. OnApplicationQuit /
        // OnApplicationPause / Load wait on this task to guarantee the last
        // bytes reach the disk before Unity tears the service down.
        [Tooltip("Disable to fall back to synchronous disk writes (e.g. for tests).")]
        [SerializeField] private bool useAsyncDiskIO = true;
        [Tooltip("Hard cap on how long OnApplicationQuit waits for a pending write.")]
        [SerializeField] private float quitWaitSeconds = 5f;

        public string RunId => _currentRunId;
        /// <summary>
        /// Per-profile monotonic run number (1, 2, 3, …). Equals 0 between
        /// BeginNewRun and the telemetry-side StartRun call that mints the
        /// next ordinal — see <see cref="SetRunOrdinal"/>.
        /// </summary>
        public int RunOrdinal => _currentRunOrdinal;

        /// <summary>
        /// Wires the run ordinal into the save layer. Called by
        /// <c>ProfileTelemetrySystem.StartRun</c> immediately after the
        /// per-profile counter is bumped (or after a save load adopts an
        /// existing ordinal). Once set, every save written by this service
        /// embeds <c>run_ordinal</c> in its meta block so the Load Game panel
        /// can render "Run #N" without consulting profile.json at list time.
        /// </summary>
        public void SetRunOrdinal(int ordinal)
        {
            if (ordinal < 0) ordinal = 0;
            int prev = _currentRunOrdinal;
            _currentRunOrdinal = ordinal;
            if (DIAG_RUN_TWIN_SAVE && prev != ordinal)
                LogRunTwinSaveDiag($"SetRunOrdinal {prev} -> {ordinal} (runId={_currentRunId})");
        }

        // Cached as plain C# values so OnApplicationQuit can read them
        // without touching any Unity Object reference during teardown.
        private Vector2 _lastKnownPlayerPos;
        private string  _lastKnownPlayerZone = "";
        private int     _lastKnownPlayerHp   = -1;   // -1 = not yet sampled
        private bool    _hasKnownPlayerPos;

        // Marks the run as worth autosaving. Stays false for sessions where the
        // player did nothing meaningful (entered the world, walked around, quit) —
        // those used to leak phantom Lv.0 autosaves into the Load Game panel.
        // Set true on damage, XP, level-up, item pickup, zone change, manual save.
        // Reset on BeginNewRun / Load (the loaded state already matches disk).
        private bool _sessionDirty;

        public bool IsSessionDirty => _sessionDirty;

        /// <summary>
        /// Fires whenever <see cref="Load"/> falls back to a backup slot
        /// because the primary save file was corrupt or missing. The HUD
        /// subscribes to this and shows a "Recovered from backup" toast
        /// — silent corruption recovery is a footgun in a sandbox game,
        /// the player must know their main save was repaired.
        /// </summary>
        public static event System.Action<SaveLoadResult> OnSaveRecovered;

        protected override bool Persist => true;

        public string CurrentSavePath => _currentSavePath;
        /// <summary>Timestamp of the GameSaveData that was most recently loaded via <see cref="Load"/>.</summary>
        public string LastLoadedTimestamp => _lastLoadedTimestamp;

        protected override void OnSingletonAwake()
        {
            SaveFileManager.EnsureSaveDirectory();
            RebindGameEvents();
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (DIAG_RUN_TWIN_SAVE)
                LogRunTwinSaveDiag(
                    $"OnSingletonAwake: instance born with runId='{_currentRunId}', " +
                    $"ordinal={_currentRunOrdinal}, sessionDirty={_sessionDirty}. " +
                    "If this fires more than once per Play session, the singleton " +
                    "was destroyed and recreated mid-session — the prime mechanism " +
                    "for the twin-save incident.");
        }

        protected override void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnbindGameEvents();
            base.OnDestroy();
        }

        // Both SceneTransitionManager.LoadScene and LoadingScreenController call
        // GameEvents.Clear() to flush stale subscribers before swapping scenes.
        // That nukes our subscriptions too, so re-bind on every scene load.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RebindGameEvents();

        /// <summary>
        /// Marks the current session as worth autosaving on quit / on the
        /// periodic timer. Also (re)starts the debounce window: if no further
        /// MarkDirty arrives within <see cref="dirtyDebounceSeconds"/> the
        /// session will be persisted automatically — covers the gap between
        /// the player doing something meaningful and the long autosave timer.
        /// </summary>
        public void MarkDirty(string reason = null)
        {
            bool firstTime = !_sessionDirty;
            _sessionDirty = true;
            _dirtyDebouncePending = true;
            _dirtyDebounceTimer   = 0f;
            if (firstTime && !string.IsNullOrEmpty(reason))
                Debug.Log($"[SaveService] Session marked dirty: {reason}");
        }

        /// <summary>
        /// Force a save right now, bypassing the periodic timer and the
        /// debounce window. Used by gameplay-critical events (boss kill,
        /// quest completed, zone change, level up, player death) where the
        /// progression must NEVER be lost to a crash.
        /// </summary>
        public bool SaveImmediately(string reason)
        {
            if (!string.IsNullOrEmpty(reason))
                Debug.Log($"[SaveService] SaveImmediately: {reason}");
            // Clear debounce so the timer doesn't double-fire after this.
            _dirtyDebouncePending = false;
            _dirtyDebounceTimer   = -1f;
            return WriteAutosaveToDisk(force: true,
                telemetryKind: SaveTelemetryEntry.SaveKind.Immediate,
                telemetryReason: reason);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // Cache position + HP every frame — plain C# values, safe to read during OnApplicationQuit.
            var playerTransform = EntityRegistry.PlayerTransform;
            if (playerTransform != null)
            {
                _lastKnownPlayerPos  = playerTransform.position;
                _lastKnownPlayerZone = "";
                _hasKnownPlayerPos   = true;

                var hp = EntityRegistry.Player?.GetComponent<Health>();
                if (hp != null) _lastKnownPlayerHp = hp.CurrentHp;
            }

            if (autosaveEnabled)
            {
                _autosaveTimer += dt;
                if (_autosaveTimer >= autosaveIntervalSeconds)
                {
                    _autosaveTimer = 0f;
                    Autosave();
                }
            }

            // Debounce: when MarkDirty has been firing recently, wait until
            // the burst settles (no MarkDirty for dirtyDebounceSeconds) and
            // then write. Resets _dirtyDebouncePending so the next MarkDirty
            // re-arms it. The long autosave timer above is unaffected.
            if (_dirtyDebouncePending && _sessionDirty)
            {
                _dirtyDebounceTimer += dt;
                if (_dirtyDebounceTimer >= dirtyDebounceSeconds)
                {
                    _dirtyDebouncePending = false;
                    _dirtyDebounceTimer   = -1f;
                    WriteAutosaveToDisk(force: false,
                        telemetryKind: SaveTelemetryEntry.SaveKind.DebounceFlush,
                        telemetryReason: "dirty-debounce settled");
                    // The post-debounce save resets the long autosave timer
                    // too — no point firing again 1 s later just because the
                    // periodic timer happened to be near its threshold.
                    _autosaveTimer = 0f;
                }
            }

            if (positionCheckpointEnabled)
            {
                _positionCheckpointTimer += dt;
                if (_positionCheckpointTimer >= positionCheckpointIntervalSeconds)
                {
                    _positionCheckpointTimer = 0f;
                    SavePositionCheckpoint();
                }
            }
        }

        /// <summary>Starts a new run by generating a fresh run ID. Call before the first autosave of a new game.</summary>
        public void BeginNewRun()
        {
            string prevId = _currentRunId;
            int    prevOrd = _currentRunOrdinal;
            _currentRunId = Guid.NewGuid().ToString("N");
            // Clear the previous run's ordinal — the bootstrap's
            // StartTelemetryRun call will mint the next one and feed it back
            // here via SetRunOrdinal. Saves written between BeginNewRun and
            // StartTelemetryRun won't include a run_ordinal (it's 0), but
            // those would only happen from a deliberate Save call before the
            // telemetry system is online, which the bootstrap order rules out.
            _currentRunOrdinal = 0;
            _sessionDirty = false;
            Debug.Log($"[SaveService] New run started: {_currentRunId}");
            if (DIAG_RUN_TWIN_SAVE)
                LogRunTwinSaveDiag(
                    $"BeginNewRun: runId {prevId} -> {_currentRunId}; ordinal {prevOrd} -> 0; sessionDirty -> false");
        }

        public List<RunGroupInfo> ListSavesByRun()
        {
            return SaveFileManager.ListSavesByRun();
        }

        /// <summary>
        /// Manual save into the current run folder.  When <paramref name="slotName"/>
        /// is null/empty a timestamp-based name is used.  Reserved names
        /// ("autosave", etc.) are silently rerouted to a timestamp-based name.
        /// </summary>
        public bool Save(string slotName = null)
        {
            if (RefuseWriteOutsidePlayMode("Save")) return false;
            try
            {
                var data = GameStateCollector.Collect();
                if (data == null)
                {
                    Debug.LogWarning("[SaveService] No save data collected — nothing to save.");
                    return false;
                }

                EnsureRunId();
                data.SetMeta("run_id", _currentRunId);
                if (_currentRunOrdinal > 0)
                    data.SetMeta("run_ordinal", _currentRunOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture));

                string fileName = SaveFileManager.SanitizeSaveName(slotName);
                if (string.IsNullOrEmpty(fileName) || SaveFileManager.IsReservedSaveName(fileName))
                    fileName = $"save_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

                string path = SaveFileManager.GetManualSavePath(_currentRunId, fileName);
                SaveFileManager.WriteSaveFile(path, data, SaveSchemaMigrator.CURRENT_SCHEMA);
                _currentSavePath = path;
                MarkDirty("manual save");

                Debug.Log($"[SaveService] Game saved to: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveService] Save failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Player-driven "save now" — always overwrites the per-run autosave,
        /// regardless of whether the session is dirty. Used by the pause menu's
        /// "Guardar partida" / "Salir" buttons where the user has explicitly
        /// asked for their progress to be persisted.
        /// </summary>
        public bool QuickSave()
        {
            return WriteAutosaveToDisk(force: true,
                telemetryKind: SaveTelemetryEntry.SaveKind.QuickSave,
                telemetryReason: "user-driven QuickSave");
        }

        /// <summary>
        /// Implicit autosave — used by the periodic timer, OnApplicationPause,
        /// and OnApplicationQuit. Skips silently when the session is not dirty
        /// (player has neither taken damage, gained XP, leveled up, picked up
        /// an item, changed zones, nor saved manually since the run started or
        /// was loaded). This prevents trivial "Lv.0 in Lobby" phantom saves
        /// from piling up every time someone briefly opens the game.
        /// </summary>
        public bool Autosave()
        {
            return WriteAutosaveToDisk(force: false);
        }

        /// <summary>
        /// Shared autosave write path. <paramref name="force"/> bypasses the
        /// dirty-flag short-circuit for player-driven saves.
        /// </summary>
        private bool WriteAutosaveToDisk(bool force, SaveTelemetryEntry.SaveKind telemetryKind = SaveTelemetryEntry.SaveKind.Autosave, string telemetryReason = null)
        {
            if (RefuseWriteOutsidePlayMode("WriteAutosaveToDisk")) return false;

            if (!force && !_sessionDirty)
            {
                Debug.Log("[SaveService] Autosave skipped — session has no progression to save.");
                return false;
            }

            // Refuse to write while the run identity is in its transient
            // bootstrap window (BeginNewRun has minted a fresh runId but
            // StartTelemetryRun has not yet set the per-profile ordinal).
            // Any save written in this window lacks `run_ordinal` and produces
            // an orphan Saves/<guid>/ folder — exactly the "phantom run"
            // pattern that pollutes the Load Game panel. The autosave timer /
            // debounce will retry once the ordinal lands, so we lose nothing
            // by skipping. Player-loaded saves bypass this gate because Load
            // restores `_currentRunOrdinal` from disk before any event can fire.
            if (_currentRunOrdinal == 0)
            {
                Debug.LogWarning("[SaveService] Save skipped — run ordinal not yet assigned (still inside bootstrap). " +
                                 "This prevents phantom Saves/<guid>/ folders from being written before " +
                                 "ProfileTelemetrySystem.StartRun finalises the run identity.");
                return false;
            }
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string targetPath = null;
            try
            {
                var data = GameStateCollector.Collect();
                if (data == null) return false;

                EnsureRunId();
                data.SetMeta("run_id", _currentRunId);
                if (_currentRunOrdinal > 0)
                    data.SetMeta("run_ordinal", _currentRunOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture));

                targetPath = SaveFileManager.GetAutosavePath(_currentRunId);
                if (DIAG_RUN_TWIN_SAVE)
                    LogRunTwinSaveDiag(
                        $"WriteAutosaveToDisk: target={targetPath}, " +
                        $"runId={_currentRunId}, ordinal={_currentRunOrdinal}, " +
                        $"force={force}, sessionDirty={_sessionDirty}");
                _currentSavePath = targetPath;

                if (useAsyncDiskIO)
                {
                    EnqueueAsyncAutosave(_currentRunId, data, telemetryKind, telemetryReason, stopwatch);
                }
                else
                {
                    SaveFileManager.RotateAutosaveBackups(_currentRunId);
                    SaveFileManager.WriteSaveFile(targetPath, data, SaveSchemaMigrator.CURRENT_SCHEMA);
                    stopwatch.Stop();
                    SaveTelemetry.Record(new SaveTelemetryEntry(
                        telemetryKind,
                        telemetryReason ?? (force ? "QuickSave" : "Autosave"),
                        success: true,
                        sizeBytes: SafeFileSize(targetPath),
                        durationMs: stopwatch.Elapsed.TotalMilliseconds,
                        path: targetPath,
                        wasAsync: false));
                }

                // A forced save establishes a new on-disk baseline. Subsequent
                // periodic autosaves only need to fire when something else
                // changes after this point, so re-arm the dirty flag.
                if (force) _sessionDirty = false;

                Debug.Log($"[SaveService] {(force ? "QuickSave" : "Autosave")} {(useAsyncDiskIO ? "queued" : "completed")}: {targetPath}");
                return true;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                SaveTelemetry.Record(new SaveTelemetryEntry(
                    telemetryKind,
                    telemetryReason ?? (force ? "QuickSave" : "Autosave"),
                    success: false,
                    sizeBytes: 0,
                    durationMs: stopwatch.Elapsed.TotalMilliseconds,
                    path: targetPath ?? string.Empty,
                    wasAsync: useAsyncDiskIO));
                Debug.LogError($"[SaveService] {(force ? "QuickSave" : "Autosave")} failed: {ex.Message}");
                return false;
            }
        }

        // ── Test-pollution guard ────────────────────────────────────────────
        // EditMode tests can `AddComponent<SaveService>` and exercise event
        // handlers that ultimately call WriteAutosaveToDisk / Save / position-
        // checkpoint, which derive paths from Application.persistentDataPath
        // and silently leak real Saves/<guid>/ folders into the user's profile
        // directory. This guard refuses every disk write that derives a path
        // from persistentDataPath when we are NOT in Play Mode (i.e. we are
        // running inside the EditMode test runner). Tests that legitimately
        // need to verify disk I/O call SaveFileManager directly with explicit
        // temp paths; that path is not affected.
        // See incident: .github/incidents/RUN_TWIN_SAVE.md
        private static bool RefuseWriteOutsidePlayMode(string callerName)
        {
            if (!Application.isEditor || Application.isPlaying) return false;
            Debug.LogWarning(
                $"[SaveService] {callerName} refused — Play Mode is not active. " +
                "EditMode test pollution prevention; production code is unaffected.");
            return true;
        }

        private void EnsureRunId()
        {
            if (string.IsNullOrEmpty(_currentRunId))
            {
                _currentRunId = Guid.NewGuid().ToString("N");
                Debug.Log($"[SaveService] No active run — generated new run id: {_currentRunId}");
                if (DIAG_RUN_TWIN_SAVE)
                    LogRunTwinSaveDiagWithStack(
                        $"EnsureRunId minted GUID {_currentRunId} mid-session " +
                        $"(ordinal={_currentRunOrdinal}, sessionDirty={_sessionDirty}). " +
                        "This is the prime suspect for the twin-save incident — " +
                        "stacktrace identifies the caller chain.");
            }
        }

        public bool Load(string path)
        {
            // Don't read while a previous async autosave is still mid-flight —
            // we'd risk loading an in-progress (renamed-out) file or stale
            // checksum. Worst case this blocks for the duration of one write.
            FlushPendingWrites(quitWaitSeconds);
            var loadResult = SaveFileManager.TryLoadWithRecoveryDetailed(path);
            if (!loadResult.IsSuccess)
            {
                Debug.LogWarning($"[SaveService] Load skipped - no valid save found for: {path}");
                return false;
            }

            // Surface a backup-recovery to listeners (HUD toast, telemetry,
            // run-end summary) BEFORE applying state. Subscribers can decide
            // whether to expose the corruption event to the player.
            if (loadResult.RecoveredFromBackup)
            {
                Debug.LogWarning($"[SaveService] Loaded RECOVERED save from backup slot " +
                                 $"#{loadResult.BackupSlotIndex} ({loadResult.SourcePath}) — " +
                                 $"primary save at '{path}' was corrupt or missing.");
                try { OnSaveRecovered?.Invoke(loadResult); }
                catch (Exception ex)
                { Debug.LogError($"[SaveService] OnSaveRecovered subscriber threw: {ex.Message}"); }
            }

            var data = SaveSchemaMigrator.Migrate(loadResult.Data);
            GameStateRestorer.Restore(data);
            _currentSavePath     = path;
            _lastLoadedTimestamp = data.timestamp;
            string prevRunId = _currentRunId;
            int    prevOrd   = _currentRunOrdinal;
            _currentRunId        = data.GetMeta("run_id", "");

            // Legacy save without a run id — adopt a fresh one so all subsequent
            // autosaves from this session are isolated in their own run folder.
            if (string.IsNullOrEmpty(_currentRunId))
            {
                _currentRunId = Guid.NewGuid().ToString("N");
                Debug.Log($"[SaveService] Loaded legacy save without run_id — assigned: {_currentRunId}");
                if (DIAG_RUN_TWIN_SAVE)
                    LogRunTwinSaveDiagWithStack(
                        $"Load: legacy save lacked meta.run_id; minted {_currentRunId} " +
                        $"(prevRunId={prevRunId}). Could explain a twin-save if this " +
                        "fires after a successful Load already set the runId.");
            }
            else if (DIAG_RUN_TWIN_SAVE)
            {
                LogRunTwinSaveDiag(
                    $"Load: runId {prevRunId} -> {_currentRunId} from meta " +
                    $"(prevOrd={prevOrd}; new ordinal will be read below).");
            }

            // Adopt the run ordinal stored alongside run_id so the resumed
            // session keeps its "Run #N" identity. Pre-ordinal saves return
            // 0 (the missing-meta fallback), and the bootstrap will mint a
            // fresh ordinal via ProfileTelemetrySystem.StartRun in that case.
            string ordinalStr = data.GetMeta("run_ordinal", "");
            if (!int.TryParse(ordinalStr, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture, out _currentRunOrdinal))
                _currentRunOrdinal = 0;

            // Loaded state matches disk byte-for-byte — no autosave needed until
            // the player actually does something.
            _sessionDirty = false;

            Debug.Log($"[SaveService] Game loaded from: {path} (schema {data.schemaVersion}, run_id={_currentRunId})");
            return true;
        }

        /// <summary>
        /// Loads the newest auto-save across all runs.  Returns false when
        /// there are no saves at all.
        /// </summary>
        public bool QuickLoad()
        {
            var groups = SaveFileManager.ListSavesByRun();
            foreach (var grp in groups)
            {
                if (grp.isLegacy) continue;
                foreach (var s in grp.saves)
                    if (s.isAutoSave && !s.isCorrupted)
                        return Load(s.path);
            }
            // Fallback: any non-corrupt save (legacy bucket included)
            foreach (var grp in groups)
                foreach (var s in grp.saves)
                    if (!s.isCorrupted) return Load(s.path);

            Debug.LogWarning("[SaveService] QuickLoad: no saves available.");
            return false;
        }

        public List<SaveSlotInfo> ListSaves()
        {
            return SaveFileManager.ListSaves();
        }

        public bool DeleteSave(string path)
        {
            return SaveFileManager.DeleteSave(path);
        }

        /// <summary>
        /// Write a tiny position-only file for crash-safe position recovery.
        /// Uses atomic write + backup copy; safe to call every 10 seconds.
        /// </summary>
        public void SavePositionCheckpoint()
        {
            if (RefuseWriteOutsidePlayMode("SavePositionCheckpoint")) return;
            if (!_hasKnownPlayerPos) return;
            // Never persist a dead player's position — it would become the crash-recovery location.
            if (_lastKnownPlayerHp <= 0) return;
            try
            {
                var data = new PositionCheckpointData
                {
                    x         = _lastKnownPlayerPos.x,
                    y         = _lastKnownPlayerPos.y,
                    zone      = _lastKnownPlayerZone ?? "",
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
                };
                SaveFileManager.WritePositionCheckpoint(data);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveService] Position checkpoint failed: {ex.Message}");
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                Debug.Log("[SaveService] Application paused — triggering autosave + position checkpoint.");
                SavePositionCheckpoint();
                // Force-save so the LATEST player position lands on disk even
                // when an earlier SaveImmediately (level up / zone change /
                // damage / XP / pickup) already cleared `_sessionDirty`.
                // Without `force=true`, a non-dirty session at pause time
                // would no-op and the player's post-trigger movement would
                // be lost on next launch.
                SaveImmediately("application pause");
                // On mobile this is the user backgrounding the app; the OS
                // can kill us at any moment after the autosave queue spawned
                // its task. Block until disk durability is guaranteed.
                FlushPendingWrites(quitWaitSeconds);
            }
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[SaveService] Application quitting — triggering position checkpoint + autosave.");
            SavePositionCheckpoint();
            // Only write the per-run autosave when the player is alive.  A dead-state
            // save would restore the player with 0 HP on next "Continue".
            if (_hasKnownPlayerPos && _lastKnownPlayerHp > 0)
            {
                // Same rationale as OnApplicationPause: force-save so the
                // latest position is on disk even when no MarkDirty has fired
                // since the last forced save.
                SaveImmediately("application quit");
            }
            // Wait for the queue to drain before Unity tears the service
            // down — otherwise Process exit can land mid-rename / mid-write.
            FlushPendingWrites(quitWaitSeconds);
        }

        // ── [RunTwinSave-Diag] ───────────────────────────────────────────────
        // Temporary instrumentation for incident
        // .github/incidents/RUN_TWIN_SAVE.md. Delete this region (and the
        // DIAG_RUN_TWIN_SAVE constant + its callsites above) once the root
        // cause is pinned down and the regression test lands.

        private static void LogRunTwinSaveDiag(string message)
        {
            Debug.Log($"[RunTwinSave-Diag] {message}");
        }

        private static void LogRunTwinSaveDiagWithStack(string message)
        {
            // Use LogWarning so Unity attaches the managed stack trace
            // automatically in builds where Log stack traces are stripped.
            Debug.LogWarning($"[RunTwinSave-Diag] {message}\n" +
                             $"Stack:\n{System.Environment.StackTrace}");
        }
    }

    /// <summary>
    /// Lightweight info about a save slot for UI display.
    /// Mirrors Python's SaveService.list_saves() metadata.
    /// </summary>
    [Serializable]
    public struct SaveSlotInfo
    {
        public string path;
        public string fileName;
        public string timestamp;
        public string schemaVersion;
        public bool   isCorrupted;
        public bool   isAutoSave;     // true = the per-run autosave.json ("Auto-Save")
        public string runId;          // empty = legacy save (no run_id in file)
        public int    runOrdinal;     // 0 = pre-ordinal save (legacy or load failure)

        // ── Gameplay metadata ────────────────────────────────────────────────
        public string playerClass;
        public int    level;
        public int    experience;
        public int    hp;
        public int    maxHp;
        public string currentZone;
    }

    /// <summary>
    /// Aggregates saves belonging to a single gameplay run, used by the Load Game panel.
    /// </summary>
    [Serializable]
    public class RunGroupInfo
    {
        public string           runId;            // empty = legacy group
        public int              runOrdinal;       // 0 = pre-ordinal or legacy group
        public string           displayName;
        public string           playerClass;
        public int              maxLevel;
        public string           latestTimestamp;
        public bool             isLegacy;
        public List<SaveSlotInfo> saves = new List<SaveSlotInfo>();
    }
}
