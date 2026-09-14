using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// One road between two towns, in world TILES: the centre line it was drawn along, and the
    /// towns it joins. The tiles it paints are the plan's shared road raster
    /// (<see cref="WorldGenMap.RoadTiles"/>), because two roads can run into the same street.
    /// </summary>
    public sealed class WorldRoad
    {
        public readonly int FromTown;
        public readonly int ToTown;

        /// <summary>The centre line, in order from <see cref="FromTown"/> to <see cref="ToTown"/>.</summary>
        public readonly List<Vector2Int> Path;

        public WorldRoad(int fromTown, int toTown, List<Vector2Int> path)
        {
            FromTown = fromTown;
            ToTown = toTown;
            Path = path ?? new List<Vector2Int>();
        }
    }
}
