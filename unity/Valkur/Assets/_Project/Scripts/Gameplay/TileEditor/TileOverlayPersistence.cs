using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Persists tile-editor edits to disk on a per-zone basis and re-applies them on game load.
    /// <para>
    /// Mirrors the Python tile→zone→map model: each zone owns one overlay JSON file with the
    /// same schema as <c>python/data/worlds/base/zones/overlays/{zone}.overlay.json</c>
    /// (a <c>{ "layers": { "Ground": [[...]], ... } }</c> matrix per layer).
    /// </para>
    /// <para>
    /// Storage is non-destructive: edits go to <c>Application.persistentDataPath/MapOverrides/</c>,
    /// not to the original <c>StreamingAssets/Maps</c> files. Use the editor menu
    /// <c>Valkur > Tile Editor > Bake Overrides into StreamingAssets</c> to promote them.
    /// </para>
    /// </summary>
    public class TileOverlayPersistence
    {
        private const string OVERRIDE_DIR_NAME = "MapOverrides";
        private const string OVERRIDE_EXTENSION = ".overlay.json";

        private readonly ZoneManager _zones;
        private readonly WorldGridBuilder _grid;
        private readonly HashSet<string> _dirtyZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public event Action OnDirtyChanged;
        public event Action<string> OnZoneSaved;
        public event Action<string, Exception> OnSaveFailed;

        public bool HasUnsavedChanges => _dirtyZones.Count > 0;
        public int DirtyZoneCount => _dirtyZones.Count;
        public IReadOnlyCollection<string> DirtyZones => _dirtyZones;

        public static string OverrideDirectory =>
            Path.Combine(Application.persistentDataPath, OVERRIDE_DIR_NAME);

        public TileOverlayPersistence(ZoneManager zones, WorldGridBuilder grid)
        {
            _zones = zones;
            _grid = grid;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Dirty tracking
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Mark the zone owning this cell as dirty.</summary>
        public void MarkCellDirty(Vector3Int cell)
        {
            if (_zones == null) return;
            if (_zones.TryGetZoneAtTile(new Vector2Int(cell.x, cell.y), out var zone))
            {
                if (_dirtyZones.Add(zone.zoneName))
                    OnDirtyChanged?.Invoke();
            }
        }

        public void MarkBatchDirty(IList<TileEdit> edits)
        {
            if (edits == null || edits.Count == 0 || _zones == null) return;
            int before = _dirtyZones.Count;
            for (int i = 0; i < edits.Count; i++)
            {
                if (_zones.TryGetZoneAtTile(new Vector2Int(edits[i].Position.x, edits[i].Position.y), out var zone))
                    _dirtyZones.Add(zone.zoneName);
            }
            if (_dirtyZones.Count != before)
                OnDirtyChanged?.Invoke();
        }

        public void ClearDirtyState()
        {
            if (_dirtyZones.Count == 0) return;
            _dirtyZones.Clear();
            OnDirtyChanged?.Invoke();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Save
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Write a snapshot of every dirty zone to disk. Returns the number of zones saved.</summary>
        public int SaveAllDirty()
        {
            if (_dirtyZones.Count == 0) return 0;

            EnsureDirectory();
            var snapshot = new List<string>(_dirtyZones);
            int saved = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (SaveZoneInternal(snapshot[i]))
                    saved++;
            }

            if (saved > 0)
            {
                _dirtyZones.Clear();
                OnDirtyChanged?.Invoke();
            }
            return saved;
        }

        public bool SaveZone(string zoneName)
        {
            EnsureDirectory();
            bool ok = SaveZoneInternal(zoneName);
            if (ok && _dirtyZones.Remove(zoneName))
                OnDirtyChanged?.Invoke();
            return ok;
        }

        private bool SaveZoneInternal(string zoneName)
        {
            if (_zones == null || _grid == null || string.IsNullOrEmpty(zoneName)) return false;
            if (!_zones.TryGetZone(zoneName, out var zone)) return false;

            try
            {
                string path = OverridePathForZone(zoneName);
                string json = BuildOverlayJson(zone);
                File.WriteAllText(path, json);
                OnZoneSaved?.Invoke(zoneName);
                Debug.Log($"[TileOverlayPersistence] Saved zone '{zoneName}' → {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TileOverlayPersistence] Failed to save zone '{zoneName}': {ex}");
                OnSaveFailed?.Invoke(zoneName, ex);
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Apply overrides on world load
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-apply every saved zone override on top of the freshly loaded world.
        /// Called once by <see cref="WorldLoader.LoadFullWorld"/> after the original overlays are painted.
        /// </summary>
        public static int ApplyAllOverrides(WorldGridBuilder gridBuilder, ZoneManager zoneManager)
        {
            if (gridBuilder == null || zoneManager == null) return 0;
            string dir = OverrideDirectory;
            if (!Directory.Exists(dir)) return 0;

            var files = Directory.GetFiles(dir, "*" + OVERRIDE_EXTENSION);
            if (files.Length == 0) return 0;

            int applied = 0;
            int w = zoneManager.ZoneWidthTiles;
            int h = zoneManager.ZoneHeightTiles;

            for (int i = 0; i < files.Length; i++)
            {
                string zoneName = Path.GetFileName(files[i]);
                if (zoneName.EndsWith(OVERRIDE_EXTENSION, StringComparison.OrdinalIgnoreCase))
                    zoneName = zoneName.Substring(0, zoneName.Length - OVERRIDE_EXTENSION.Length);

                if (!zoneManager.TryGetZone(zoneName, out var zone))
                {
                    Debug.LogWarning($"[TileOverlayPersistence] Override '{zoneName}' has no matching zone — skipped.");
                    continue;
                }

                OverlayLoader.LoadOverlayFromPath(files[i], gridBuilder,
                    zone.gridOffset.x, zone.gridOffset.y,
                    clearLayerRegion: true, regionWidth: w, regionHeight: h);
                applied++;
            }

            if (applied > 0)
                Debug.Log($"[TileOverlayPersistence] Applied {applied} zone override(s) from {dir}");
            return applied;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Override management (for editor menu / debugging)
        // ─────────────────────────────────────────────────────────────────

        public static string OverridePathForZone(string zoneName)
        {
            EnsureDirectoryStatic();
            return Path.Combine(OverrideDirectory, zoneName + OVERRIDE_EXTENSION);
        }

        public static string[] ListOverrideFiles()
        {
            string dir = OverrideDirectory;
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetFiles(dir, "*" + OVERRIDE_EXTENSION);
        }

        public static bool DeleteOverride(string zoneName)
        {
            string path = OverridePathForZone(zoneName);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        //  JSON build (Python-compatible format)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build the per-zone overlay JSON. Layer keys match
        /// <see cref="TilemapLayerSetup.TilemapLayer"/> names. Each layer is an
        /// h×w matrix where row 0 is the top row of the zone (Python convention).
        /// </summary>
        private string BuildOverlayJson(ZoneManager.ZoneDefinition zone)
        {
            int w = _zones.ZoneWidthTiles;
            int h = _zones.ZoneHeightTiles;

            // We collect layers into a dict so order is stable and deterministic.
            var perLayer = new List<KeyValuePair<string, string[,]>>();
            foreach (TilemapLayerSetup.TilemapLayer layer in Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer)))
            {
                var tilemap = _grid.GetTilemap(layer);
                if (tilemap == null) continue;

                var matrix = new string[h, w];
                bool hasAny = false;
                for (int row = 0; row < h; row++)
                {
                    // Python row 0 = top of zone = highest unity y.
                    int unityY = zone.gridOffset.y + (h - 1 - row);
                    for (int col = 0; col < w; col++)
                    {
                        var tile = tilemap.GetTile(new Vector3Int(zone.gridOffset.x + col, unityY, 0));
                        string name = TileRegistry.Instance.GetName(tile);
                        if (!string.IsNullOrEmpty(name))
                        {
                            matrix[row, col] = name;
                            hasAny = true;
                        }
                        else
                        {
                            matrix[row, col] = string.Empty;
                        }
                    }
                }

                if (hasAny)
                    perLayer.Add(new KeyValuePair<string, string[,]>(layer.ToString(), matrix));
            }

            return SerializeOverlay(perLayer, w, h);
        }

        private static string SerializeOverlay(List<KeyValuePair<string, string[,]>> perLayer, int w, int h)
        {
            var sb = new StringBuilder(64 * 1024);
            sb.Append("{\n  \"layers\": {");

            for (int i = 0; i < perLayer.Count; i++)
            {
                var pair = perLayer[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    \"").Append(EscapeJson(pair.Key)).Append("\": [");

                for (int row = 0; row < h; row++)
                {
                    sb.Append(row == 0 ? "\n      [" : ",\n      [");
                    for (int col = 0; col < w; col++)
                    {
                        if (col > 0) sb.Append(", ");
                        sb.Append('"').Append(EscapeJson(pair.Value[row, col] ?? string.Empty)).Append('"');
                    }
                    sb.Append(']');
                }
                sb.Append("\n    ]");
            }

            sb.Append("\n  }\n}");
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // Tile names are alphanumeric + underscore; full escape kept short for safety.
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // ─────────────────────────────────────────────────────────────────
        //  Misc
        // ─────────────────────────────────────────────────────────────────

        private static void EnsureDirectoryStatic()
        {
            string dir = OverrideDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private void EnsureDirectory() => EnsureDirectoryStatic();
    }
}
