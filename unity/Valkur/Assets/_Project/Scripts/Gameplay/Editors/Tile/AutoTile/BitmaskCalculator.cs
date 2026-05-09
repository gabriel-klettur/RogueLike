using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Pure logic that converts a terrain grid + a cell coordinate into a 4-bit
    /// cardinal mask suitable for indexing into a Blob16 ruleset.
    /// </summary>
    public static class BitmaskCalculator
    {
        public const byte BitN = 1 << 0;
        public const byte BitE = 1 << 1;
        public const byte BitS = 1 << 2;
        public const byte BitW = 1 << 3;

        /// <summary>
        /// Computes the 4-bit cardinal mask for <paramref name="cell"/>, comparing each
        /// cardinal neighbor against <paramref name="terrain"/>. Cells outside the grid
        /// (key not present in <paramref name="grid"/>) do NOT count as same terrain.
        /// </summary>
        public static byte CardinalMask(IReadOnlyDictionary<Vector2Int, string> grid,
                                         Vector2Int cell, string terrain)
        {
            if (grid == null) return 0;

            byte mask = 0;
            if (NeighborMatches(grid, cell + Vector2Int.up,    terrain)) mask |= BitN;
            if (NeighborMatches(grid, cell + Vector2Int.right, terrain)) mask |= BitE;
            if (NeighborMatches(grid, cell + Vector2Int.down,  terrain)) mask |= BitS;
            if (NeighborMatches(grid, cell + Vector2Int.left,  terrain)) mask |= BitW;
            return mask;
        }

        private static bool NeighborMatches(IReadOnlyDictionary<Vector2Int, string> grid,
                                             Vector2Int key, string terrain)
        {
            return grid.TryGetValue(key, out var value) && value == terrain;
        }
    }
}
