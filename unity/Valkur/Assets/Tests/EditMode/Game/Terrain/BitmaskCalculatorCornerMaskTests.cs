using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Terrain
{
    /// <summary>
    /// Unit tests for <see cref="BitmaskCalculator.CornerMask"/> — the Corner16
    /// counterpart of <see cref="BitmaskCalculatorTests"/>'s cardinal-mask coverage.
    ///
    /// Exercises the corner-majority convention documented on CornerMask itself:
    /// each corner reads the 2x2 block of cells sharing that corner's vertex, a
    /// majority (3-4 of 4) of the block matching the tested terrain sets the bit,
    /// an exact 2-2 split is broken by the painted cell's OWN terrain, and cells
    /// outside the grid never count as a match — same convention CardinalMask
    /// already uses.
    /// </summary>
    [TestFixture]
    public class BitmaskCalculatorCornerMaskTests
    {
        private const string Grass = "grass";
        private const string Dirt = "dirt";

        private static IReadOnlyDictionary<Vector2Int, string> Grid(params (int x, int y, string t)[] cells)
        {
            var d = new Dictionary<Vector2Int, string>(cells.Length);
            foreach (var (x, y, t) in cells)
                d[new Vector2Int(x, y)] = t;
            return d;
        }

        [Test]
        public void NullGrid_ReturnsZero()
        {
            byte mask = BitmaskCalculator.CornerMask(null, new Vector2Int(0, 0), Dirt);
            Assert.AreEqual(0, mask);
        }

        [Test]
        public void EmptyGrid_UnknownCell_ReturnsZero_DoesNotThrow()
        {
            var grid = Grid();
            byte mask = 0;
            Assert.DoesNotThrow(() => mask = BitmaskCalculator.CornerMask(grid, new Vector2Int(5, 5), Dirt));
            Assert.AreEqual(0, mask, "A cell nobody painted, in an otherwise empty grid, must read as CornerNone.");
        }

        [Test]
        public void BitConstants_MatchCorner16SlotBitLayout()
        {
            Assert.AreEqual(0b1000, BitmaskCalculator.BitCornerNW);
            Assert.AreEqual(0b0100, BitmaskCalculator.BitCornerNE);
            Assert.AreEqual(0b0010, BitmaskCalculator.BitCornerSE);
            Assert.AreEqual(0b0001, BitmaskCalculator.BitCornerSW);
            Assert.AreEqual((byte)Valkur.Data.Corner16Slot.CornerNW, BitmaskCalculator.BitCornerNW);
            Assert.AreEqual((byte)Valkur.Data.Corner16Slot.CornerNE, BitmaskCalculator.BitCornerNE);
            Assert.AreEqual((byte)Valkur.Data.Corner16Slot.CornerSE, BitmaskCalculator.BitCornerSE);
            Assert.AreEqual((byte)Valkur.Data.Corner16Slot.CornerSW, BitmaskCalculator.BitCornerSW);
        }

        [Test]
        public void FullySurroundedBySecondary_ReturnsCornerFull()
        {
            var grid = Grid(
                (0, 0, Grass),
                (0, 1, Dirt), (1, 0, Dirt), (0, -1, Dirt), (-1, 0, Dirt),
                (1, 1, Dirt), (-1, 1, Dirt), (1, -1, Dirt), (-1, -1, Dirt));
            byte mask = BitmaskCalculator.CornerMask(grid, new Vector2Int(0, 0), Dirt);
            Assert.AreEqual(0b1111, mask);
        }

        [Test]
        public void NoSecondaryAnywhere_ReturnsCornerNone()
        {
            var grid = Grid(
                (0, 0, Grass),
                (0, 1, Grass), (1, 0, Grass), (0, -1, Grass), (-1, 0, Grass));
            byte mask = BitmaskCalculator.CornerMask(grid, new Vector2Int(0, 0), Dirt);
            Assert.AreEqual(0b0000, mask);
        }

        [Test]
        public void NW_ThreeOfFourBlockMembersSecondary_SetsOnlyNWBit()
        {
            // NW block = {C, N, W, NW}. Center stays primary; N/W/NW form the
            // secondary majority. No other corner's block even reaches a tie.
            var grid = Grid((0, 0, Grass), (0, 1, Dirt), (-1, 0, Dirt), (-1, 1, Dirt));
            byte mask = BitmaskCalculator.CornerMask(grid, new Vector2Int(0, 0), Dirt);
            Assert.AreEqual(BitmaskCalculator.BitCornerNW, mask);
        }

        [Test]
        public void NE_ExactlyTwoOfFourSecondary_CenterPrimary_TieBreaksToFalseOnEveryCorner()
        {
            // NE block = {C, N, E, NE}. N and E are secondary (2 of 4), center is
            // primary and NE itself is unset — a 2-2 split resolved by the
            // painted cell's own (primary) terrain. N and E individually only
            // contribute 1 match each to the other three corners, so nothing
            // else even reaches the tie threshold.
            var grid = Grid((0, 0, Grass), (0, 1, Dirt), (1, 0, Dirt));
            byte mask = BitmaskCalculator.CornerMask(grid, new Vector2Int(0, 0), Dirt);
            Assert.AreEqual(0b0000, mask, "A 2-2 tie with a primary center must resolve to 'not secondary'.");
        }

        [Test]
        public void CenterSecondary_PlusNorthNeighborSecondary_TieBreaksBothNorthCornersToTrue()
        {
            // Only the CENTER cell's own terrain changes vs. the previous test (N
            // stays the sole secondary cardinal neighbor) — that alone is enough
            // to flip both NW and NE (the two corners whose block includes N)
            // across the tie threshold, because the center is now itself one of
            // the two secondary members of each of those blocks. SE/SW (whose
            // blocks don't include N) still have only one match (the center) and
            // stay below the tie threshold.
            var grid = Grid((0, 0, Dirt), (0, 1, Dirt));
            byte mask = BitmaskCalculator.CornerMask(grid, new Vector2Int(0, 0), Dirt);
            Assert.AreEqual(0b1100, mask);
        }

        [Test]
        public void OffGridNeighbor_CountsIdenticallyToAnExplicitPrimaryNeighbor()
        {
            var withExplicitPrimary = Grid((0, 0, Grass), (1, 1, Grass));
            var withMissingNeighbor = Grid((0, 0, Grass));

            byte a = BitmaskCalculator.CornerMask(withExplicitPrimary, new Vector2Int(0, 0), Dirt);
            byte b = BitmaskCalculator.CornerMask(withMissingNeighbor, new Vector2Int(0, 0), Dirt);
            Assert.AreEqual(a, b, "A neighbor absent from the grid must be indistinguishable from one " +
                "explicitly stamped with the primary terrain.");
        }

        [Test]
        public void IsolatedSecondarySpeck_SurroundedByNothing_ReadsAsCornerNone()
        {
            // A single secondary cell with every neighbor off-grid: every corner's
            // block has exactly one matching member (the cell itself), which never
            // reaches the tie threshold — it reads as solid primary, not a speck.
            var grid = Grid((0, 0, Dirt));
            byte mask = BitmaskCalculator.CornerMask(grid, new Vector2Int(0, 0), Dirt);
            Assert.AreEqual(0b0000, mask);
        }
    }
}
