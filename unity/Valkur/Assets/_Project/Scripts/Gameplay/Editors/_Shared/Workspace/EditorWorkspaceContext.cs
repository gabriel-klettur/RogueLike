using UnityEngine;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Editors.Workspace
{
    /// <summary>
    /// Where the author currently is: which map slot, which zone.
    ///
    /// A remembered selection is only worth resolving in the context it was taken in, so
    /// every editor that persists one has to answer the same two questions. Fifteen
    /// editors each reaching into <c>MapEditorManager</c> and <c>ZoneManager</c> their own
    /// way is fifteen chances to disagree about what "no slot" means — and a disagreement
    /// here does not fail loudly, it silently restores a selection from the wrong world.
    ///
    /// Both getters are null-safe by design: an editor may be opened before either manager
    /// exists (bootstrap order is a coroutine), and in EditMode tests neither is present.
    /// The empty string is the documented "not scoped" value that
    /// <see cref="Valkur.Core.Editors.EditorSelectionRecord.AppliesTo"/> treats as matching
    /// anything, so an editor with no world context degrades to "always applies" rather
    /// than to "never applies".
    /// </summary>
    public static class EditorWorkspaceContext
    {
        /// <summary>The active map slot, or empty when there is no Map editor yet.</summary>
        public static string CurrentMapSlot
        {
            get
            {
                var mgr = MapEditorManager.HasInstance ? MapEditorManager.Instance : null;
                if (mgr == null) return string.Empty;
                return mgr.ActiveMapSlot ?? string.Empty;
            }
        }

        /// <summary>
        /// The zone the player is standing in, or empty when zone detection is unavailable
        /// or suspended.
        ///
        /// Suspended matters: <c>ZoneManager.SuspendDetection</c> is what an interior
        /// overlay uses, and during it <c>CurrentZone</c> holds whatever outdoor zone was
        /// last detected — which is not where the author is. Reporting empty there means a
        /// selection taken inside a house is not silently filed under the field outside it.
        /// </summary>
        public static string CurrentZone
        {
            get
            {
                var zm = ResolveZoneManager();
                if (zm == null) return string.Empty;
                if (zm.IsDetectionSuspended) return string.Empty;
                return zm.CurrentZone ?? string.Empty;
            }
        }

        private static ZoneManager _zones;

        /// <summary>
        /// <see cref="ZoneManager"/> is a plain MonoBehaviour, not a
        /// <c>SingletonMonoBehaviour</c>, so the project reaches it with
        /// <c>FindObjectOfType</c> — from eight separate call sites today. Cached here so
        /// folding those into one helper does not also multiply the scans; the Unity null
        /// check re-finds it after a scene load destroyed the previous one.
        /// </summary>
        private static ZoneManager ResolveZoneManager()
        {
            if (_zones == null) _zones = Object.FindObjectOfType<ZoneManager>();
            return _zones;
        }

        /// <summary>
        /// Domain Reload is OFF, so a cached manager from the previous Play session would
        /// otherwise still be referenced here — destroyed, and answering a Unity-null that
        /// only the explicit check above recovers from. Clear it outright.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCacheOnPlayModeEnter()
        {
            _zones = null;
        }
    }
}
