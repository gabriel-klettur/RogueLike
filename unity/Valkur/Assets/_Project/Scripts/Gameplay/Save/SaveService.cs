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
    public class SaveService : SingletonMonoBehaviour<SaveService>
    {
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

        // Debounce window after a MarkDirty so a rapid burst of dirty events
        // (combat sequence, mass loot pickup) coalesces into a single save
        // instead of spamming the disk. The autosave timer remains the long-
        // term safety net; this window is the short-term coalescer.
        [SerializeField] private float dirtyDebounceSeconds = 2f;
        private float _dirtyDebounceTimer = -1f;
        private bool  _dirtyDebouncePending;

        public string RunId => _currentRunId;

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

        protected override bool Persist => true;

        public string CurrentSavePath => _currentSavePath;
        /// <summary>Timestamp of the GameSaveData that was most recently loaded via <see cref="Load"/>.</summary>
        public string LastLoadedTimestamp => _lastLoadedTimestamp;

        protected override void OnSingletonAwake()
        {
            SaveFileManager.EnsureSaveDirectory();
            RebindGameEvents();
            SceneManager.sceneLoaded += OnSceneLoaded;
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
            return WriteAutosaveToDisk(force: true);
        }

        private void RebindGameEvents()
        {
            // Removing a non-subscribed handler is a safe no-op, so doing
            // unbind+bind unconditionally keeps subscriptions exactly-once
            // even when GameEvents.Clear() ran between calls.
            UnbindGameEvents();
            // Tier 2 dirty triggers — flip the flag, the timer / debounce
            // takes care of the actual write.
            GameEvents.OnPlayerDamaged += HandlePlayerDamaged;
            GameEvents.OnXpGained      += HandleXpGained;
            GameEvents.OnItemPickedUp  += HandleItemPickedUp;
            GameEvents.OnItemConsumed  += HandleItemConsumed;
            // Critical milestones — force an immediate save.
            GameEvents.OnLevelUp       += HandleLevelUp;
            GameEvents.OnZoneChanged   += HandleZoneChanged;
            GameEvents.OnPlayerDied    += HandlePlayerDied;
            GameEvents.OnEntityDied    += HandleEntityDied;
        }

        private void UnbindGameEvents()
        {
            GameEvents.OnPlayerDamaged -= HandlePlayerDamaged;
            GameEvents.OnXpGained      -= HandleXpGained;
            GameEvents.OnItemPickedUp  -= HandleItemPickedUp;
            GameEvents.OnItemConsumed  -= HandleItemConsumed;
            GameEvents.OnLevelUp       -= HandleLevelUp;
            GameEvents.OnZoneChanged   -= HandleZoneChanged;
            GameEvents.OnPlayerDied    -= HandlePlayerDied;
            GameEvents.OnEntityDied    -= HandleEntityDied;
        }

        private void HandlePlayerDamaged(int amount, int currentHp, int maxHp) =>
            MarkDirty($"player damaged ({amount} dmg)");

        private void HandleXpGained(GameObject entity, int amount)
        {
            if (entity != null && entity.CompareTag("Player"))
                MarkDirty($"player gained {amount} XP");
        }

        private void HandleLevelUp(GameObject entity, int newLevel)
        {
            if (entity == null || !entity.CompareTag("Player")) return;
            // Level-up is a milestone — force the save now instead of waiting
            // for the timer. A crash between level-up and next periodic save
            // would otherwise lose the new level + skill points.
            SaveImmediately($"player leveled up to {newLevel}");
        }

        private void HandleItemPickedUp(GameObject collector, string itemName, int quantity)
        {
            if (collector != null && collector.CompareTag("Player"))
                MarkDirty($"player picked up {itemName} x{quantity}");
        }

        private void HandleItemConsumed(GameObject consumer, string itemName)
        {
            if (consumer != null && consumer.CompareTag("Player"))
                MarkDirty($"player consumed {itemName}");
        }

        private void HandleZoneChanged(string oldZone, string newZone) =>
            // Zone transitions are the canonical "checkpoint" in sandbox
            // games. Force-save so a crash on the new zone never sends the
            // player back to the old one.
            SaveImmediately($"zone {oldZone} → {newZone}");

        private void HandlePlayerDied()
        {
            // Player death is the most expensive thing to lose — restart-from-
            // checkpoint UX depends on the on-disk state being current. We
            // still gate on _hasKnownPlayerPos because OnApplicationQuit's
            // alive-only guard does not apply to death itself (we WANT the
            // dead state recorded so the run-end UI can read it).
            SaveImmediately("player died");
        }

        private void HandleEntityDied(GameObject victim, GameObject killer)
        {
            // GameEvents.FireEntityDied passes (victim, killer) in that order;
            // the same convention as OnEntityDamaged. Keep parameter names in
            // sync with the source signature so the boss-detection branch
            // inspects the actual victim.
            if (victim == null) return;
            var boss = victim.GetComponent<BossPhaseController>();
            if (boss != null)
                SaveImmediately($"boss '{victim.name}' defeated");
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
                    Autosave();
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
            _currentRunId = Guid.NewGuid().ToString("N");
            _sessionDirty = false;
            Debug.Log($"[SaveService] New run started: {_currentRunId}");
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
            return WriteAutosaveToDisk(force: true);
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
        private bool WriteAutosaveToDisk(bool force)
        {
            if (!force && !_sessionDirty)
            {
                Debug.Log("[SaveService] Autosave skipped — session has no progression to save.");
                return false;
            }
            try
            {
                var data = GameStateCollector.Collect();
                if (data == null) return false;

                EnsureRunId();
                data.SetMeta("run_id", _currentRunId);

                SaveFileManager.RotateAutosaveBackups(_currentRunId);

                string path = SaveFileManager.GetAutosavePath(_currentRunId);
                SaveFileManager.WriteSaveFile(path, data, SaveSchemaMigrator.CURRENT_SCHEMA);
                _currentSavePath = path;

                // A forced save establishes a new on-disk baseline. Subsequent
                // periodic autosaves only need to fire when something else
                // changes after this point, so re-arm the dirty flag.
                if (force) _sessionDirty = false;

                Debug.Log($"[SaveService] {(force ? "QuickSave" : "Autosave")} completed: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveService] {(force ? "QuickSave" : "Autosave")} failed: {ex.Message}");
                return false;
            }
        }

        private void EnsureRunId()
        {
            if (string.IsNullOrEmpty(_currentRunId))
            {
                _currentRunId = Guid.NewGuid().ToString("N");
                Debug.Log($"[SaveService] No active run — generated new run id: {_currentRunId}");
            }
        }

        public bool Load(string path)
        {
            var data = SaveFileManager.TryLoadWithRecovery(path);
            if (data == null)
            {
                Debug.LogWarning($"[SaveService] Load skipped - no valid save found for: {path}");
                return false;
            }

            data = SaveSchemaMigrator.Migrate(data);
            GameStateRestorer.Restore(data);
            _currentSavePath     = path;
            _lastLoadedTimestamp = data.timestamp;
            _currentRunId        = data.GetMeta("run_id", "");

            // Legacy save without a run id — adopt a fresh one so all subsequent
            // autosaves from this session are isolated in their own run folder.
            if (string.IsNullOrEmpty(_currentRunId))
            {
                _currentRunId = Guid.NewGuid().ToString("N");
                Debug.Log($"[SaveService] Loaded legacy save without run_id — assigned: {_currentRunId}");
            }

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
                Autosave();
            }
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[SaveService] Application quitting — triggering position checkpoint + autosave.");
            SavePositionCheckpoint();
            // Only write the per-run autosave when the player is alive.  A dead-state
            // save would restore the player with 0 HP on next "Continue".
            if (_hasKnownPlayerPos && _lastKnownPlayerHp > 0)
                Autosave();
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
        public string           displayName;
        public string           playerClass;
        public int              maxLevel;
        public string           latestTimestamp;
        public bool             isLegacy;
        public List<SaveSlotInfo> saves = new List<SaveSlotInfo>();
    }
}
