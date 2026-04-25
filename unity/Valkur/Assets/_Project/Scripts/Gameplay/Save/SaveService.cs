using System;
using System.Collections.Generic;
using UnityEngine;
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
        private const string AUTOSAVE_PREFIX = "autosave";
        private const string QUICKSAVE_PREFIX = "quicksave";

        [Header("Autosave")]
        [SerializeField] private bool autosaveEnabled = true;
        [SerializeField] private float autosaveIntervalSeconds = 300f;

        [Header("Position Checkpoint")]
        [Tooltip("Write a lightweight position-only file this often. Protects position against crashes.")]
        [SerializeField] private bool positionCheckpointEnabled = true;
        [SerializeField] private float positionCheckpointIntervalSeconds = 10f;

        private float _autosaveTimer;
        private float _positionCheckpointTimer;
        private string _currentSavePath;
        private string _lastLoadedTimestamp;

        // Cached as plain C# values so OnApplicationQuit can read them
        // without touching any Unity Object reference during teardown.
        private Vector2 _lastKnownPlayerPos;
        private string  _lastKnownPlayerZone = "";
        private int     _lastKnownPlayerHp   = -1;   // -1 = not yet sampled
        private bool    _hasKnownPlayerPos;

        protected override bool Persist => true;

        public string CurrentSavePath => _currentSavePath;
        /// <summary>Timestamp of the GameSaveData that was most recently loaded via <see cref="Load"/>.</summary>
        public string LastLoadedTimestamp => _lastLoadedTimestamp;

        protected override void OnSingletonAwake()
        {
            SaveFileManager.EnsureSaveDirectory();
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

                string fileName = string.IsNullOrEmpty(slotName)
                    ? $"save_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
                    : slotName;

                string path = SaveFileManager.GetSavePath(fileName);
                SaveFileManager.WriteSaveFile(path, data, SaveSchemaMigrator.CURRENT_SCHEMA);
                _currentSavePath = path;

                Debug.Log($"[SaveService] Game saved to: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveService] Save failed: {ex.Message}");
                return false;
            }
        }

        public bool QuickSave()
        {
            return Save(QUICKSAVE_PREFIX);
        }

        public void Autosave()
        {
            try
            {
                var data = GameStateCollector.Collect();
                if (data == null) return;

                SaveFileManager.RotateBackups(AUTOSAVE_PREFIX);

                string path = SaveFileManager.GetSavePath($"{AUTOSAVE_PREFIX}_0");
                SaveFileManager.WriteSaveFile(path, data, SaveSchemaMigrator.CURRENT_SCHEMA);

                Debug.Log($"[SaveService] Autosave completed: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveService] Autosave failed: {ex.Message}");
            }
        }

        public bool Load(string path)
        {
            var data = SaveFileManager.TryLoadWithRecovery(path);
            if (data == null)
            {
                Debug.LogError($"[SaveService] Load failed — no valid save found for: {path}");
                return false;
            }

            data = SaveSchemaMigrator.Migrate(data);
            GameStateRestorer.Restore(data);
            _currentSavePath = path;
            _lastLoadedTimestamp = data.timestamp;

            Debug.Log($"[SaveService] Game loaded from: {path} (schema {data.schemaVersion})");
            return true;
        }

        public bool QuickLoad()
        {
            string path = SaveFileManager.GetSavePath(QUICKSAVE_PREFIX);
            return Load(path);
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
            Debug.Log("[SaveService] Application quitting — triggering position checkpoint + shutdown save.");
            SavePositionCheckpoint();
            // Only write shutdown_save when the player is alive.
            // A dead-state save would restore the player with 0 HP on next "Continue".
            if (_hasKnownPlayerPos && _lastKnownPlayerHp > 0)
                Save("shutdown_save");
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

        // ── Gameplay metadata ────────────────────────────────────────────────
        public string playerClass;
        public int    level;
        public int    experience;
        public int    hp;
        public int    maxHp;
        public string currentZone;
    }
}
