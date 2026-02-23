using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Manages world zones and transitions between them.
    /// Maps to Python's zones.json and map_manager with zone offsets.
    /// Each zone has a name, grid offset, and associated tilemap/spawner data.
    /// </summary>
    public class ZoneManager : MonoBehaviour
    {
        [Serializable]
        public struct ZoneDefinition
        {
            public string zoneName;
            public Vector2Int gridOffset;
            public AudioClip zoneMusic;
            public bool editableInTileEditor;
        }

        [Header("Zones")]
        [SerializeField] private List<ZoneDefinition> zones = new List<ZoneDefinition>();
        [SerializeField] private int zoneWidthTiles = 50;
        [SerializeField] private int zoneHeightTiles = 50;
        [SerializeField] private float tileSize = 1f;

        [Header("Runtime")]
        [SerializeField] private string currentZone = "Lobby";

        private readonly Dictionary<string, ZoneDefinition> _zoneMap = new Dictionary<string, ZoneDefinition>();
        private Transform _playerTransform;

        public string CurrentZone => currentZone;
        public int ZoneWidthTiles => zoneWidthTiles;
        public int ZoneHeightTiles => zoneHeightTiles;
        public float TileSize => tileSize;
        public event Action<string, string> OnZoneChanged;
        public event Action OnZonesChanged;

        private void Awake()
        {
            EnsureLegacyEditableDefaults();
            RebuildZoneMap();
        }

        private void Update()
        {
            if (_playerTransform == null)
            {
                _playerTransform = EntityRegistry.PlayerTransform;
                if (_playerTransform == null) return;
            }

            string detected = DetectZone(_playerTransform.position);
            if (!string.IsNullOrEmpty(detected) && detected != currentZone)
            {
                string oldZone = currentZone;
                currentZone = detected;
                OnZoneChanged?.Invoke(oldZone, currentZone);

                // Play zone music via AudioManager (found by type to avoid cross-asmdef dep)
                if (_zoneMap.TryGetValue(currentZone, out var def) && def.zoneMusic != null)
                {
                    PlayZoneMusic(def.zoneMusic);
                }
            }
        }

        /// <summary>
        /// Detect which zone a world position belongs to based on grid offsets.
        /// Maps to Python's zone detection from zones.json grid coordinates.
        /// </summary>
        public string DetectZone(Vector2 worldPos)
        {
            int tileX = Mathf.FloorToInt(worldPos.x / tileSize);
            int tileY = Mathf.FloorToInt(worldPos.y / tileSize);

            foreach (var z in zones)
            {
                if (ContainsTile(z, tileX, tileY))
                    return z.zoneName;
            }

            return currentZone;
        }

        public bool TryGetZone(string zoneName, out ZoneDefinition def)
        {
            return _zoneMap.TryGetValue(zoneName, out def);
        }

        public ZoneDefinition[] GetZonesSnapshot()
        {
            return zones.ToArray();
        }

        public bool TryGetZoneAtTile(Vector2Int tilePos, out ZoneDefinition zone)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (ContainsTile(z, tilePos.x, tilePos.y))
                {
                    zone = z;
                    return true;
                }
            }

            zone = default;
            return false;
        }

        public bool IsTileInEditableZone(Vector3Int cellPos)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (ContainsTile(z, cellPos.x, cellPos.y))
                    return z.editableInTileEditor;
            }

            return false;
        }

        public RectInt GetZoneRect(string zoneName)
        {
            if (!TryGetZone(zoneName, out var zone))
                return default;

            return GetZoneRect(zone);
        }

        public RectInt GetZoneRect(ZoneDefinition zone)
        {
            return new RectInt(zone.gridOffset.x, zone.gridOffset.y, zoneWidthTiles, zoneHeightTiles);
        }

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

        /// <summary>
        /// Get world position of zone center.
        /// </summary>
        public Vector2 GetZoneCenter(string zoneName)
        {
            if (_zoneMap.TryGetValue(zoneName, out var def))
            {
                float cx = (def.gridOffset.x + zoneWidthTiles * 0.5f) * tileSize;
                float cy = (def.gridOffset.y + zoneHeightTiles * 0.5f) * tileSize;
                return new Vector2(cx, cy);
            }
            return Vector2.zero;
        }

        /// <summary>
        /// Play music via IAudioService resolved through ServiceLocator.
        /// AudioManager in Infrastructure registers itself on Awake.
        /// </summary>
        private static void PlayZoneMusic(AudioClip clip)
        {
            if (clip == null) return;
            var audio = ServiceLocator.Get<IAudioService>();
            audio?.PlayMusic(clip);
        }

        private bool ContainsTile(ZoneDefinition zone, int tileX, int tileY)
        {
            int minX = zone.gridOffset.x;
            int minY = zone.gridOffset.y;
            int maxX = minX + zoneWidthTiles;
            int maxY = minY + zoneHeightTiles;
            return tileX >= minX && tileX < maxX && tileY >= minY && tileY < maxY;
        }

        private int FindZoneIndex(string zoneName)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].zoneName == zoneName)
                    return i;
            }

            return -1;
        }

        private void RebuildZoneMap()
        {
            _zoneMap.Clear();
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                if (!string.IsNullOrWhiteSpace(zone.zoneName))
                    _zoneMap[zone.zoneName] = zone;
            }
        }

        private void EnsureLegacyEditableDefaults()
        {
            if (zones.Count == 0) return;

            bool hasEditableZone = false;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].editableInTileEditor)
                {
                    hasEditableZone = true;
                    break;
                }
            }

            if (hasEditableZone) return;

            // Backward compatibility: legacy scene/prefab data had no editable flag.
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                zone.editableInTileEditor = true;
                zones[i] = zone;
            }
        }

        private static bool IsValidZoneName(string zoneName)
        {
            return !string.IsNullOrWhiteSpace(zoneName);
        }
    }
}
