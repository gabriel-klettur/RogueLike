using System;
using System.IO;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Lightweight settings data class persisted to JSON in persistentDataPath/settings.json.
    /// Mirrors Python audio_config.py + input_config.py defaults.
    /// All values are stored here but enforcement (e.g. actual audio volume) is deferred
    /// until the respective systems are implemented.
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        // ── Audio ────────────────────────────────────────────────────────────
        public float musicVolume    = 0.6f;   // Python default 0.6
        public float ambientVolume  = 0.6f;
        public float sfxVolume      = 0.7f;
        public float ambientMinInterval = 6.0f;
        public float ambientMaxInterval = 18.0f;
        public float duckingAttenuation = -4.0f;
        public float duckingHoldMs      = 250f;
        public float duckingReleaseMs   = 200f;

        // ── Input bindings (keyboard primary / secondary) ────────────────────
        // General
        public string pauseKeyA        = "Escape";
        public string toggleInventoryKeyA = "i";
        // Movement
        public string moveUpKeyA    = "w";       public string moveUpKeyB    = "UpArrow";
        public string moveDownKeyA  = "s";       public string moveDownKeyB  = "DownArrow";
        public string moveLeftKeyA  = "a";       public string moveLeftKeyB  = "LeftArrow";
        public string moveRightKeyA = "d";       public string moveRightKeyB = "RightArrow";
        public string dashKeyA      = "RightCtrl"; public string dashKeyB   = "RightShift";
        // Spells
        public string spell1KeyA = "1";
        public string spell2KeyA = "2";
        public string spell3KeyA = "3";
        public string spell4KeyA = "4";
        public string primaryAttackMouse  = "LeftButton";
        public string secondaryAttackMouse = "RightButton";
        // Editors
        public string toggleTileEditorKeyA = "F8";
        public string toggleMapEditorKeyA  = "F11";

        // ── Statics ──────────────────────────────────────────────────────────
        private static GameSettings _instance;
        public static GameSettings Instance
        {
            get
            {
                if (_instance == null) _instance = Load();
                return _instance;
            }
        }

        private static string FilePath =>
            Path.Combine(Application.persistentDataPath, "settings.json");

        public static GameSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var loaded = JsonUtility.FromJson<GameSettings>(json);
                    if (loaded != null)
                    {
                        Debug.Log("[GameSettings] Loaded from disk.");
                        return loaded;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSettings] Load failed ({e.Message}), using defaults.");
            }
            return new GameSettings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
                Debug.Log("[GameSettings] Saved to disk.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameSettings] Save failed: {e.Message}");
            }
        }

        public void ResetToDefaults()
        {
            var fresh = new GameSettings();
            // Audio
            musicVolume = fresh.musicVolume;
            ambientVolume = fresh.ambientVolume;
            sfxVolume = fresh.sfxVolume;
            ambientMinInterval  = fresh.ambientMinInterval;
            ambientMaxInterval  = fresh.ambientMaxInterval;
            duckingAttenuation  = fresh.duckingAttenuation;
            duckingHoldMs       = fresh.duckingHoldMs;
            duckingReleaseMs    = fresh.duckingReleaseMs;
            // Input
            pauseKeyA = fresh.pauseKeyA;
            toggleInventoryKeyA = fresh.toggleInventoryKeyA;
            moveUpKeyA    = fresh.moveUpKeyA;    moveUpKeyB    = fresh.moveUpKeyB;
            moveDownKeyA  = fresh.moveDownKeyA;  moveDownKeyB  = fresh.moveDownKeyB;
            moveLeftKeyA  = fresh.moveLeftKeyA;  moveLeftKeyB  = fresh.moveLeftKeyB;
            moveRightKeyA = fresh.moveRightKeyA; moveRightKeyB = fresh.moveRightKeyB;
            dashKeyA      = fresh.dashKeyA;      dashKeyB      = fresh.dashKeyB;
            spell1KeyA    = fresh.spell1KeyA;
            spell2KeyA    = fresh.spell2KeyA;
            spell3KeyA    = fresh.spell3KeyA;
            spell4KeyA    = fresh.spell4KeyA;
            primaryAttackMouse   = fresh.primaryAttackMouse;
            secondaryAttackMouse = fresh.secondaryAttackMouse;
            toggleTileEditorKeyA = fresh.toggleTileEditorKeyA;
            toggleMapEditorKeyA  = fresh.toggleMapEditorKeyA;
        }
    }
}
