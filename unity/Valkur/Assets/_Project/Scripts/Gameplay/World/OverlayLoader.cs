using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Loads overlay JSON files (from Python map data) and paints tiles
    /// onto the WorldGridBuilder tilemap layers at runtime.
    /// Maps Python tile name references to Unity sprites via Resources.Load.
    /// </summary>
    public static class OverlayLoader
    {
        private static readonly Dictionary<string, TileBase> _tileCache = new Dictionary<string, TileBase>();
        private static TileBase _placeholderTile;
        private static int _missingCount;

        /// <summary>
        /// Load an overlay JSON from StreamingAssets/Maps/ and paint onto the world grid at (0,0).
        /// </summary>
        public static void LoadOverlay(string overlayFileName, WorldGridBuilder gridBuilder)
        {
            LoadOverlay(overlayFileName, gridBuilder, 0, 0);
        }

        /// <summary>
        /// Load an overlay JSON from StreamingAssets/Maps/ and paint onto the world grid
        /// at the specified tile offset. Used for multi-zone world loading.
        /// </summary>
        public static void LoadOverlay(string overlayFileName, WorldGridBuilder gridBuilder,
            int offsetX, int offsetY)
        {
            string jsonPath = Path.Combine(Application.streamingAssetsPath, "Maps", overlayFileName);
            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[OverlayLoader] Overlay file not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var root = MiniJsonRuntime.Deserialize(json) as Dictionary<string, object>;
            if (root == null)
            {
                Debug.LogError("[OverlayLoader] Failed to parse overlay JSON.");
                return;
            }

            var layers = root.GetValueOrDefault("layers") as Dictionary<string, object>;
            if (layers == null)
            {
                Debug.LogError("[OverlayLoader] Missing 'layers' key in overlay JSON.");
                return;
            }

            _missingCount = 0;

            foreach (var kvp in layers)
            {
                string layerName = kvp.Key;
                var rows = kvp.Value as List<object>;
                if (rows == null) continue;

                if (!System.Enum.TryParse<TilemapLayerSetup.TilemapLayer>(layerName, out var tilemapLayer))
                {
                    Debug.LogWarning($"[OverlayLoader] Unknown layer '{layerName}', skipping.");
                    continue;
                }

                var tilemap = gridBuilder.GetTilemap(tilemapLayer);
                if (tilemap == null)
                {
                    Debug.LogWarning($"[OverlayLoader] Tilemap for layer '{layerName}' not found.");
                    continue;
                }

                PaintLayer(tilemap, rows, tilemapLayer == TilemapLayerSetup.TilemapLayer.Collision
                    || tilemapLayer == TilemapLayerSetup.TilemapLayer.WallsBottom, offsetX, offsetY);
            }

            if (_missingCount > 0)
                Debug.LogWarning($"[OverlayLoader] {_missingCount} tile references could not be resolved ({overlayFileName}).");
            else
                Debug.Log($"[OverlayLoader] Overlay '{overlayFileName}' loaded at offset ({offsetX},{offsetY}).");
        }

        private static void PaintLayer(Tilemap tilemap, List<object> rows, bool isCollisionLayer,
            int offsetX = 0, int offsetY = 0)
        {
            int tilesSet = 0;
            for (int y = 0; y < rows.Count; y++)
            {
                var row = rows[y] as List<object>;
                if (row == null) continue;

                for (int x = 0; x < row.Count; x++)
                {
                    string tileName = row[x] as string;
                    if (string.IsNullOrEmpty(tileName)) continue;

                    var tile = ResolveTile(tileName, isCollisionLayer);
                    if (tile == null) continue;

                    // Overlay data is row-major (y=0 is top). Unity tilemap y=0 is bottom.
                    // Flip Y so row 0 in JSON maps to the top of the map.
                    int flippedY = rows.Count - 1 - y;
                    tilemap.SetTile(new Vector3Int(offsetX + x, offsetY + flippedY, 0), tile);
                    tilesSet++;
                }
            }

            if (tilesSet > 0)
                Debug.Log($"[OverlayLoader] Painted {tilesSet} tiles on '{tilemap.gameObject.name}'.");
        }

        private static TileBase ResolveTile(string tileName, bool isCollisionLayer)
        {
            // Cache key includes collision context since same tile may need different collider types
            string cacheKey = isCollisionLayer ? tileName + "__col" : tileName;
            if (_tileCache.TryGetValue(cacheKey, out var cached))
                return cached;

            Sprite sprite = ResolveSprite(tileName);
            if (sprite == null)
            {
                _missingCount++;
                _tileCache[cacheKey] = null;
                return null;
            }

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = Color.white;

            // Collision layer tiles always get grid collider for TilemapCollider2D
            tile.colliderType = isCollisionLayer ? Tile.ColliderType.Grid : Tile.ColliderType.None;

            _tileCache[cacheKey] = tile;
            return tile;
        }

        private static Sprite ResolveSprite(string tileName)
        {
            // Direct mirror: overlay name maps 1:1 to Resources/Tiles/{tileName}
            // Python assets/tiles/floor.png → Unity Resources/Tiles/floor.png (PPU=32)
            // Python assets/tiles/tileset_1/rock_grass/rock_grass_32_4.png → Resources/Tiles/tileset_1/rock_grass/rock_grass_32_4.png
            var sprite = Resources.Load<Sprite>("Tiles/" + tileName);
            if (sprite != null) return sprite;

            Debug.LogWarning($"[OverlayLoader] Could not resolve tile: '{tileName}' (tried Resources/Tiles/{tileName})");
            return null;
        }
    }
}
