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
        public event Action<string, string> OnZoneChanged;

        private void Awake()
        {
            foreach (var z in zones)
                _zoneMap[z.zoneName] = z;
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
                int minX = z.gridOffset.x;
                int minY = z.gridOffset.y;
                int maxX = minX + zoneWidthTiles;
                int maxY = minY + zoneHeightTiles;

                if (tileX >= minX && tileX < maxX && tileY >= minY && tileY < maxY)
                    return z.zoneName;
            }

            return currentZone;
        }

        public bool TryGetZone(string zoneName, out ZoneDefinition def)
        {
            return _zoneMap.TryGetValue(zoneName, out def);
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
    }
}
