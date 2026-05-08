using System;
using System.IO;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// File-backed source of truth for the Map Editor's active map slot.
    ///
    /// The Map Editor (<c>Valkur.Gameplay.MapEditor.MapEditorMapSlots</c>) writes
    /// the active slot name to <c>persistentDataPath/Maps/_active.txt</c>. Any
    /// subsystem that needs to route per-slot data on disk (BuildingLoader at
    /// boot, BuildingsRuntimeEditor on save, future per-slot light / spawner /
    /// particle persistence) reads it through this helper rather than reaching
    /// into the MapEditor singleton, which:
    ///   • lives in a different assembly (Gameplay) than some callers
    ///     (Infrastructure repos);
    ///   • may not exist yet at boot time (BuildingLoader.Start() runs before
    ///     the editor singleton instance is created).
    ///
    /// The "default" slot keeps the legacy <c>StreamingAssets/Buildings/...</c>
    /// path so existing builds continue to load the baseline world unchanged.
    /// Custom slots are routed to <c>persistentDataPath/Maps/&lt;slot&gt;/Buildings/...</c>
    /// — runtime-writable on every Unity target, and isolated per slot so editing
    /// one map can never silently nuke another.
    /// </summary>
    public static class MapEditorActiveSlot
    {
        public const string DEFAULT_SLOT = "default";

        // Filenames must match Valkur.Gameplay.MapEditor.MapEditorMapSlots.
        // We re-declare them here (rather than reference) to keep Core free of
        // a Gameplay→Core cycle.
        private const string MAPS_DIR_NAME = "Maps";
        private const string ACTIVE_FILE   = "_active.txt";
        private const string BUILDINGS_DIR = "Buildings";

        // ── Override hook for tests ────────────────────────────────────────────
        //
        // Tests need to pin a specific slot without writing _active.txt to the
        // real persistentDataPath (which would leak into other fixtures and the
        // editor itself). Setting this to non-null short-circuits the file read.
        // Production code never touches it.
        //
        // Domain-reload safety: Enter Play Mode Options has Disable Domain Reload
        // ON for fast iteration. Without the SubsystemRegistration reset below,
        // a test crash (before TearDown clears the overrides) would leave these
        // statics non-null and cause the production save to route to a temp
        // directory, silently dropping saves into the wrong slot.

        private static string s_overrideForTests;
        private static string s_persistentRootOverrideForTests;
        private static string s_streamingRootOverrideForTests;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            // Production runtime never sets these; only tests do. Clearing them on
            // every Play mode entry ensures a crashed test fixture cannot leak its
            // override into a subsequent manual session (Domain Reload OFF).
            s_overrideForTests            = null;
            s_persistentRootOverrideForTests = null;
            s_streamingRootOverrideForTests  = null;
        }

        /// <summary>For test fixtures only. Pass null to revert to the file-backed value.</summary>
        public static void SetOverrideForTests(string slotOrNull) => s_overrideForTests = slotOrNull;

        /// <summary>For test fixtures only. Pin the persistent-data root used by <see cref="BuildingsDir"/>.</summary>
        public static void SetPersistentRootOverrideForTests(string rootOrNull) => s_persistentRootOverrideForTests = rootOrNull;

        /// <summary>For test fixtures only. Pin the streaming-assets root used by <see cref="BuildingsDir"/>.</summary>
        public static void SetStreamingRootOverrideForTests(string rootOrNull) => s_streamingRootOverrideForTests = rootOrNull;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the active slot from <c>persistentDataPath/Maps/_active.txt</c>.
        /// Returns <see cref="DEFAULT_SLOT"/> when the file is absent, empty, or
        /// unreadable so callers can safely use the result without a null check.
        /// </summary>
        public static string Read()
        {
            if (s_overrideForTests != null) return s_overrideForTests;
            try
            {
                string path = Path.Combine(PersistentRoot, MAPS_DIR_NAME, ACTIVE_FILE);
                if (!File.Exists(path)) return DEFAULT_SLOT;
                string raw = File.ReadAllText(path)?.Trim();
                return string.IsNullOrEmpty(raw) ? DEFAULT_SLOT : raw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditorActiveSlot] Failed to read active slot: {ex.Message}");
                return DEFAULT_SLOT;
            }
        }

        /// <summary>
        /// True if <paramref name="slot"/> is the implicit baseline slot. Treats
        /// null / empty / "default" (case-insensitive) as default so callers
        /// don't need to repeat the comparison.
        /// </summary>
        public static bool IsDefault(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot)) return true;
            return string.Equals(slot, DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the directory under which all Buildings JSON files
        /// (<c>buildings_instances.json</c>, the two collider stores, ...) live
        /// for the given <paramref name="slot"/>.
        ///
        /// Default slot keeps the legacy <c>StreamingAssets/Buildings/</c> path
        /// — the baseline world ships with the build, and any custom slot the
        /// user creates lives independently under
        /// <c>persistentDataPath/Maps/&lt;slot&gt;/Buildings/</c>.
        /// </summary>
        public static string BuildingsDir(string slot)
        {
            if (IsDefault(slot))
                return Path.Combine(StreamingRoot, BUILDINGS_DIR);
            return Path.Combine(PersistentRoot, MAPS_DIR_NAME, slot, BUILDINGS_DIR);
        }

        /// <summary>Convenience wrapper: <see cref="BuildingsDir"/> for the active slot.</summary>
        public static string BuildingsDirForActiveSlot() => BuildingsDir(Read());

        // ── Internals ─────────────────────────────────────────────────────────

        private static string PersistentRoot
            => s_persistentRootOverrideForTests ?? Application.persistentDataPath;

        private static string StreamingRoot
            => s_streamingRootOverrideForTests ?? Application.streamingAssetsPath;
    }
}
