using System.IO;
using UnityEngine;
using Valkur.Gameplay.MapEditor;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// Where a baked world is written. A map slot is two things on disk: its zone list
    /// (<c>Maps/&lt;slot&gt;.zones.json</c>) and its per-zone tile overlays
    /// (<c>MapOverrides/&lt;slot&gt;/</c>). Both roots are injectable so a test writes to a
    /// temporary folder and never to the author's machine state.
    /// </summary>
    public sealed class SeedWorldBakeRequest
    {
        /// <summary>The overlay directory marker that says "Seed World owns this slot".</summary>
        public const string MarkerFileName = "_seedworld.json";

        /// <summary>Zones are this many tiles square, which is what every loader in the project assumes.</summary>
        public const int DefaultZoneSize = 50;

        public string Slot { get; }
        public string MapsDirectory { get; }
        public string OverridesDirectory { get; }
        public int ZoneSize { get; }

        /// <summary>True when the roots are the real <c>persistentDataPath</c> ones.</summary>
        public bool UsesRealRoots { get; }

        private SeedWorldBakeRequest(string slot, string mapsDir, string overridesDir, int zoneSize, bool real)
        {
            Slot = slot;
            MapsDirectory = mapsDir;
            OverridesDirectory = overridesDir;
            ZoneSize = zoneSize;
            UsesRealRoots = real;
        }

        /// <summary>
        /// The production target for <paramref name="slotName"/>, or null when the name is not a
        /// usable slot. <c>default</c> is refused: it maps to the BASE world, whose overlays live
        /// in the flat <c>MapOverrides/</c> root beside the authored world's own edits.
        /// </summary>
        public static SeedWorldBakeRequest ForSlot(string slotName)
        {
            string clean = CleanSlot(slotName);
            if (clean == null) return null;
            string persistent = Application.persistentDataPath;
            return new SeedWorldBakeRequest(clean,
                Path.Combine(persistent, "Maps"),
                Path.Combine(persistent, "MapOverrides", clean),
                DefaultZoneSize, real: true);
        }

        /// <summary>A target under <paramref name="root"/>, for tests.</summary>
        public static SeedWorldBakeRequest ForTest(string slotName, string root, int zoneSize = DefaultZoneSize)
        {
            string clean = CleanSlot(slotName);
            if (clean == null) return null;
            return new SeedWorldBakeRequest(clean,
                Path.Combine(root, "Maps"),
                Path.Combine(root, "MapOverrides", clean),
                zoneSize, real: false);
        }

        public string SlotFilePath => Path.Combine(MapsDirectory, Slot + ".zones.json");
        public string MarkerPath => Path.Combine(OverridesDirectory, MarkerFileName);

        private static string CleanSlot(string slotName)
        {
            string clean = MapEditorMapSlots.Sanitize(slotName);
            if (string.IsNullOrEmpty(clean)) return null;
            if (string.Equals(clean, MapEditorMapSlots.DEFAULT_SLOT, System.StringComparison.OrdinalIgnoreCase)) return null;
            return clean;
        }
    }
}
