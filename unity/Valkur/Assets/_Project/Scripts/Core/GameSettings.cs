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

        /// <summary>
        /// One dial in front of the three channels. It exists because the shipped Audio panel
        /// had eight rows and not one of them was "turn the game down": Music, Ambience and SFX
        /// each had their own slider and five of the eight rows were mixing knobs (the ducking
        /// attenuation, hold and release, and the ambience interval pair) that belong to whoever
        /// tunes the mix, not to whoever is playing.
        /// </summary>
        public float masterVolume   = 1.0f;

        // ── Video ────────────────────────────────────────────────────────────

        /// <summary>0 off, 1 every v-blank, 2 every second one. Applied through QualitySettings.</summary>
        public int vSyncCount = 1;

        /// <summary>0 means no cap. Only consulted when vSync is off; Unity ignores it otherwise.</summary>
        public int frameRateCap = 0;

        /// <summary>
        /// Multiplies the HUD and menu canvas scale. 0 means "follow the resolution", which is
        /// what every canvas did before this existed — correct for a 1600x800 window and small
        /// for somebody playing a pixel-art game on a 4K display.
        /// </summary>
        public float uiScale = 0f;

        // ── Accessibility ────────────────────────────────────────────────────

        /// <summary>
        /// Turns off the menu's particles, the title's assembly, the carousel's slow push and
        /// every panel animation. It is a real accessibility setting and not a performance one:
        /// what it removes is MOVEMENT, and everything it removes is decoration by construction —
        /// no state in this game is readable only from something that moves.
        /// </summary>
        public bool reduceMotion = false;

        /// <summary>Shakes the camera on impacts. Separate from reduceMotion: one is the world,
        /// the other is the interface, and a player may well want one and not the other.</summary>
        public bool screenShake = true;

        /// <summary>-1 small, 0 normal, 1 large. Scales menu and HUD type.</summary>
        public int textSize = 0;

        /// <summary>Shows the control hints at the foot of every menu panel.</summary>
        public bool showHints = true;

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
            masterVolume        = fresh.masterVolume;
            // Video
            vSyncCount   = fresh.vSyncCount;
            frameRateCap = fresh.frameRateCap;
            uiScale      = fresh.uiScale;
            // Accessibility
            reduceMotion = fresh.reduceMotion;
            screenShake  = fresh.screenShake;
            textSize     = fresh.textSize;
            showHints    = fresh.showHints;
            // Input
        }

        /// <summary>
        /// Pushes the video settings Unity owns. Called on boot and on every Apply, because
        /// QualitySettings is engine state and a value that only lives in this object is the
        /// authored-and-inert shape this project already records a dozen times.
        /// </summary>
        public void ApplyVideoSettings()
        {
            QualitySettings.vSyncCount = Mathf.Clamp(vSyncCount, 0, 2);
            // Unity ignores targetFrameRate entirely while vSync is on, so a cap set there would
            // be a control that silently does nothing — set it to -1 so the two never disagree.
            Application.targetFrameRate = vSyncCount > 0 ? -1 : (frameRateCap <= 0 ? -1 : frameRateCap);
        }

        /// <summary>The multiplier every menu and HUD canvas applies on top of its own scale.</summary>
        public float TextScale()
        {
            switch (Mathf.Clamp(textSize, -1, 1))
            {
                case -1: return 0.88f;
                case 1: return 1.18f;
                default: return 1f;
            }
        }
    }
}
