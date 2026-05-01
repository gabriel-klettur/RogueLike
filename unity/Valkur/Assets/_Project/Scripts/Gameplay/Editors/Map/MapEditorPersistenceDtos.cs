using System;
using System.Collections.Generic;

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
    internal class ZonePersistenceFile
    {
        public bool restrictTileEditingToEditableZones;
        public int nextZoneIndex;
        public List<ZonePersistenceEntry> zones = new List<ZonePersistenceEntry>();
    }

    [Serializable]
    internal class ZonePersistenceEntry
    {
        public string zoneName;
        public int gridOffsetX;
        public int gridOffsetY;
        public bool editableInTileEditor;
    }
}
