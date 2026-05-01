using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    public partial class DungeonLoader : MonoBehaviour
    {

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