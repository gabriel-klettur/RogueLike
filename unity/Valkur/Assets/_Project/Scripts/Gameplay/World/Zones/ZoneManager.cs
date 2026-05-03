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
    public partial class ZoneManager : MonoBehaviour
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
        [SerializeField] private int zoneWidthTiles = Valkur.Data.WorldConfig.LegacyChunkSize;
        [SerializeField] private int zoneHeightTiles = Valkur.Data.WorldConfig.LegacyChunkSize;
        [SerializeField] private float tileSize = 1f;

        [Header("Runtime")]
        [SerializeField] private string currentZone = "Lobby";

        private readonly Dictionary<string, ZoneDefinition> _zoneMap = new Dictionary<string, ZoneDefinition>(StringComparer.OrdinalIgnoreCase);
        private Transform _playerTransform;
        // First detection after the scene loads = spawn, not a real transition.
        // Suppresses the static GameEvents.OnZoneChanged so SaveService doesn't
        // mark the session dirty just because the player spawned away from the
        // default "Lobby" value (e.g. when loading a save in zone_100_50).
        private bool _hasDetectedInitialZone;

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
            if (string.IsNullOrEmpty(detected)) return;

            if (detected != currentZone)
            {
                string oldZone = currentZone;
                currentZone = detected;
                OnZoneChanged?.Invoke(oldZone, currentZone);

                // Only notify the static event bus on a *real* transition. The
                // very first detection after scene load is just the spawn-zone
                // catching up with the player's starting position — no progress.
                if (_hasDetectedInitialZone)
                    GameEvents.FireZoneChanged(oldZone, currentZone);

                // Notify audio system of zone change for music/ambient resolution
                var audio = ServiceLocator.Get<IAudioService>();
                if (audio != null)
                {
                    audio.OnZoneChanged(currentZone);
                }
            }
            _hasDetectedInitialZone = true;
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
    }
}
