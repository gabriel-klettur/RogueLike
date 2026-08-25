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

        // Corner model bit layout — matches Corner16Slot and the offline
        // tile_rulesets.json corner order "NW,NE,SE,SW" (a 4-char binary key like
        // "0110" parses directly as this byte via Convert.ToByte(key, 2)).
        public const byte BitCornerSW = 1 << 0;
        public const byte BitCornerSE = 1 << 1;
        public const byte BitCornerNE = 1 << 2;
        public const byte BitCornerNW = 1 << 3;

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

        /// <summary>
        /// Computes the 4-bit CORNER mask for <paramref name="cell"/> under the
        /// Corner16 auto-tile model — see <see cref="Valkur.Data.Corner16Slot"/> for
        /// the bit layout this returns.
        ///
        /// <para>
        /// <b>Why this is not a dual grid.</b> A <c>TerrainMap</c> stores one terrain
        /// per CELL, but corner-Wang art is authored per grid POINT (the vertex shared
        /// by 4 cells). The textbook fix for that mismatch is a dual grid: render each
        /// corner tile offset by half a cell so it's centered on a vertex instead of a
        /// cell. Valkur can't do that here — tiles are painted onto a Unity
        /// <c>Tilemap</c> in whole integer cells, so every visual tile drawn for cell
        /// <c>C</c> must still occupy exactly cell <c>C</c>. Instead, each of C's 4
        /// corners is derived from the 2x2 block of cells that TOUCH that corner's
        /// vertex:
        /// </para>
        /// <list type="bullet">
        /// <item>NW corner &lt;- block { C, N, W, NW }</item>
        /// <item>NE corner &lt;- block { C, N, E, NE }</item>
        /// <item>SE corner &lt;- block { C, S, E, SE }</item>
        /// <item>SW corner &lt;- block { C, S, W, SW }</item>
        /// </list>
        /// <para>
        /// A corner reads as <paramref name="terrain"/> when a MAJORITY (3 or 4 of the
        /// 4 cells) of its block matches. When the block is split exactly 2-2, the tie
        /// is resolved by cell C's own terrain — an ambiguous checkerboard corner
        /// renders as whatever the painted cell itself is, rather than an arbitrary
        /// pick. Cells outside the grid do NOT count as <paramref name="terrain"/> —
        /// same convention <see cref="CardinalMask"/> already uses — so the world edge
        /// behaves identically under both models.
        /// </para>
        /// </summary>
        public static byte CornerMask(IReadOnlyDictionary<Vector2Int, string> grid,
                                       Vector2Int cell, string terrain)
        {
            if (grid == null) return 0;

            string center = CellTerrain(grid, cell);

            byte mask = 0;
            if (CornerBlockMatches(grid, cell, Vector2Int.up,   Vector2Int.left,  terrain, center)) mask |= BitCornerNW;
            if (CornerBlockMatches(grid, cell, Vector2Int.up,   Vector2Int.right, terrain, center)) mask |= BitCornerNE;
            if (CornerBlockMatches(grid, cell, Vector2Int.down, Vector2Int.right, terrain, center)) mask |= BitCornerSE;
            if (CornerBlockMatches(grid, cell, Vector2Int.down, Vector2Int.left,  terrain, center)) mask |= BitCornerSW;
            return mask;
        }

        private static bool CornerBlockMatches(IReadOnlyDictionary<Vector2Int, string> grid, Vector2Int cell,
                                                Vector2Int vertical, Vector2Int horizontal,
                                                string terrain, string centerTerrain)
        {
            int matches = centerTerrain == terrain ? 1 : 0;
            if (NeighborMatches(grid, cell + vertical, terrain)) matches++;
            if (NeighborMatches(grid, cell + horizontal, terrain)) matches++;
            if (NeighborMatches(grid, cell + vertical + horizontal, terrain)) matches++;

            if (matches >= 3) return true;
            if (matches <= 1) return false;
            // Exactly 2 of 4 match (a diagonal split): the painted cell's own terrain
            // breaks the tie instead of an arbitrary majority pick.
            return centerTerrain == terrain;
        }

        private static string CellTerrain(IReadOnlyDictionary<Vector2Int, string> grid, Vector2Int cell)
        {
            return grid.TryGetValue(cell, out var value) ? value : null;
        }

        private static bool NeighborMatches(IReadOnlyDictionary<Vector2Int, string> grid,
                                             Vector2Int key, string terrain)
        {
            return grid.TryGetValue(key, out var value) && value == terrain;
        }
    }
}
