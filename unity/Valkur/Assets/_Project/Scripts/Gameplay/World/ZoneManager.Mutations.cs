using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    public partial class ZoneManager
    {
        public bool AddZone(string zoneName, Vector2Int gridOffset, bool editableInTileEditor = true)
        {
            if (!IsValidZoneName(zoneName) || _zoneMap.ContainsKey(zoneName))
                return false;

            zones.Add(new ZoneDefinition
            {
                zoneName = zoneName,
                gridOffset = gridOffset,
                zoneMusic = null,
                editableInTileEditor = editableInTileEditor
            });

            RebuildZoneMap();
            OnZonesChanged?.Invoke();
            return true;
        }

        public bool AddZoneFromTemplate(string sourceZoneName, string newZoneName, Vector2Int gridOffset, bool? editableOverride = null)
        {
            if (!IsValidZoneName(sourceZoneName) || !IsValidZoneName(newZoneName) || _zoneMap.ContainsKey(newZoneName))
                return false;

            int idx = FindZoneIndex(sourceZoneName);
            if (idx < 0)
                return false;

            var source = zones[idx];
            zones.Add(new ZoneDefinition
            {
                zoneName = newZoneName,
                gridOffset = gridOffset,
                zoneMusic = source.zoneMusic,
                editableInTileEditor = editableOverride ?? source.editableInTileEditor
            });

            RebuildZoneMap();
            OnZonesChanged?.Invoke();
            return true;
        }

        public bool DuplicateZone(string sourceZoneName, out string duplicatedZoneName)
        {
            duplicatedZoneName = null;
            if (!IsValidZoneName(sourceZoneName))
                return false;

            int idx = FindZoneIndex(sourceZoneName);
            if (idx < 0)
                return false;

            var source = zones[idx];
            duplicatedZoneName = GenerateUniqueDuplicateName(source.zoneName);

            zones.Add(new ZoneDefinition
            {
                zoneName = duplicatedZoneName,
                gridOffset = source.gridOffset,
                zoneMusic = source.zoneMusic,
                editableInTileEditor = source.editableInTileEditor
            });

            RebuildZoneMap();
            OnZonesChanged?.Invoke();
            return true;
        }

        public void ReplaceZones(IReadOnlyList<ZoneDefinition> newZones)
        {
            zones.Clear();
            if (newZones != null)
            {
                for (int i = 0; i < newZones.Count; i++)
                    zones.Add(newZones[i]);
            }

            EnsureLegacyEditableDefaults();
            RebuildZoneMap();
            OnZonesChanged?.Invoke();
        }

        public bool RemoveZone(string zoneName)
        {
            int idx = FindZoneIndex(zoneName);
            if (idx < 0) return false;

            zones.RemoveAt(idx);
            RebuildZoneMap();

            if (currentZone == zoneName && zones.Count > 0)
                currentZone = zones[0].zoneName;

            OnZonesChanged?.Invoke();
            return true;
        }

        public bool RenameZone(string oldZoneName, string newZoneName)
        {
            if (!IsValidZoneName(oldZoneName) || !IsValidZoneName(newZoneName) || oldZoneName == newZoneName)
                return false;
            if (_zoneMap.ContainsKey(newZoneName))
                return false;

            int idx = FindZoneIndex(oldZoneName);
            if (idx < 0) return false;

            var zone = zones[idx];
            zone.zoneName = newZoneName;
            zones[idx] = zone;

            if (currentZone == oldZoneName)
                currentZone = newZoneName;

            RebuildZoneMap();
            OnZonesChanged?.Invoke();
            return true;
        }

        public bool MoveZone(string zoneName, Vector2Int delta)
        {
            int idx = FindZoneIndex(zoneName);
            if (idx < 0) return false;

            var zone = zones[idx];
            zone.gridOffset += delta;
            zones[idx] = zone;

            RebuildZoneMap();
            OnZonesChanged?.Invoke();
            return true;
        }

        public bool SetZoneEditable(string zoneName, bool editable)
        {
            int idx = FindZoneIndex(zoneName);
            if (idx < 0) return false;

            var zone = zones[idx];
            if (zone.editableInTileEditor == editable)
                return true;

            zone.editableInTileEditor = editable;
            zones[idx] = zone;
            RebuildZoneMap();
            OnZonesChanged?.Invoke();
            return true;
        }
    }
}
