using System;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    public partial class ZoneManager
    {
        /// <summary>
        /// Override the current zone name without detection (used by ZonePortal on overlay swap).
        /// </summary>
        public void ForceZoneName(string zoneName)
        {
            if (string.IsNullOrWhiteSpace(zoneName)) return;
            string old = currentZone;
            currentZone = zoneName;
            if (old != zoneName)
            {
                OnZoneChanged?.Invoke(old, currentZone);
                GameEvents.FireZoneChanged(old, currentZone);
            }
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
            // Case-insensitive to stay consistent with _zoneMap (built with
            // StringComparer.OrdinalIgnoreCase). A case-sensitive miss here
            // would let RenameZone/RemoveZone/MoveZone/SetZoneEditable
            // silently no-op while the zone remained reachable through
            // _zoneMap by a differently-cased lookup.
            for (int i = 0; i < zones.Count; i++)
            {
                if (string.Equals(zones[i].zoneName, zoneName, StringComparison.OrdinalIgnoreCase))
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

        private string GenerateUniqueDuplicateName(string sourceZoneName)
        {
            string seed = $"{sourceZoneName}_copy";
            if (!_zoneMap.ContainsKey(seed))
                return seed;

            for (int i = 2; i < 10000; i++)
            {
                string candidate = $"{seed}{i}";
                if (!_zoneMap.ContainsKey(candidate))
                    return candidate;
            }

            return $"{seed}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
        }
    }
}
