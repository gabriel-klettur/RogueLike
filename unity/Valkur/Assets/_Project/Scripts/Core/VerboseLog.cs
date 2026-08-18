using System;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Central on/off switch for high-volume development logging.
    ///
    /// Some subsystems legitimately want to narrate every step — which overlay
    /// file was painted, how many tiles landed on each tilemap, every settings
    /// write. That detail is genuinely useful while working on those systems and
    /// while diagnosing a bad map, but at boot it buries the handful of lines
    /// that actually matter: a single world load emitted 336 OverlayLoader lines
    /// against ~20 lines of real signal.
    ///
    /// So the detail is NOT deleted — it is gated. Each noisy area gets a
    /// <see cref="Category"/>; the category is off by default and can be flipped
    /// at any time, without a recompile, from the in-game DevConsole:
    ///
    ///     verbose                 → list categories and their state
    ///     verbose world on        → re-enable the per-overlay / per-layer detail
    ///     verbose all off         → silence every gated category
    ///
    /// The choice persists through <see cref="PlayerPrefs"/>, so turning a
    /// category on for a debugging session survives Play-mode restarts and
    /// domain reloads until it is explicitly turned off again.
    ///
    /// Summary lines (e.g. "[WorldLoader] Full world loaded: 48 overlays…") are
    /// deliberately NOT gated — they stay visible always, because they are the
    /// line you read when you are not debugging that subsystem.
    /// </summary>
    public static class VerboseLog
    {
        /// <summary>Gated logging areas. Add a member here, then gate the calls.</summary>
        [Flags]
        public enum Category
        {
            None    = 0,
            /// <summary>Per-overlay / per-tilemap world loading detail.</summary>
            World   = 1 << 0,
            /// <summary>Settings persistence (every GameSettings.Save).</summary>
            Settings = 1 << 1,
            /// <summary>Per-layer collision baking diagnostics.</summary>
            Collision = 1 << 2,
            /// <summary>Scene composition step-by-step wiring.</summary>
            Bootstrap = 1 << 3,
            All = World | Settings | Collision | Bootstrap,
        }

        private const string PREFS_KEY = "Valkur.VerboseLog.Mask";

        // Domain Reload is OFF for fast iteration, so a static field would keep
        // whatever the last Play session left behind. Re-reading PlayerPrefs on
        // SubsystemRegistration makes the state deterministic at every entry.
        private static Category _enabled = Category.None;
        private static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEnter()
        {
            _loaded = false;
            Load();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try { _enabled = (Category)PlayerPrefs.GetInt(PREFS_KEY, (int)Category.None); }
            catch { _enabled = Category.None; }
        }

        /// <summary>Currently enabled categories.</summary>
        public static Category Enabled
        {
            get { Load(); return _enabled; }
        }

        /// <summary>True when <paramref name="category"/> should emit its detail logs.</summary>
        public static bool IsOn(Category category)
        {
            Load();
            return (_enabled & category) != 0;
        }

        /// <summary>Turn one or more categories on or off and persist the choice.</summary>
        public static void Set(Category category, bool on)
        {
            Load();
            _enabled = on ? (_enabled | category) : (_enabled & ~category);
            try
            {
                PlayerPrefs.SetInt(PREFS_KEY, (int)_enabled);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VerboseLog] Could not persist verbose mask: {ex.Message}");
            }
        }

        /// <summary>
        /// Log <paramref name="message"/> only when <paramref name="category"/>
        /// is enabled. Callers that build an expensive string should test
        /// <see cref="IsOn"/> first — this overload still pays for the
        /// interpolation before the call.
        /// </summary>
        public static void Log(Category category, string message)
        {
            if (IsOn(category)) Debug.Log(message);
        }

        /// <summary>
        /// Allocation-free variant: the message is only built when the category
        /// is on. Use this on hot paths (per-tile, per-frame, per-file loops).
        /// </summary>
        public static void Log(Category category, Func<string> messageFactory)
        {
            if (messageFactory != null && IsOn(category)) Debug.Log(messageFactory());
        }

        /// <summary>Human-readable state dump, used by the DevConsole `verbose` command.</summary>
        public static string Describe()
        {
            Load();
            var values = (Category[])Enum.GetValues(typeof(Category));
            var sb = new System.Text.StringBuilder();
            sb.Append("verbose categories:");
            foreach (var v in values)
            {
                if (v == Category.None || v == Category.All) continue;
                sb.Append("\n  ").Append(v.ToString().ToLowerInvariant())
                  .Append(" = ").Append(IsOn(v) ? "on" : "off");
            }
            return sb.ToString();
        }

        /// <summary>Parse a category name ("world", "all", …). Returns false when unknown.</summary>
        public static bool TryParse(string name, out Category category)
        {
            category = Category.None;
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (Category v in Enum.GetValues(typeof(Category)))
            {
                if (v == Category.None) continue;
                if (string.Equals(v.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    category = v;
                    return true;
                }
            }
            return false;
        }
    }
}
