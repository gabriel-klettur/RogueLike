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
        private static readonly Dictionary<string, Sprite> _spriteByName = new Dictionary<string, Sprite>();
        private static bool _spriteIndexBuilt;
        private static int _missingCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _tileCache.Clear();
            _spriteByName.Clear();
            _spriteIndexBuilt = false;
            _missingCount = 0;
        }

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
            LoadOverlay(overlayFileName, gridBuilder, offsetX, offsetY, 0, 0);
        }

        /// <summary>
        /// Load an overlay JSON and paint onto the world grid, optionally clipped to a
        /// per-zone footprint. Pass <paramref name="maxWidth"/>/<paramref name="maxHeight"/> &gt; 0
        /// to enforce zone bounds — any tile in the JSON outside the rectangle is skipped
        /// and a single warning is logged. Pass 0 to disable clipping (legacy behaviour).
        /// </summary>
        public static void LoadOverlay(string overlayFileName, WorldGridBuilder gridBuilder,
            int offsetX, int offsetY, int maxWidth, int maxHeight)
        {
            string jsonPath = Path.Combine(Application.streamingAssetsPath, "Maps", overlayFileName);
            LoadOverlayFromPath(jsonPath, gridBuilder, offsetX, offsetY,
                clearLayerRegion: false, regionWidth: 0, regionHeight: 0,
                maxWidth: maxWidth, maxHeight: maxHeight);
        }

        /// <summary>
        /// Load an overlay JSON from an arbitrary absolute path and paint at the given offset.
        /// When <paramref name="clearLayerRegion"/> is true, the [offset, offset+region] rectangle
        /// of every painted layer is cleared first so empty cells in the JSON are also applied (true erase).
        /// Used for runtime overrides loaded from <c>persistentDataPath/MapOverrides</c>.
        ///
        /// When <paramref name="maxWidth"/> / <paramref name="maxHeight"/> are &gt; 0, any tile in the
        /// JSON outside the [0, max) rectangle is skipped and a single warning is logged. This guards
        /// against malformed overlays that would otherwise paint into neighbouring zones.
        /// </summary>
        public static void LoadOverlayFromPath(string jsonPath, WorldGridBuilder gridBuilder,
            int offsetX, int offsetY, bool clearLayerRegion, int regionWidth, int regionHeight,
            int maxWidth = 0, int maxHeight = 0)
        {
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

                if (clearLayerRegion && regionWidth > 0 && regionHeight > 0)
                {
                    for (int y = 0; y < regionHeight; y++)
                        for (int x = 0; x < regionWidth; x++)
                            tilemap.SetTile(new Vector3Int(offsetX + x, offsetY + y, 0), null);
                }

                PaintLayer(tilemap, rows, tilemapLayer == TilemapLayerSetup.TilemapLayer.Collision
                    || tilemapLayer == TilemapLayerSetup.TilemapLayer.WallsBottom, offsetX, offsetY,
                    maxWidth, maxHeight, jsonPath);
            }

            if (_missingCount > 0)
                Debug.LogWarning($"[OverlayLoader] {_missingCount} tile references could not be resolved ({jsonPath}).");
            else
                Debug.Log($"[OverlayLoader] Overlay '{Path.GetFileName(jsonPath)}' loaded at offset ({offsetX},{offsetY}).");
        }

        private static void PaintLayer(Tilemap tilemap, List<object> rows, bool isCollisionLayer,
            int offsetX = 0, int offsetY = 0, int maxWidth = 0, int maxHeight = 0,
            string sourceLabel = null)
        {
            int tilesSet = 0;
            int tilesClipped = 0;
            int rowCount = rows.Count;
            // When clipping is enabled, use the JSON's row count to compute the Y-flip basis
            // so painting still produces the same orientation; we only skip out-of-bounds tiles.
            for (int y = 0; y < rowCount; y++)
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
                    int flippedY = rowCount - 1 - y;

                    // Bounds clip — skip any tile outside the declared zone footprint.
                    if (maxWidth > 0 && x >= maxWidth) { tilesClipped++; continue; }
                    if (maxHeight > 0 && flippedY >= maxHeight) { tilesClipped++; continue; }

                    tilemap.SetTile(new Vector3Int(offsetX + x, offsetY + flippedY, 0), tile);
                    tilesSet++;
                }
            }

            if (tilesClipped > 0)
            {
                string label = sourceLabel != null ? Path.GetFileName(sourceLabel) : "<unknown>";
                Debug.LogWarning($"[OverlayLoader] '{label}' on '{tilemap.gameObject.name}': " +
                                 $"{tilesClipped} tile(s) clipped to zone footprint {maxWidth}x{maxHeight}.");
            }

            if (tilesSet > 0)
                Debug.Log($"[OverlayLoader] Painted {tilesSet} tiles on '{tilemap.gameObject.name}'.");
        }

        private static TileBase ResolveTile(string tileName, bool isCollisionLayer)
        {
            // Cache key includes collision context since same tile may need different collider types
            string cacheKey = isCollisionLayer ? tileName + "__col" : tileName;
            if (_tileCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            Sprite sprite = ResolveSprite(tileName);
            if (sprite == null)
            {
                _missingCount++;
                return null;
            }

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = Color.white;

            // Collision layer tiles always get grid collider for TilemapCollider2D
            tile.colliderType = isCollisionLayer ? Tile.ColliderType.Grid : Tile.ColliderType.None;

            // Name the tile so reverse lookup (TileRegistry.GetName) works for round-trip persistence
            tile.name = tileName;
            Valkur.Gameplay.TileEditor.TileRegistry.Instance.Register(tileName, tile);

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

            // Fallback: Python "ready/{category}/..." tiles may have been imported
            // without the "ready/" prefix (e.g. grass_dirt/, grass_rock/ at Tiles root).
            if (tileName.StartsWith("ready/"))
            {
                string stripped = tileName.Substring(6); // Remove "ready/"
                sprite = Resources.Load<Sprite>("Tiles/" + stripped);
                if (sprite != null) return sprite;
                tileName = stripped;
            }

            EnsureSpriteIndex();
            if (_spriteByName.TryGetValue(tileName, out sprite) && sprite != null)
                return sprite;

            if (tileName.Contains("/"))
            {
                string fileName = Path.GetFileName(tileName);
                if (_spriteByName.TryGetValue(fileName, out sprite) && sprite != null)
                    return sprite;
            }

            Debug.LogWarning($"[OverlayLoader] Could not resolve tile: '{tileName}' (tried Resources/Tiles/{tileName})");
            return null;
        }

        private static void EnsureSpriteIndex()
        {
            if (_spriteIndexBuilt) return;

            var sprites = Resources.LoadAll<Sprite>("Tiles");
            if (sprites == null || sprites.Length == 0) return;
            _spriteIndexBuilt = true;

            for (int i = 0; i < sprites.Length; i++)
            {
                var sprite = sprites[i];
                if (sprite == null || string.IsNullOrEmpty(sprite.name)) continue;
                if (!_spriteByName.ContainsKey(sprite.name))
                    _spriteByName[sprite.name] = sprite;
            }
        }
    }
}
