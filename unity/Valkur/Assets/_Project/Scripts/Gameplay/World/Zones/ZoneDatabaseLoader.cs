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

        [Tooltip("Shift every zone offset by -world_origin so the world's south-west corner sits at (0,0). " +
                 "Mirrors Python's ZonesService normalization. Disable only for debugging.")]
        [SerializeField] private bool _normalizeToOrigin = true;

        /// <summary>Zone width in tiles (read from JSON). Default 50.</summary>
        public int ZoneWidthTiles { get; private set; } = 50;

        /// <summary>Zone height in tiles (read from JSON). Default 50.</summary>
        public int ZoneHeightTiles { get; private set; } = 50;

        /// <summary>Minimum X offset found (for negative zone support). Read from <c>world_origin_x</c>.</summary>
        public int WorldOriginX { get; private set; }

        /// <summary>Minimum Y offset found. Read from <c>world_origin_y</c>.</summary>
        public int WorldOriginY { get; private set; }

        /// <summary>X-shift actually applied to zone offsets during normalization (0 if normalization disabled).</summary>
        public int AppliedOriginShiftX { get; private set; }

        /// <summary>Y-shift actually applied to zone offsets after the global Y-flip (0 if normalization disabled or unsupported).</summary>
        public int AppliedOriginShiftY { get; private set; }

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

            // --- Global Y-flip ---
            // Python uses Y-down (row 0 = top/north), Unity uses Y-up.
            // OverlayLoader already flips tiles WITHIN each zone, but zone offset Y
            // values from the JSON are still Python-style (higher Y = further south).
            // We flip all zone offset Y's so that south-in-Python = south-in-Unity.
            // Formula: flippedY = maxWorldY - offsetY - zoneHeight
            int maxWorldY = 0;
            for (int i = 0; i < _entries.Count; i++)
                maxWorldY = Mathf.Max(maxWorldY, _entries[i].offsetY + ZoneHeightTiles);

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                int flippedY = maxWorldY - e.offsetY - ZoneHeightTiles;
                _entries[i] = new ZoneEntry
                {
                    name = e.name,
                    offsetX = e.offsetX,
                    offsetY = flippedY,
                    overlayFile = e.overlayFile,
                    collisionFile = e.collisionFile,
                };
                zoneDefs[i] = new ZoneManager.ZoneDefinition
                {
                    zoneName = e.name,
                    gridOffset = new Vector2Int(e.offsetX, flippedY),
                    zoneMusic = zoneDefs[i].zoneMusic,
                    editableInTileEditor = zoneDefs[i].editableInTileEditor,
                };
            }

            // --- World-origin normalization ---
            // Python's ZonesService shifts every zone so the south-west corner of
            // the world sits at (0,0). Mirror that here so consumers (Tilemap bounds,
            // ZoneManager.DetectZone, persistence, minimap, etc.) never see negative
            // tile coordinates. All offsets shift by the same delta, so RELATIVE
            // positions (lobby↔dungeon distance, player spawn relative to lobby) are
            // preserved.
            AppliedOriginShiftX = 0;
            AppliedOriginShiftY = 0;
            if (_normalizeToOrigin && WorldOriginX != 0)
            {
                int shiftX = -WorldOriginX;
                AppliedOriginShiftX = shiftX;
                for (int i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    e.offsetX += shiftX;
                    _entries[i] = e;
                    var d = zoneDefs[i];
                    d.gridOffset = new Vector2Int(e.offsetX, d.gridOffset.y);
                    zoneDefs[i] = d;
                }
            }
            if (_normalizeToOrigin && WorldOriginY != 0)
            {
                // world_origin_y is in pre-flip Python space (Y-down). In current
                // databases this is always 0 so we have no real-world test coverage
                // for the post-flip shift. Log and skip rather than guess.
                Debug.LogWarning("[ZoneDatabaseLoader] world_origin_y != 0 normalization is not yet implemented; leaving Y unshifted.");
            }

            // --- Overlap diagnostics ---
            // After all transforms, scan for any two zones whose [offsetX..+W, offsetY..+H]
            // rectangles intersect. This catches malformed databases at load time instead
            // of letting tiles silently overpaint each other.
            DetectAndReportOverlaps();

            _zoneManager.ReplaceZones(zoneDefs);
            Debug.Log($"[ZoneDatabaseLoader] Loaded {zoneDefs.Count} zones into ZoneManager " +
                      $"(origin [{WorldOriginX},{WorldOriginY}], shift applied [{AppliedOriginShiftX},{AppliedOriginShiftY}], " +
                      $"zone size {ZoneWidthTiles}x{ZoneHeightTiles}, Y-flipped with maxWorldY={maxWorldY}).");
        }

        /// <summary>
        /// Logs an error for every pair of zones whose footprints overlap.
        /// Intended as a defensive load-time check — overlapping zones cause
        /// tiles to stack on top of each other on the same Tilemap layer.
        /// </summary>
        private void DetectAndReportOverlaps()
        {
            int reported = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                var a = _entries[i];
                int axMin = a.offsetX, axMax = a.offsetX + ZoneWidthTiles - 1;
                int ayMin = a.offsetY, ayMax = a.offsetY + ZoneHeightTiles - 1;
                for (int j = i + 1; j < _entries.Count; j++)
                {
                    var b = _entries[j];
                    int bxMin = b.offsetX, bxMax = b.offsetX + ZoneWidthTiles - 1;
                    int byMin = b.offsetY, byMax = b.offsetY + ZoneHeightTiles - 1;
                    bool overlap = axMin <= bxMax && bxMin <= axMax && ayMin <= byMax && byMin <= ayMax;
                    if (!overlap) continue;
                    Debug.LogError($"[ZoneDatabaseLoader] Zone overlap detected: '{a.name}' " +
                                   $"[{axMin}..{axMax},{ayMin}..{ayMax}] intersects '{b.name}' " +
                                   $"[{bxMin}..{bxMax},{byMin}..{byMax}]. Fix zones_database.json.");
                    reported++;
                }
            }
            if (reported == 0)
                Debug.Log($"[ZoneDatabaseLoader] Overlap check passed: {_entries.Count} zones, no intersections.");
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
