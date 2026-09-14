using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Data.WorldGen;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// A live Seed World (phase 5), opened from its slot's marker: the plan rebuilt from the
    /// saved settings, and the one method that turns a zone coordinate into that zone's overlay.
    /// Pure of scene state, so a fixture can compare it zone for zone against a bake.
    ///
    /// <para><b>The seed is the save.</b> The marker holds the settings and nothing generated; the
    /// ground of a zone the player never visits is never computed, and the ground of one they
    /// revisit is computed again, identically. Only a zone somebody EDITS is written, by the Tile
    /// editor, as the ordinary overlay file every other map uses — and that file wins over the
    /// generator from then on (<see cref="OverlayPathFor"/>).</para>
    /// </summary>
    public sealed class SeedWorldLiveWorld
    {
        public readonly string Slot;
        public readonly SeedWorldPlan Plan;
        public readonly string OverridesDirectory;
        public readonly SeedWorldZoneBuilder.Stats Stats = new SeedWorldZoneBuilder.Stats();

        private readonly SeedWorldTilePalette _palette;

        public SeedWorldLiveWorld(string slot, WorldGenSettings settings, int zoneSize, string overridesDirectory,
                                  SeedWorldTilePalette palette)
        {
            Slot = slot;
            OverridesDirectory = overridesDirectory;
            _palette = palette;
            Plan = SeedWorldPlan.Create(settings, zoneSize, palette.Compatible);
        }

        /// <summary>
        /// The live world behind <paramref name="request"/>'s slot, or null when the slot is not a
        /// live Seed World (no marker, a baked one, or unreadable settings).
        /// </summary>
        public static SeedWorldLiveWorld TryOpen(SeedWorldBakeRequest request, SeedWorldTilePalette palette, out string error)
        {
            error = null;
            if (request == null) { error = "sin slot"; return null; }
            if (palette == null || !palette.HasCatalog) { error = "sin TerrainCatalog"; return null; }
            if (!File.Exists(request.MarkerPath)) { error = "no es un mundo de Seed World"; return null; }

            SeedWorldMarker marker;
            try { marker = JsonUtility.FromJson<SeedWorldMarker>(File.ReadAllText(request.MarkerPath)); }
            catch (System.Exception ex) { error = "marcador ilegible: " + ex.Message; return null; }

            if (marker == null || !marker.live) { error = "mundo horneado"; return null; }
            var settings = WorldGenSettings.FromJson(marker.settingsJson);
            if (settings == null) { error = "ajustes ilegibles"; return null; }

            int zoneSize = marker.zoneSize > 0 ? marker.zoneSize : request.ZoneSize;
            return new SeedWorldLiveWorld(request.Slot, settings, zoneSize, request.OverridesDirectory, palette);
        }

        public int ZoneSize => Plan.ZoneSize;

        /// <summary>Where an edited zone of this world lives on disk.</summary>
        public string OverlayPathFor(int zx, int zy)
            => Path.Combine(OverridesDirectory, Plan.ZoneNames[zx, zy] + ".overlay.json");

        /// <summary>The zone as the generator makes it: never reads disk.</summary>
        public SeedWorldZoneBuilder.ZoneContent GenerateZone(int zx, int zy)
        {
            int z = Plan.ZoneSize;
            int baseX = zx * z, baseY = zy * z;
            var grid = Plan.BuildRegion(baseX, baseY, z, z, _palette.Compatible);
            return SeedWorldZoneBuilder.Build(grid, _palette, Plan.Settings, baseX, baseY, z, Plan.Origin, Stats);
        }

        /// <summary>
        /// The overlay tree for a zone: the edited file when one exists, otherwise generated.
        /// <paramref name="fromDisk"/> says which, because an edited zone is the author's and must
        /// not be treated as disposable ground.
        /// </summary>
        public Dictionary<string, object> ZoneOverlay(int zx, int zy, out bool fromDisk)
        {
            string path = OverlayPathFor(zx, zy);
            if (File.Exists(path))
            {
                var parsed = OverlayLoader.ParseOverlay(path);
                if (parsed != null) { fromDisk = true; return parsed; }
                Debug.LogWarning($"[SeedWorldLive] '{path}' did not parse; generating the zone instead.");
            }
            fromDisk = false;
            return SeedWorldZoneBuilder.ToOverlayRoot(GenerateZone(zx, zy));
        }
    }
}
