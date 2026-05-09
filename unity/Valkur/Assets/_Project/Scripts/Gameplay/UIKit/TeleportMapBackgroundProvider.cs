using UnityEngine;

namespace Valkur.UIKit
{
    /// <summary>
    /// Round-robin selector for the per-map teleport-screen background art.
    /// Sprites live under <c>Resources/UI/teleport_map/</c> and are loaded
    /// once on first call; subsequent calls just advance the rotation
    /// pointer so a single session walking through several portals sees
    /// every authored variant before any image repeats.
    ///
    /// State is reset on Play-mode enter so the rotation starts fresh
    /// each session — Domain Reload is OFF in this project, so without
    /// the explicit reset hook the static index would survive a stop/play
    /// cycle and repeat the same image the user just saw.
    /// </summary>
    public static class TeleportMapBackgroundProvider
    {
        private const string ResourcesFolder = "UI/teleport_map";

        private static Sprite[] _sprites;
        private static int _nextIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _sprites   = null;
            _nextIndex = 0;
        }

        /// <summary>
        /// Return the next background sprite in the rotation. Returns
        /// <c>null</c> when no images are present in
        /// <c>Resources/UI/teleport_map/</c> — callers should fall back
        /// to a solid colour.
        /// </summary>
        public static Sprite NextBackground()
        {
            EnsureLoaded();
            if (_sprites == null || _sprites.Length == 0) return null;

            var sprite = _sprites[_nextIndex % _sprites.Length];
            _nextIndex = (_nextIndex + 1) % _sprites.Length;
            return sprite;
        }

        public static int Count
        {
            get
            {
                EnsureLoaded();
                return _sprites != null ? _sprites.Length : 0;
            }
        }

        private static void EnsureLoaded()
        {
            if (_sprites != null) return;
            _sprites = Resources.LoadAll<Sprite>(ResourcesFolder);
            if (_sprites == null || _sprites.Length == 0)
            {
                Debug.LogWarning(
                    $"[TeleportMapBackgroundProvider] No sprites found at " +
                    $"Resources/{ResourcesFolder}/. Loading overlay will fall " +
                    $"back to a solid colour.");
                return;
            }
            // Sort alphabetically so the rotation order is deterministic and
            // matches what a designer expects from looking at the folder.
            System.Array.Sort(_sprites,
                (a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
