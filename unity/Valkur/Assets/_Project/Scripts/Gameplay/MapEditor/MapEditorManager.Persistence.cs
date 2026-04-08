using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Disk persistence (save / load zone data) for <see cref="MapEditorManager"/>.
    /// </summary>
    public partial class MapEditorManager
    {
        private void PersistZonesToDisk()
        {
            if (zoneManager == null) return;

            var data = new ZonePersistenceFile
            {
                restrictTileEditingToEditableZones = _state.RestrictTileEditingToEditableZones,
                nextZoneIndex = _state.NextZoneIndex
            };

            var zones = zoneManager.GetZonesSnapshot();
            for (int i = 0; i < zones.Length; i++)
            {
                data.zones.Add(new ZonePersistenceEntry
                {
                    zoneName         = zones[i].zoneName,
                    gridOffsetX      = zones[i].gridOffset.x,
                    gridOffsetY      = zones[i].gridOffset.y,
                    editableInTileEditor = zones[i].editableInTileEditor
                });
            }

            try
            {
                string json = JsonUtility.ToJson(data, prettyPrint: true);
                File.WriteAllText(PersistencePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Failed to persist zones to '{PersistencePath}': {ex.Message}");
            }
        }

        private void LoadZonesFromDisk()
        {
            if (zoneManager == null || !File.Exists(PersistencePath)) return;

            try
            {
                string json = File.ReadAllText(PersistencePath);
                var data = JsonUtility.FromJson<ZonePersistenceFile>(json);
                if (data?.zones == null || data.zones.Count == 0) return;

                var existingZones = zoneManager.GetZonesSnapshot();
                var musicByName   = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < existingZones.Length; i++)
                    musicByName[existingZones[i].zoneName] = existingZones[i].zoneMusic;

                var newZones = new List<ZoneManager.ZoneDefinition>(data.zones.Count);
                for (int i = 0; i < data.zones.Count; i++)
                {
                    var entry = data.zones[i];
                    AudioClip zoneMusic = null;
                    if (!string.IsNullOrWhiteSpace(entry.zoneName))
                        musicByName.TryGetValue(entry.zoneName, out zoneMusic);

                    newZones.Add(new ZoneManager.ZoneDefinition
                    {
                        zoneName         = entry.zoneName,
                        gridOffset       = new Vector2Int(entry.gridOffsetX, entry.gridOffsetY),
                        zoneMusic        = zoneMusic,
                        editableInTileEditor = entry.editableInTileEditor
                    });
                }

                zoneManager.ReplaceZones(newZones);
                _state.RestrictTileEditingToEditableZones = data.restrictTileEditingToEditableZones;
                _state.NextZoneIndex = Mathf.Max(1, data.nextZoneIndex);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Failed to load persisted zones from '{PersistencePath}': {ex.Message}");
            }
        }
    }
}
