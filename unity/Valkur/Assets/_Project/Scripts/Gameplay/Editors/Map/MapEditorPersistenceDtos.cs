using System;
using System.Collections.Generic;
using Valkur.Core.Persistence;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// On-disk schema for <c>map_editor_zones.json</c>. Held outside
    /// <see cref="MapEditorManager"/> rather than as a private nested class
    /// so Unity's JsonUtility serializes/deserializes it deterministically.
    /// JsonUtility has documented quirks with private nested types
    /// (especially around <c>List&lt;T&gt;</c> fields), and the runtime
    /// persistence regression that ate user-created zones traced back to
    /// that exact pattern. Keeping these DTOs at namespace scope makes the
    /// round-trip bombproof across Unity versions.
    /// </summary>
    [Serializable]
    internal class ZonePersistenceFile : IVersioned
    {
        // schemaVersion is wired up so the file can flow through a generic
        // MigrationChain&lt;ZonePersistenceFile&gt; the moment a v1.x->v2.x
        // shape change is needed. Files written before this version was
        // introduced lack the field; JsonUtility deserializes those as
        // null/empty and the migration chain treats that as "lowest
        // registered" by convention.
        public string schemaVersion = MapZonesSchema.CurrentVersion;
        public bool restrictTileEditingToEditableZones;
        public int nextZoneIndex;
        public List<ZonePersistenceEntry> zones = new List<ZonePersistenceEntry>();

        // Last known player world-position on this map. Captured every time
        // PersistZonesToDisk runs (which is on every zone op) so the slot
        // file always reflects the freshest position. When the user switches
        // back to this slot, the player teleports here instead of always
        // landing at (0,0). False → "never visited" → fall back to (0,0).
        public bool hasLastPlayerPosition;
        public float lastPlayerWorldX;
        public float lastPlayerWorldY;

        // Portals placed in this map slot via the F11 portal-placement mode.
        // Empty / null on legacy 1.0 files; the migration chain backfills
        // an empty list so downstream code never has to null-check.
        public List<PortalPersistenceEntry> portals = new List<PortalPersistenceEntry>();

        // Buildings spawned by the F11 biome generator. Re-spawned at slot
        // load so a regenerated biome survives a session restart instead of
        // disappearing the next time the user opens the map. Empty / null
        // on pre-1.2 files; the migration chain backfills an empty list.
        public List<BiomeBuildingPersistenceEntry> biomeBuildings = new List<BiomeBuildingPersistenceEntry>();

        string IVersioned.SchemaVersion
        {
            get => schemaVersion;
            set => schemaVersion = value;
        }
    }

    /// <summary>
    /// On-disk record of a building spawned by the biome generator. Stores
    /// the world-unit position rather than tile coordinates so a future zone
    /// move/rename doesn't drift the building. The instance id is preserved
    /// in case the user inspects the saved scene under a debugger; biome IDs
    /// always sit at or above
    /// <see cref="MapEditorManager.BIOME_INSTANCE_ID_BASE"/>.
    /// </summary>
    [Serializable]
    internal class BiomeBuildingPersistenceEntry
    {
        public int templateId;
        public string zoneName;
        public float worldX;
        public float worldY;
        public int instanceId;
    }

    [Serializable]
    internal class ZonePersistenceEntry
    {
        public string zoneName;
        public int gridOffsetX;
        public int gridOffsetY;
        public bool editableInTileEditor;
    }

    /// <summary>
    /// On-disk record of a placed portal. Anchored to a map slot, not a zone:
    /// the source position is given in world units (so portals stay in place
    /// even when their owning zone is renamed or moved), and the destination
    /// is referenced by zone name (resolved against the live ZoneManager at
    /// spawn time, not pinned to a tile coordinate that could be invalidated
    /// by a future zone shuffle).
    /// </summary>
    [Serializable]
    internal class PortalPersistenceEntry
    {
        // Stable, slot-unique identifier so renames and re-saves don't
        // accidentally produce ghost duplicates. Generated client-side as a
        // GUID-N when the portal is first placed.
        public string portalId;

        // Source side — where the user must walk to trigger the portal.
        public float sourceWorldX;
        public float sourceWorldY;

        // Destination side — zoneName resolves against ZoneManager at
        // spawn time; (destinationWorldX, destinationWorldY) is the world-unit
        // landing point. When destinationUseZoneCenter is true the explicit
        // coordinates are ignored and the destination zone's centre is used.
        public string destinationZoneName;
        public bool destinationUseZoneCenter;
        public float destinationWorldX;
        public float destinationWorldY;

        // Activation radius (world units). Zero / negative falls back to the
        // ZonePortal default (0.6f) at spawn time.
        public float activationRadius;
    }

    /// <summary>
    /// Schema version constants for <c>map_editor_zones.json</c>. Bump
    /// <see cref="CurrentVersion"/> any time the on-disk shape changes and
    /// register a corresponding step in the migration chain.
    /// </summary>
    internal static class MapZonesSchema
    {
        /// <summary>Pre-portals schema — only zones, restrict flag, next index,
        /// and last-known player position. Legacy files without a
        /// schemaVersion field are tagged 1.0 on first load.</summary>
        public const string V1_0 = "1.0";

        /// <summary>Adds the per-slot portals list. v1.0 files migrate by
        /// backfilling an empty list so the post-migration shape is identical
        /// to a freshly-saved v1.1 doc with no portals placed yet.</summary>
        public const string V1_1 = "1.1";

        /// <summary>Adds the per-slot biome-buildings list so biome
        /// regeneration survives session restarts. v1.1 files migrate by
        /// backfilling an empty list — same shape as a fresh v1.2 doc that
        /// has never had a biome run.</summary>
        public const string V1_2 = "1.2";

        public const string CurrentVersion = V1_2;
    }
}
