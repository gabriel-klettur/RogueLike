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
        /// The 4-bit CORNER mask for the tile drawn at <paramref name="cell"/> under the
        /// Corner16 model — see <see cref="Valkur.Data.Corner16Slot"/> for the bit layout.
        ///
        /// <para><b>The grid is keyed by VERTEX, not by cell.</b> Corner-Wang art is authored
        /// per grid POINT: each of a tile's four corners is one terrain, and the four corners
        /// of the tile at cell <c>(x, y)</c> are the four vertices <c>(x, y+1)</c>,
        /// <c>(x+1, y+1)</c>, <c>(x+1, y)</c>, <c>(x, y)</c>. So the terrain layer is offset
        /// half a cell from the render layer — the textbook dual grid — while every drawn tile
        /// still occupies exactly one whole Unity <c>Tilemap</c> cell, which is the constraint
        /// that made this look impossible.</para>
        ///
        /// <para><b>What it replaced, and why.</b> This used to store one terrain per CELL and
        /// derive each corner from a MAJORITY VOTE over the 2x2 block of cells touching that
        /// corner, with a 2-2 tie broken by the painted cell itself. Both halves were
        /// internally consistent and the result disagreed only with the screen. Measured over
        /// all 512 two-terrain 3x3 neighbourhoods, flipping ONLY the centre cell changed the
        /// signature in 508 of them — the tile drawn was very nearly a function of the cell's
        /// own terrain. The consequence is the one thing the model exists for: a straight
        /// border between two areas of the same pack produced exactly TWO signatures, 0000 and
        /// 1111, i.e. solid primary butted against solid secondary with no transition art at
        /// all. Measured live on the shipped <c>water_water_deep</c> pack over a 10x6 field
        /// split down the middle: 30 cells of 0000, 26 of 1111, and the only four boundary
        /// tiles were the outer corners of the painted rectangle. Reading vertices makes the
        /// boundary tile half one terrain and half the other by construction, because its two
        /// left corners and its two right corners really are different vertices.</para>
        ///
        /// <para>A vertex with no entry counts as NOT the secondary terrain, so an unpainted
        /// world reads as solid primary and painting the secondary carves into it.</para>
        /// </summary>
        public static byte CornerMask(IReadOnlyDictionary<Vector2Int, string> grid,
                                      Vector2Int cell, string terrain)
        {
            if (grid == null) return 0;

            byte mask = 0;
            if (VertexIs(grid, cell.x,     cell.y + 1, terrain)) mask |= BitCornerNW;
            if (VertexIs(grid, cell.x + 1, cell.y + 1, terrain)) mask |= BitCornerNE;
            if (VertexIs(grid, cell.x + 1, cell.y,     terrain)) mask |= BitCornerSE;
            if (VertexIs(grid, cell.x,     cell.y,     terrain)) mask |= BitCornerSW;
            return mask;
        }

        /// <summary>The four vertices that are the corners of the tile at <paramref name="cell"/>,
        /// in the bit order NW, NE, SE, SW. Painting all four is what fully determines one tile,
        /// and is why the AUTO brush forces a 2x2 footprint.</summary>
        public static void CornersOf(Vector2Int cell, Vector2Int[] into)
        {
            if (into == null || into.Length < 4) return;
            into[0] = new Vector2Int(cell.x,     cell.y + 1); // NW
            into[1] = new Vector2Int(cell.x + 1, cell.y + 1); // NE
            into[2] = new Vector2Int(cell.x + 1, cell.y);     // SE
            into[3] = new Vector2Int(cell.x,     cell.y);     // SW
        }

        private static bool VertexIs(IReadOnlyDictionary<Vector2Int, string> grid,
                                     int x, int y, string terrain)
        {
            return grid.TryGetValue(new Vector2Int(x, y), out var t) && t == terrain;
        }

        /// <summary>
        /// True when the grid holds <paramref name="terrain"/> at <paramref name="neighbor"/>.
        /// A key that is absent does NOT count as a match — used by the cardinal
        /// (Blob16) mask, which still reads one entry per CELL.
        /// </summary>
        private static bool NeighborMatches(IReadOnlyDictionary<Vector2Int, string> grid,
                                            Vector2Int neighbor, string terrain)
        {
            return grid.TryGetValue(neighbor, out var t) && t == terrain;
        }
    }
}
