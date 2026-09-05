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
        // ── Game mode ────────────────────────────────────────────────────────
        // Permadeath: when true, the player's death deletes the active save
        // and forces a return to the main menu. Off by default — opt-in
        // hardcore mode. PermadeathSaveCleanupSystem listens to OnPlayerDied
        // and respects this flag.
        public bool permadeath = false;

        // ── Display ──────────────────────────────────────────────────────────
        // Window size, in the units the player picked from Options > Video.
        // 0x0 means "Native" — keep the desktop resolution and let
        // AspectRatioEnforcer letterbox down to the 2:1 target. Any other value
        // must be one of DisplaySettings.Presets; an unrecognised pair falls
        // back to Native rather than resizing the window to something the
        // camera can't render seam-free.
        public int resolutionWidth  = 0;
        public int resolutionHeight = 0;
        public WindowMode windowMode = WindowMode.Windowed;

        // ── Audio ────────────────────────────────────────────────────────────
        public float musicVolume    = 0.6f;   // Python default 0.6
        public float ambientVolume  = 0.6f;
        public float sfxVolume      = 0.7f;
        public float ambientMinInterval = 6.0f;
        public float ambientMaxInterval = 18.0f;
        public float duckingAttenuation = -4.0f;
        public float duckingHoldMs      = 250f;
        public float duckingReleaseMs   = 200f;

        // ── Input bindings ───────────────────────────────────────────────────
        //
        // THE BINDING FIELDS ARE GONE, and what they were is worth recording. Twenty-eight
        // strings — pauseKeyA, toggleInventoryKeyA, moveUp/Down/Left/RightKeyA+B, dashKeyA+B,
        // spell1..4KeyA, primaryAttackMouse, secondaryAttackMouse and twelve editor toggles —
        // written by a Controls panel in two menus and read, in production, by exactly one
        // consumer: EditorBindingsApplier, which bridged the TWELVE EDITOR KEYS onto the real
        // actions and nothing else. Every gameplay field had zero production readers, verified
        // by grep across the whole project; only tests touched them. So the panel let a player
        // rebind their movement, showed the new key, saved it to disk, and changed nothing.
        //
        // There is one binding model now — Resources/Input/ValkurInputActions — and its
        // overrides persist through Valkur.Core.Input.InputBindingStore, beside the per-action
        // War/Peace stance masks. Nothing about controls belongs in this file any more.

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
                // Save() runs on every settings mutation — dragging one volume
                // slider produced dozens of identical lines. Kept behind
                // `verbose settings on` for when persistence itself is suspect.
                VerboseLog.Log(VerboseLog.Category.Settings, "[GameSettings] Saved to disk.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameSettings] Save failed: {e.Message}");
            }
        }

        public void ResetToDefaults()
        {
            var fresh = new GameSettings();
            // Game mode
            permadeath = fresh.permadeath;
            // Display
            resolutionWidth  = fresh.resolutionWidth;
            resolutionHeight = fresh.resolutionHeight;
            windowMode       = fresh.windowMode;
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
        }
    }
}
