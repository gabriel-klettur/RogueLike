using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// One river: the tiles of its centre line, from source to mouth, 4-connected.
    /// Width is applied when it is rasterised (<see cref="WorldRiverRaster"/>), not stored.
    /// </summary>
    public sealed class WorldRiver
    {
        private readonly List<Vector2Int> _tiles;

        public IReadOnlyList<Vector2Int> Tiles => _tiles;

        public Vector2Int Source => _tiles[0];
        public Vector2Int Mouth => _tiles[_tiles.Count - 1];
        public int Length => _tiles.Count;

        public WorldRiver(List<Vector2Int> tiles)
        {
            _tiles = tiles ?? new List<Vector2Int>();
        }
    }
}
