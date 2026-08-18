using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileOverlayPersistence
    {
        // ─────────────────────────────────────────────────────────────────
        //  Per-world routing
        // ─────────────────────────────────────────────────────────────────
        //
        // The legacy single-world callsites (no WorldId argument) still resolve
        // to <see cref="WorldId.Base"/>, which keeps writing under the flat
        // <c>persistentDataPath/MapOverrides/</c> root for byte-compat with
        // pre-multi-map saves. Non-base worlds nest under their slug — e.g.
        // <c>persistentDataPath/MapOverrides/forest/</c> — so each map slot
        // owns an independent override layer on disk.

        /// <summary>Absolute directory holding overlay files for the given world.</summary>
        public static string OverrideDirectoryForWorld(WorldId worldId)
        {
            string root = Path.Combine(Application.persistentDataPath, OVERRIDE_DIR_NAME);
            return worldId.IsBase ? root : Path.Combine(root, worldId.Slug);
        }

        public static int ApplyAllOverrides(WorldGridBuilder gridBuilder, ZoneManager zoneManager)
            => ApplyAllOverrides(gridBuilder, zoneManager, WorldId.Base);

        public static int ApplyAllOverrides(WorldGridBuilder gridBuilder, ZoneManager zoneManager, WorldId worldId)
        {
            if (gridBuilder == null || zoneManager == null) return 0;
            string dir = OverrideDirectoryForWorld(worldId);
            if (!Directory.Exists(dir)) return 0;

            var files = Directory.GetFiles(dir, "*" + OVERRIDE_EXTENSION);
            if (files.Length == 0) return 0;

            int applied = 0;
            int orphaned = 0;
            string firstOrphan = null;
            int w = zoneManager.ZoneWidthTiles;
            int h = zoneManager.ZoneHeightTiles;

            // Resolve the parallel-matrix sinks — both live on the TileEditorManager
            // singleton via lazy properties, so accessing them spawns the underlying
            // map if it isn't built yet. Guarded by HasInstance for the rare case
            // ApplyAllOverrides runs before GameplaySceneSetup composes the editor.
            CollisionTagMap collisionTagSink = null;
            World.Layering.LayerJumpMap layerJumpSink = null;
            if (TileEditorManager.HasInstance)
            {
                collisionTagSink = TileEditorManager.Instance.CollisionTags;
                layerJumpSink = TileEditorManager.Instance.LayerJumps;
            }
            int tagsLoaded = 0;
            int jumpsLoaded = 0;

            for (int i = 0; i < files.Length; i++)
            {
                string zoneName = Path.GetFileName(files[i]);
                if (zoneName.EndsWith(OVERRIDE_EXTENSION, StringComparison.OrdinalIgnoreCase))
                    zoneName = zoneName.Substring(0, zoneName.Length - OVERRIDE_EXTENSION.Length);

                if (!zoneManager.TryGetZone(zoneName, out var zone))
                {
                    // A zone deleted via the Map Editor leaves its overlay
                    // file behind — that is fully expected, not a bug.
                    // Spamming one warning per orphan floods the console;
                    // collapse them into a single aggregate notice.
                    if (orphaned == 0) firstOrphan = zoneName;
                    orphaned++;
                    continue;
                }

                // Parse once, apply three times. Each *FromPath overload re-reads and
                // re-deserializes the file, so this loop used to parse every override
                // three times over — 4.56 MB of JSON where 1.52 MB does, plus the
                // matching garbage, inside a stage that never yields.
                var root = OverlayLoader.ParseOverlay(files[i]);
                if (root == null)
                {
                    Debug.LogWarning($"[TileOverlayPersistence] Could not parse override '{files[i]}'.");
                    continue;
                }

                OverlayLoader.LoadOverlayFromRoot(root, gridBuilder,
                    zone.gridOffset.x, zone.gridOffset.y,
                    clearLayerRegion: true, regionWidth: w, regionHeight: h,
                    sourceLabel: files[i]);
                applied++;

                if (collisionTagSink != null)
                    tagsLoaded += OverlayLoader.ApplyCollisionTags(
                        root, collisionTagSink, zone.gridOffset.x, zone.gridOffset.y);
                if (layerJumpSink != null)
                    jumpsLoaded += OverlayLoader.ApplyLayerJumps(
                        root, layerJumpSink, zone.gridOffset.x, zone.gridOffset.y);
            }

            if (applied > 0)
                Debug.Log($"[TileOverlayPersistence] Applied {applied} zone override(s) from {dir}");
            if (orphaned > 0)
                Debug.Log($"[TileOverlayPersistence] Skipped {orphaned} orphaned override " +
                          $"file(s) (no matching zone, e.g. '{firstOrphan}'). Safe to ignore — " +
                          $"these belong to zones deleted via the Map Editor.");
            if (tagsLoaded > 0 || jumpsLoaded > 0)
                Debug.Log($"[TileOverlayPersistence] Restored {tagsLoaded} collision tag(s) " +
                          $"and {jumpsLoaded} layer-jump cell(s) from disk.");

            // M2.1: the sub-tilemap composites bake from the freshly painted Collision
            // tilemap + tag map. SetTile already marks the baker dirty via
            // Tilemap.tilemapTileChanged, but the tagMap edits we just streamed in
            // don't fire that event — schedule an explicit rebake so per-layer
            // physics matches the loaded tags from frame 0.
            if (tagsLoaded > 0 && Application.isPlaying)
                World.Layering.WorldCollisionBaker.EnsureExists().ScheduleRebake();

            return applied;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Override management (for editor menu / debugging)
        // ─────────────────────────────────────────────────────────────────

        public static string OverridePathForZone(string zoneName)
            => OverridePathForZone(zoneName, WorldId.Base);

        public static string OverridePathForZone(string zoneName, WorldId worldId)
        {
            EnsureDirectoryStatic(worldId);
            return Path.Combine(OverrideDirectoryForWorld(worldId), zoneName + OVERRIDE_EXTENSION);
        }

        public static string[] ListOverrideFiles()
            => ListOverrideFiles(WorldId.Base);

        public static string[] ListOverrideFiles(WorldId worldId)
        {
            string dir = OverrideDirectoryForWorld(worldId);
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetFiles(dir, "*" + OVERRIDE_EXTENSION);
        }

        public static bool DeleteOverride(string zoneName)
            => DeleteOverride(zoneName, WorldId.Base);

        public static bool DeleteOverride(string zoneName, WorldId worldId)
        {
            string path = OverridePathForZone(zoneName, worldId);
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
            => RenameOverride(oldZoneName, newZoneName, WorldId.Base);

        public static bool RenameOverride(string oldZoneName, string newZoneName, WorldId worldId)
        {
            if (string.IsNullOrEmpty(oldZoneName) || string.IsNullOrEmpty(newZoneName)) return false;
            if (string.Equals(oldZoneName, newZoneName, StringComparison.Ordinal)) return true;

            string oldPath = OverridePathForZone(oldZoneName, worldId);
            if (!File.Exists(oldPath)) return true;     // nothing to move — success

            string newPath = OverridePathForZone(newZoneName, worldId);
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

                // Collision must always be emitted, even when empty: if the user
                // erased every collider in a zone, omitting the layer would let
                // the base map's colliders (loaded additively in Phase 1) survive
                // the override pass — the erasures would silently come back on
                // the next load. The `clearLayerRegion: true` semantics in
                // ApplyAllOverrides only fire for layers present in the JSON,
                // so we keep an empty Collision matrix to force the clear.
                bool alwaysEmit = layer == TilemapLayerSetup.TilemapLayer.Collision;
                if (hasAny || alwaysEmit)
                    perLayer.Add(new KeyValuePair<string, string[,]>(layer.ToString(), matrix));
            }

            string[,] terrainMatrix = null;
            if (TerrainMap != null && TerrainMap.HasAnyInRect(zone.gridOffset.x, zone.gridOffset.y, w, h))
                terrainMatrix = TerrainMap.BuildMatrix(zone.gridOffset.x, zone.gridOffset.y, w, h);

            string[,] collisionTagMatrix = null;
            if (CollisionTagMap != null && CollisionTagMap.HasAnyInRect(zone.gridOffset.x, zone.gridOffset.y, w, h))
                collisionTagMatrix = CollisionTagMap.BuildMatrix(zone.gridOffset.x, zone.gridOffset.y, w, h);

            string[,] layerJumpsMatrix = null;
            if (LayerJumpMap != null && LayerJumpMap.HasAnyInRect(zone.gridOffset.x, zone.gridOffset.y, w, h))
                layerJumpsMatrix = LayerJumpMap.BuildMatrix(zone.gridOffset.x, zone.gridOffset.y, w, h);

            return SerializeOverlay(perLayer, terrainMatrix, collisionTagMatrix, layerJumpsMatrix, w, h);
        }

        private static string SerializeOverlay(List<KeyValuePair<string, string[,]>> perLayer,
                                                string[,] terrainMatrix,
                                                string[,] collisionTagMatrix,
                                                string[,] layerJumpsMatrix,
                                                int w, int h)
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

            sb.Append("\n  }");

            if (terrainMatrix != null)
            {
                sb.Append(",\n  \"terrains\": [");
                for (int row = 0; row < h; row++)
                {
                    sb.Append(row == 0 ? "\n    [" : ",\n    [");
                    for (int col = 0; col < w; col++)
                    {
                        if (col > 0) sb.Append(", ");
                        sb.Append('"').Append(EscapeJson(terrainMatrix[row, col] ?? string.Empty)).Append('"');
                    }
                    sb.Append(']');
                }
                sb.Append("\n  ]");
            }

            if (collisionTagMatrix != null)
            {
                sb.Append(",\n  \"collisionTags\": [");
                for (int row = 0; row < h; row++)
                {
                    sb.Append(row == 0 ? "\n    [" : ",\n    [");
                    for (int col = 0; col < w; col++)
                    {
                        if (col > 0) sb.Append(", ");
                        sb.Append('"').Append(EscapeJson(collisionTagMatrix[row, col] ?? string.Empty)).Append('"');
                    }
                    sb.Append(']');
                }
                sb.Append("\n  ]");
            }

            if (layerJumpsMatrix != null)
            {
                sb.Append(",\n  \"layerJumps\": [");
                for (int row = 0; row < h; row++)
                {
                    sb.Append(row == 0 ? "\n    [" : ",\n    [");
                    for (int col = 0; col < w; col++)
                    {
                        if (col > 0) sb.Append(", ");
                        sb.Append('"').Append(EscapeJson(layerJumpsMatrix[row, col] ?? string.Empty)).Append('"');
                    }
                    sb.Append(']');
                }
                sb.Append("\n  ]");
            }

            sb.Append("\n}");
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

        private static void EnsureDirectoryStatic(WorldId worldId)
        {
            string dir = OverrideDirectoryForWorld(worldId);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private void EnsureDirectory() => EnsureDirectoryStatic(_worldId);
    }
}