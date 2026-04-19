using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Loads the full multi-zone world by iterating over the zone database
    /// and painting each zone's overlay and collision data at the correct offset.
    /// Maps to Python's WorldManager + MapManager that loads all zone overlays
    /// and collision grids for the base world.
    /// </summary>
    public class WorldLoader : MonoBehaviour
    {
        [Tooltip("Zone database loader that provides zone entries.")]
        [SerializeField] private ZoneDatabaseLoader _databaseLoader;

        [Tooltip("WorldGridBuilder for tilemap access.")]
        [SerializeField] private WorldGridBuilder _gridBuilder;

        [Tooltip("Load world automatically after database is loaded.")]
        [SerializeField] private bool _autoLoad = true;

        private int _overlaysLoaded;
        private int _collisionsLoaded;

        /// <summary>Number of overlay files successfully loaded.</summary>
        public int OverlaysLoaded => _overlaysLoaded;

        /// <summary>Number of collision files successfully loaded.</summary>
        public int CollisionsLoaded => _collisionsLoaded;

        private void Start()
        {
            if (_autoLoad)
                LoadFullWorld();
        }

        /// <summary>
        /// Load all zone overlays and collision grids from the zone database.
        /// Each overlay is painted at its zone's grid offset so the full world
        /// appears as a contiguous tilemap.
        /// </summary>
        public void LoadFullWorld()
        {
            if (_databaseLoader == null)
            {
                _databaseLoader = FindObjectOfType<ZoneDatabaseLoader>();
                if (_databaseLoader == null)
                {
                    Debug.LogError("[WorldLoader] ZoneDatabaseLoader not found.", this);
                    return;
                }
            }

            if (_gridBuilder == null)
            {
                _gridBuilder = FindObjectOfType<WorldGridBuilder>();
                if (_gridBuilder == null)
                {
                    Debug.LogError("[WorldLoader] WorldGridBuilder not found.", this);
                    return;
                }
            }

            _overlaysLoaded = 0;
            _collisionsLoaded = 0;

            var entries = _databaseLoader.Entries;
            if (entries == null || entries.Count == 0)
            {
                Debug.LogWarning("[WorldLoader] No zone entries in database.");
                return;
            }

            // Defensive dedup: avoid painting the same overlay/collision into the same
            // (offsetX, offsetY) twice. This guards against malformed zones_database.json
            // and prevents stacked tilemap fillrate cost on layers like Ground.
            var paintedOverlays   = new HashSet<(int, int, string)>();
            var paintedCollisions = new HashSet<(int, int, string)>();
            int skippedOverlays   = 0;
            int skippedCollisions = 0;

            // Each overlay must paint within its declared zone footprint. Pass these
            // dimensions to OverlayLoader so any out-of-bounds tile is skipped with a
            // logged warning instead of bleeding into the neighbouring zone.
            int zoneW = _databaseLoader.ZoneWidthTiles;
            int zoneH = _databaseLoader.ZoneHeightTiles;

            foreach (var entry in entries)
            {
                // Load overlay at zone offset
                if (!string.IsNullOrEmpty(entry.overlayFile))
                {
                    var key = (entry.offsetX, entry.offsetY, entry.overlayFile);
                    if (paintedOverlays.Add(key))
                    {
                        OverlayLoader.LoadOverlay(entry.overlayFile, _gridBuilder,
                            entry.offsetX, entry.offsetY, zoneW, zoneH);
                        _overlaysLoaded++;
                    }
                    else
                    {
                        Debug.LogWarning($"[WorldLoader] Skipped duplicate overlay '{entry.overlayFile}' at ({entry.offsetX},{entry.offsetY}).");
                        skippedOverlays++;
                    }
                }

                // Load collision grid at zone offset
                if (!string.IsNullOrEmpty(entry.collisionFile))
                {
                    var key = (entry.offsetX, entry.offsetY, entry.collisionFile);
                    if (paintedCollisions.Add(key))
                    {
                        LoadCollisionGrid(entry.collisionFile, entry.offsetX, entry.offsetY, zoneW, zoneH);
                    }
                    else
                    {
                        skippedCollisions++;
                    }
                }
            }

            Debug.Log($"[WorldLoader] Full world loaded: {_overlaysLoaded} overlays, " +
                      $"{_collisionsLoaded} collision grids across {entries.Count} zones " +
                      $"(skipped duplicates: {skippedOverlays} overlays, {skippedCollisions} collisions).");

            // Apply persisted tile-editor overrides (one JSON per zone in persistentDataPath/MapOverrides).
            // This restores user edits made in previous play sessions.
            var zoneManager = FindObjectOfType<ZoneManager>();
            if (zoneManager != null)
                Valkur.Gameplay.TileEditor.TileOverlayPersistence.ApplyAllOverrides(_gridBuilder, zoneManager);
        }

        /// <summary>
        /// Parse a collision JSON file (50x50 grid of "#"/"."/"=") and paint
        /// wall tiles onto the Collision tilemap layer.
        /// "#" = solid wall, "." = walkable, "=" = special connector.
        /// When <paramref name="maxWidth"/>/<paramref name="maxHeight"/> &gt; 0, any cell outside
        /// the zone footprint is skipped and a single warning is logged.
        /// </summary>
        private void LoadCollisionGrid(string collisionFileName, int offsetX, int offsetY,
            int maxWidth = 0, int maxHeight = 0)
        {
            string jsonPath = Path.Combine(
                Application.streamingAssetsPath, "Collisions", collisionFileName);

            if (!File.Exists(jsonPath))
            {
                Debug.LogWarning($"[WorldLoader] Collision file not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var rows = MiniJsonRuntime.Deserialize(json) as List<object>;
            if (rows == null)
            {
                Debug.LogError($"[WorldLoader] Failed to parse collision file: {collisionFileName}");
                return;
            }

            var collisionTilemap = _gridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            if (collisionTilemap == null)
            {
                Debug.LogWarning("[WorldLoader] Collision tilemap layer not found.");
                return;
            }

            int cellsSet = 0;
            int cellsClipped = 0;
            int rowCount = rows.Count;

            for (int y = 0; y < rowCount; y++)
            {
                var row = rows[y] as List<object>;
                if (row == null) continue;

                for (int x = 0; x < row.Count; x++)
                {
                    string cell = row[x] as string;
                    if (cell != "#") continue;  // Only paint solid walls

                    // Y-flip: row 0 in Python is top, row 0 in Unity tilemap is bottom
                    int flippedY = rowCount - 1 - y;

                    // Bounds clip — refuse to paint a wall outside the declared zone footprint.
                    if (maxWidth > 0 && x >= maxWidth) { cellsClipped++; continue; }
                    if (maxHeight > 0 && flippedY >= maxHeight) { cellsClipped++; continue; }

                    var tile = GetWallCollisionTile();
                    collisionTilemap.SetTile(
                        new Vector3Int(offsetX + x, offsetY + flippedY, 0), tile);
                    cellsSet++;
                }
            }

            if (cellsClipped > 0)
                Debug.LogWarning($"[WorldLoader] Collision '{collisionFileName}': " +
                                 $"{cellsClipped} cell(s) clipped to zone footprint {maxWidth}x{maxHeight}.");

            if (cellsSet > 0)
            {
                _collisionsLoaded++;
                Debug.Log($"[WorldLoader] Collision '{collisionFileName}': {cellsSet} wall cells " +
                          $"at offset ({offsetX},{offsetY}).");
            }
        }

        private static TileBase _wallTile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _wallTile = null;
        }

        /// <summary>
        /// Get or create a simple collision tile for wall cells.
        /// Uses Grid collider type for TilemapCollider2D.
        /// </summary>
        private static TileBase GetWallCollisionTile()
        {
            if (_wallTile != null) return _wallTile;

            // Try to load a dedicated wall sprite, fall back to a plain tile
            var sprite = Resources.Load<Sprite>("Tiles/wall");
            if (sprite == null)
                sprite = Resources.Load<Sprite>("Tiles/floor");

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = new Color(1f, 1f, 1f, 0f); // Invisible collision-only tile
            tile.colliderType = Tile.ColliderType.Grid;
            _wallTile = tile;
            return tile;
        }
    }
}
