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

        private float _autosaveTimer;
        private string _currentSavePath;

        protected override bool Persist => true;

        public string CurrentSavePath => _currentSavePath;

        protected override void OnSingletonAwake()
        {
            SaveFileManager.EnsureSaveDirectory();
        }

        private void Update()
        {
            if (!autosaveEnabled) return;

            _autosaveTimer += Time.unscaledDeltaTime;
            if (_autosaveTimer >= autosaveIntervalSeconds)
            {
                _autosaveTimer = 0f;
                Autosave();
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

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                Debug.Log("[SaveService] Application paused — triggering autosave.");
                Autosave();
            }
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[SaveService] Application quitting — triggering shutdown save.");
            Save("shutdown_save");
        }
    }

    /// <summary>
    /// Lightweight info about a save slot for UI display.
    /// </summary>
    [Serializable]
    public struct SaveSlotInfo
    {
        public string path;
        public string fileName;
        public string timestamp;
        public string schemaVersion;
    }
}
