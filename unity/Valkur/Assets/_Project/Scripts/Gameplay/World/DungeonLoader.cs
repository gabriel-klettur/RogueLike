using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Generates the procedural dungeon at runtime and paints tiles onto the world tilemap.
    /// Port of Python's MapService dungeon flow: generate rooms, paint tiles, carve
    /// connecting tunnel between lobby and dungeon across zone boundaries, set collision.
    /// </summary>
    public class DungeonLoader : MonoBehaviour
    {
        [Tooltip("Dungeon generator configuration (rooms, tunnels, tile chars).")]
        [SerializeField] private DungeonGeneratorConfig _config;

        [Tooltip("Optional seed for deterministic generation. -1 for random.")]
        [SerializeField] private int _seed = -1;

        [Header("Tile Sprites")]
        [Tooltip("Sprites for dungeon room floor tiles (char 'O'). Randomly selected per tile.")]
        [SerializeField] private Sprite[] _dungeonFloorSprites;

        [Tooltip("Sprites for tunnel floor tiles (char '='). Randomly selected per tile.")]
        [SerializeField] private Sprite[] _tunnelFloorSprites;

        [Tooltip("Sprite for wall tiles (char '#'). Used for collision layer.")]
        [SerializeField] private Sprite _wallSprite;

        /// <summary>Last generation result (rooms, grid). Available after Generate().</summary>
        public DungeonGenerator.Result LastResult { get; private set; }

        /// <summary>Whether the dungeon has been generated and painted this session.</summary>
        public bool IsGenerated { get; private set; }

        /// <summary>Set the generator config at runtime (for programmatic setup).</summary>
        public void SetConfig(DungeonGeneratorConfig config) => _config = config;

        private readonly Dictionary<string, TileBase> _tileCache = new Dictionary<string, TileBase>();
        private System.Random _spriteRng;

        /// <summary>
        /// Generate the dungeon and paint it onto the world grid at the given zone offset.
        /// Also carves a connecting tunnel between the lobby and the nearest dungeon room.
        /// </summary>
        /// <param name="gridBuilder">The world grid builder providing tilemap access.</param>
        /// <param name="dungeonOffsetX">Tile X offset of the dungeon zone in world coords.</param>
        /// <param name="dungeonOffsetY">Tile Y offset of the dungeon zone in world coords.</param>
        /// <param name="lobbyOffsetX">Tile X offset of the lobby zone in world coords.</param>
        /// <param name="lobbyOffsetY">Tile Y offset of the lobby zone in world coords.</param>
        /// <param name="zoneHeight">Height of each zone in tiles (for Y-flip calculation).</param>
        /// <param name="seed">RNG seed. -1 for random.</param>
        public void GenerateAndPaint(
            WorldGridBuilder gridBuilder,
            int dungeonOffsetX, int dungeonOffsetY,
            int lobbyOffsetX, int lobbyOffsetY,
            int zoneHeight,
            int seed = -1)
        {
            if (_config == null)
            {
                Debug.LogError("[DungeonLoader] DungeonGeneratorConfig not assigned.", this);
                return;
            }

            if (gridBuilder == null)
            {
                Debug.LogError("[DungeonLoader] WorldGridBuilder is null.", this);
                return;
            }

            LoadSpritesIfNeeded();

            int effectiveSeed = seed != -1 ? seed : _seed;

            // 1) Generate dungeon grid
            var result = DungeonGenerator.Generate(_config, effectiveSeed);
            LastResult = result;

            // Use a separate RNG for sprite selection so tile visuals are deterministic per seed
            _spriteRng = effectiveSeed >= 0 ? new System.Random(effectiveSeed + 7919) : new System.Random();

            // 2) Paint dungeon tiles onto Ground tilemap
            var groundTilemap = gridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            var collisionTilemap = gridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);

            if (groundTilemap == null)
            {
                Debug.LogError("[DungeonLoader] Ground tilemap layer not found.");
                return;
            }

            int tilesSet = 0;
            int collisionSet = 0;
            char wallChar = _config.WallChar;
            char roomChar = _config.RoomChar;
            char tunnelChar = _config.TunnelChar;

            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    char c = result.Grid[y][x];

                    // Y-flip: row 0 in generation is top, Unity tilemap y=0 is bottom
                    int flippedY = result.Height - 1 - y;
                    var pos = new Vector3Int(dungeonOffsetX + x, dungeonOffsetY + flippedY, 0);

                    if (c == roomChar || c == 'D')
                    {
                        groundTilemap.SetTile(pos, GetDungeonFloorTile());
                        tilesSet++;
                    }
                    else if (c == tunnelChar)
                    {
                        groundTilemap.SetTile(pos, GetTunnelFloorTile());
                        tilesSet++;
                    }
                    else if (c == wallChar)
                    {
                        // Python renders walls as visible stone-brick tiles on the ground layer
                        // AND they block movement. Paint visible tile + collision.
                        groundTilemap.SetTile(pos, GetWallGroundTile());
                        tilesSet++;
                        if (collisionTilemap != null)
                        {
                            collisionTilemap.SetTile(pos, GetWallCollisionTile());
                            collisionSet++;
                        }
                    }
                }
            }

            // 3) Carve connecting tunnel between lobby and dungeon
            int connectingTiles = CarveConnectingTunnel(
                gridBuilder, result,
                dungeonOffsetX, dungeonOffsetY,
                lobbyOffsetX, lobbyOffsetY,
                zoneHeight);

            IsGenerated = true;
            Debug.Log($"[DungeonLoader] Dungeon generated: {result.Rooms.Count} rooms, " +
                      $"{tilesSet} ground tiles, {collisionSet} collision tiles, " +
                      $"{connectingTiles} connecting tunnel tiles (seed={effectiveSeed}).");
        }

        /// <summary>
        /// Carve an L-shaped tunnel from the lobby bottom edge to the nearest
        /// dungeon room center. Port of Python's MapService._connect_tunnels_in_world().
        /// After global Y-flip in ZoneDatabaseLoader, the dungeon zone sits below
        /// the lobby (smaller Y), so the tunnel exits the lobby's bottom edge.
        /// </summary>
        private int CarveConnectingTunnel(
            WorldGridBuilder gridBuilder,
            DungeonGenerator.Result dungeonResult,
            int dungeonOffX, int dungeonOffY,
            int lobbyOffX, int lobbyOffY,
            int zoneHeight)
        {
            if (dungeonResult.Rooms == null || dungeonResult.Rooms.Count == 0)
                return 0;

            var groundTilemap = gridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            var collisionTilemap = gridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            if (groundTilemap == null) return 0;

            // Python: dungeon_connect_side = "bottom" → dungeon is south of lobby.
            // After ZoneDatabaseLoader's global Y-flip, dungeon offset Y < lobby offset Y,
            // so the dungeon zone is directly below the lobby in Unity coords.
            // The lobby's bottom edge (lowest Y) is adjacent to the dungeon's top edge.
            // Lobby exit: bottom-center of lobby (Y = lobbyOffY, X = midpoint).
            int lobbyExitLocalX = _config.ZoneWidth / 2;
            int lobbyExitWorldX = lobbyOffX + lobbyExitLocalX;
            int lobbyExitWorldY = lobbyOffY; // Bottom edge of lobby = adjacent to dungeon top

            // Find closest dungeon room center (in world tile coords, Unity Y-up)
            int bestDX = 0, bestDY = 0;
            int minDist = int.MaxValue;

            for (int i = 0; i < dungeonResult.Rooms.Count; i++)
            {
                var center = DungeonGenerator.CenterOf(dungeonResult.Rooms[i]);
                // Convert from generation coords (Y-down) to Unity tilemap coords (Y-up)
                int flippedCY = dungeonResult.Height - 1 - center.y;
                int worldCX = dungeonOffX + center.x;
                int worldCY = dungeonOffY + flippedCY;

                int dist = Mathf.Abs(worldCX - lobbyExitWorldX) + Mathf.Abs(worldCY - lobbyExitWorldY);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestDX = worldCX;
                    bestDY = worldCY;
                }
            }

            // Carve L-shaped tunnel in world tile coords
            int thickness = _config.TunnelThickness;
            int tilesCarved = 0;

            // Horizontal segment: from lobbyExit.x to dungeonRoom.x at lobbyExit.y
            int xStart = Mathf.Min(lobbyExitWorldX, bestDX);
            int xEnd = Mathf.Max(lobbyExitWorldX, bestDX);
            int half = thickness / 2;

            for (int xx = xStart; xx <= xEnd; xx++)
            {
                for (int t = 0; t < thickness; t++)
                {
                    int yy = lobbyExitWorldY + t - half;
                    var pos = new Vector3Int(xx, yy, 0);
                    groundTilemap.SetTile(pos, GetTunnelFloorTile());
                    // Clear any collision tile at this position
                    collisionTilemap?.SetTile(pos, null);
                    tilesCarved++;
                }
            }

            // Vertical segment: from lobbyExit.y to dungeonRoom.y at dungeonRoom.x
            int yMin = Mathf.Min(lobbyExitWorldY, bestDY);
            int yMax = Mathf.Max(lobbyExitWorldY, bestDY);

            for (int yy = yMin; yy <= yMax; yy++)
            {
                for (int t = 0; t < thickness; t++)
                {
                    int xx = bestDX + t - half;
                    var pos = new Vector3Int(xx, yy, 0);
                    groundTilemap.SetTile(pos, GetTunnelFloorTile());
                    // Clear any collision tile at this position
                    collisionTilemap?.SetTile(pos, null);
                    tilesCarved++;
                }
            }

            return tilesCarved;
        }

        /// <summary>Load sprites from Resources if not assigned via inspector.</summary>
        private void LoadSpritesIfNeeded()
        {
            if (_dungeonFloorSprites == null || _dungeonFloorSprites.Length == 0)
            {
                // Python only uses dungeon_1.png (downscaled 1024→32 at runtime).
                // Unity uses a pre-scaled 32×32 sprite: dungeon_floor.png
                var sprites = new List<Sprite>();
                var s = Resources.Load<Sprite>("Tiles/dungeon_floor");
                if (s != null)
                    sprites.Add(s);
                else
                {
                    // Fallback to generic floor sprite
                    var fallback = Resources.Load<Sprite>("Tiles/floor_1");
                    if (fallback == null) fallback = Resources.Load<Sprite>("Tiles/floor");
                    if (fallback != null) sprites.Add(fallback);
                }
                _dungeonFloorSprites = sprites.ToArray();
            }

            if (_tunnelFloorSprites == null || _tunnelFloorSprites.Length == 0)
            {
                // Python only uses dungeon_c_1.png (downscaled 1024→32).
                // Unity uses pre-scaled 32×32 sprite: dungeon_tunnel.png
                var sprites = new List<Sprite>();
                var s = Resources.Load<Sprite>("Tiles/dungeon_tunnel");
                if (s != null)
                    sprites.Add(s);
                else
                {
                    var fallback = Resources.Load<Sprite>("Tiles/floor_1");
                    if (fallback == null) fallback = Resources.Load<Sprite>("Tiles/floor");
                    if (fallback != null) sprites.Add(fallback);
                }
                _tunnelFloorSprites = sprites.ToArray();
            }

            if (_wallSprite == null)
            {
                _wallSprite = Resources.Load<Sprite>("Tiles/wall");
            }
        }

        private TileBase GetDungeonFloorTile()
        {
            if (_dungeonFloorSprites == null || _dungeonFloorSprites.Length == 0)
                return null;

            int idx = _spriteRng.Next(_dungeonFloorSprites.Length);
            string key = $"dungeon_floor_{idx}";
            if (_tileCache.TryGetValue(key, out var cached))
                return cached;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = _dungeonFloorSprites[idx];
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            _tileCache[key] = tile;
            return tile;
        }

        private TileBase GetTunnelFloorTile()
        {
            if (_tunnelFloorSprites == null || _tunnelFloorSprites.Length == 0)
                return null;

            int idx = _spriteRng.Next(_tunnelFloorSprites.Length);
            string key = $"tunnel_floor_{idx}";
            if (_tileCache.TryGetValue(key, out var cached))
                return cached;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = _tunnelFloorSprites[idx];
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            _tileCache[key] = tile;
            return tile;
        }

        private TileBase _wallCollisionTile;
        private TileBase _wallGroundTile;

        /// <summary>Visible wall tile for the Ground tilemap (matches Python's wall.PNG rendering).</summary>
        private TileBase GetWallGroundTile()
        {
            if (_wallGroundTile != null) return _wallGroundTile;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = _wallSprite;
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            _wallGroundTile = tile;
            return tile;
        }

        private TileBase GetWallCollisionTile()
        {
            if (_wallCollisionTile != null) return _wallCollisionTile;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = _wallSprite;
            tile.color = new Color(1f, 1f, 1f, 0f); // Invisible collision-only tile
            tile.colliderType = Tile.ColliderType.Grid;
            _wallCollisionTile = tile;
            return tile;
        }
    }
}
