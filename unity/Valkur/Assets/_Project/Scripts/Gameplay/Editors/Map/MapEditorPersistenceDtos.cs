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

        string IVersioned.SchemaVersion
        {
            get => schemaVersion;
            set => schemaVersion = value;
        }
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
    /// Schema version constants for <c>map_editor_zones.json</c>. Bump
    /// <see cref="CurrentVersion"/> any time the on-disk shape changes and
    /// register a corresponding step in the migration chain.
    /// </summary>
    internal static class MapZonesSchema
    {
        /// <summary>Initial versioned schema. Pre-versioned files are
        /// indistinguishable from this layout — they're tagged 1.0 on first
        /// load and proceed normally.</summary>
        public const string CurrentVersion = "1.0";
    }
}
