using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileOverlayPersistence
    {

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

        /// <summary>
        /// Move a zone's persisted tile-edit file to a new zone name. Used by
        /// the Map Editor's rename flow so the override file (and the tiles
        /// inside it) follows the zone — without this, renaming a zone with
        /// painted tiles loses every tile on the next world load because
        /// <see cref="ApplyAllOverrides"/> looks up the file's basename in the
        /// ZoneManager and finds nothing.
        /// </summary>
        /// <returns>true if the file was moved (or there was nothing to move);
        /// false if a file already exists at the new name (caller can
        /// decide to overwrite).</returns>
        public static bool RenameOverride(string oldZoneName, string newZoneName)
        {
            if (string.IsNullOrEmpty(oldZoneName) || string.IsNullOrEmpty(newZoneName)) return false;
            if (string.Equals(oldZoneName, newZoneName, StringComparison.Ordinal)) return true;

            string oldPath = OverridePathForZone(oldZoneName);
            if (!File.Exists(oldPath)) return true;     // nothing to move — success

            string newPath = OverridePathForZone(newZoneName);
            if (File.Exists(newPath))
            {
                Debug.LogWarning(
                    $"[TileOverlayPersistence] Cannot rename override '{oldZoneName}' → '{newZoneName}': " +
                    $"a file already exists at the destination. Old file preserved.");
                return false;
            }

            try
            {
                File.Move(oldPath, newPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TileOverlayPersistence] Failed to rename override '{oldZoneName}' → '{newZoneName}': {ex.Message}");
                return false;
            }
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