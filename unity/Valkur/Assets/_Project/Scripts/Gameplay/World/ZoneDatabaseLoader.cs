using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Reads StreamingAssets/Maps/zones_database.json and populates ZoneManager
    /// with all zone definitions from the Python base world.
    /// Maps to Python's zones.json + ZonesService auto-expand.
    /// </summary>
    public class ZoneDatabaseLoader : MonoBehaviour
    {
        private const string DATABASE_FILE = "Maps/zones_database.json";

        [Tooltip("ZoneManager to populate. Found automatically if null.")]
        [SerializeField] private ZoneManager _zoneManager;

        [Tooltip("Load zone database automatically on Start.")]
        [SerializeField] private bool _autoLoad = true;

        /// <summary>Zone width in tiles (read from JSON). Default 50.</summary>
        public int ZoneWidthTiles { get; private set; } = 50;

        /// <summary>Zone height in tiles (read from JSON). Default 50.</summary>
        public int ZoneHeightTiles { get; private set; } = 50;

        /// <summary>Minimum X offset found (for negative zone support).</summary>
        public int WorldOriginX { get; private set; }

        /// <summary>Minimum Y offset found.</summary>
        public int WorldOriginY { get; private set; }

        /// <summary>All zone entries loaded from the database.</summary>
        public IReadOnlyList<ZoneEntry> Entries => _entries;

        private readonly List<ZoneEntry> _entries = new List<ZoneEntry>();

        [Serializable]
        public struct ZoneEntry
        {
            public string name;
            public int offsetX;
            public int offsetY;
            public string overlayFile;
            public string collisionFile;
        }

        private void Start()
        {
            if (_autoLoad)
                LoadDatabase();
        }

        /// <summary>
        /// Parse zones_database.json, populate ZoneManager with all zone definitions,
        /// and store entries for later use by WorldLoader.
        /// </summary>
        public void LoadDatabase()
        {
            _entries.Clear();

            if (_zoneManager == null)
            {
                _zoneManager = FindObjectOfType<ZoneManager>();
                if (_zoneManager == null)
                {
                    Debug.LogError("[ZoneDatabaseLoader] ZoneManager not found.", this);
                    return;
                }
            }

            string jsonPath = Path.Combine(Application.streamingAssetsPath, DATABASE_FILE);
            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[ZoneDatabaseLoader] Database not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var root = MiniJsonRuntime.Deserialize(json) as Dictionary<string, object>;
            if (root == null)
            {
                Debug.LogError("[ZoneDatabaseLoader] Failed to parse zones_database.json.");
                return;
            }

            // Read global settings
            if (root.TryGetValue("zone_width_tiles", out var zw))
                ZoneWidthTiles = Convert.ToInt32(zw);
            if (root.TryGetValue("zone_height_tiles", out var zh))
                ZoneHeightTiles = Convert.ToInt32(zh);
            if (root.TryGetValue("world_origin_x", out var ox))
                WorldOriginX = Convert.ToInt32(ox);
            if (root.TryGetValue("world_origin_y", out var oy))
                WorldOriginY = Convert.ToInt32(oy);

            var zonesArray = root.GetValueOrDefault("zones") as List<object>;
            if (zonesArray == null)
            {
                Debug.LogError("[ZoneDatabaseLoader] Missing 'zones' array in database.");
                return;
            }

            var zoneDefs = new List<ZoneManager.ZoneDefinition>();
            foreach (var item in zonesArray)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null) continue;

                string name = GetString(dict, "name");
                int offX = GetInt(dict, "offset_x");
                int offY = GetInt(dict, "offset_y");
                string overlay = GetStringOrNull(dict, "overlay");
                string collision = GetStringOrNull(dict, "collision");

                _entries.Add(new ZoneEntry
                {
                    name = name,
                    offsetX = offX,
                    offsetY = offY,
                    overlayFile = overlay,
                    collisionFile = collision,
                });

                zoneDefs.Add(new ZoneManager.ZoneDefinition
                {
                    zoneName = name,
                    gridOffset = new Vector2Int(offX, offY),
                    zoneMusic = null,
                    editableInTileEditor = true,
                });
            }

            _zoneManager.ReplaceZones(zoneDefs);
            Debug.Log($"[ZoneDatabaseLoader] Loaded {zoneDefs.Count} zones into ZoneManager " +
                      $"(origin [{WorldOriginX},{WorldOriginY}], zone size {ZoneWidthTiles}x{ZoneHeightTiles}).");
        }

        private static string GetString(Dictionary<string, object> d, string key, string fallback = "")
        {
            if (d.TryGetValue(key, out var v) && v is string s)
                return s;
            return fallback;
        }

        private static string GetStringOrNull(Dictionary<string, object> d, string key)
        {
            if (d.TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s))
                return s;
            return null;
        }

        private static int GetInt(Dictionary<string, object> d, string key, int fallback = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToInt32(v);
            return fallback;
        }
    }
}
