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

            foreach (var entry in entries)
            {
                // Load overlay at zone offset
                if (!string.IsNullOrEmpty(entry.overlayFile))
                {
                    OverlayLoader.LoadOverlay(entry.overlayFile, _gridBuilder,
                        entry.offsetX, entry.offsetY);
                    _overlaysLoaded++;
                }

                // Load collision grid at zone offset
                if (!string.IsNullOrEmpty(entry.collisionFile))
                {
                    LoadCollisionGrid(entry.collisionFile, entry.offsetX, entry.offsetY);
                }
            }

            Debug.Log($"[WorldLoader] Full world loaded: {_overlaysLoaded} overlays, " +
                      $"{_collisionsLoaded} collision grids across {entries.Count} zones.");
        }

        /// <summary>
        /// Parse a collision JSON file (50x50 grid of "#"/"."/"=") and paint
        /// wall tiles onto the Collision tilemap layer.
        /// "#" = solid wall, "." = walkable, "=" = special connector.
        /// </summary>
        private void LoadCollisionGrid(string collisionFileName, int offsetX, int offsetY)
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
                    var tile = GetWallCollisionTile();
                    collisionTilemap.SetTile(
                        new Vector3Int(offsetX + x, offsetY + flippedY, 0), tile);
                    cellsSet++;
                }
            }

            if (cellsSet > 0)
            {
                _collisionsLoaded++;
                Debug.Log($"[WorldLoader] Collision '{collisionFileName}': {cellsSet} wall cells " +
                          $"at offset ({offsetX},{offsetY}).");
            }
        }

        private static TileBase _wallTile;

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
