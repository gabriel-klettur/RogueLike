using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Terrain
{
    /// <summary>
    /// Unit tests for <see cref="BitmaskCalculator"/>.
    ///
    /// Exercises the cardinal mask logic that drives the auto-tile solver:
    /// off-grid neighbors, mismatched-terrain neighbors, and every combination
    /// of N/E/S/W neighbors of the same terrain.
    /// </summary>
    [TestFixture]
    public class BitmaskCalculatorTests
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
        public void EmptyGrid_ReturnsZero()
        {
            var grid = Grid();
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(0, mask, "Empty grid should yield mask=0 (Isolated).");
        }

        [Test]
        public void NullGrid_ReturnsZero()
        {
            byte mask = BitmaskCalculator.CardinalMask(null, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(0, mask);
        }

        [Test]
        public void NeighborSameTerrainNorth_SetsBitN()
        {
            var grid = Grid((0, 1, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(BitmaskCalculator.BitN, mask);
        }

        [Test]
        public void NeighborSameTerrainEast_SetsBitE()
        {
            var grid = Grid((1, 0, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(BitmaskCalculator.BitE, mask);
        }

        [Test]
        public void NeighborSameTerrainSouth_SetsBitS()
        {
            var grid = Grid((0, -1, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(BitmaskCalculator.BitS, mask);
        }

        [Test]
        public void NeighborSameTerrainWest_SetsBitW()
        {
            var grid = Grid((-1, 0, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(BitmaskCalculator.BitW, mask);
        }

        [Test]
        public void AllFourNeighborsSameTerrain_ReturnsCenterMask()
        {
            var grid = Grid(
                (0, 1,  Grass),
                (1, 0,  Grass),
                (0, -1, Grass),
                (-1, 0, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(0b1111, mask);
        }

        [Test]
        public void NeighborDifferentTerrain_DoesNotCount()
        {
            var grid = Grid((0, 1, Dirt), (1, 0, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(BitmaskCalculator.BitE, mask);
        }

        [Test]
        public void DiagonalNeighbors_DoNotAffectMask()
        {
            var grid = Grid(
                (1, 1,  Grass),
                (-1, -1, Grass),
                (1, -1, Grass),
                (-1, 1, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(0, mask, "Only N/E/S/W neighbors should affect the cardinal mask.");
        }

        [Test]
        public void VerticalLine_NS_ReturnsMask5()
        {
            var grid = Grid((0, 1, Grass), (0, -1, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(0b0101, mask);
        }

        [Test]
        public void HorizontalLine_EW_ReturnsMask10()
        {
            var grid = Grid((1, 0, Grass), (-1, 0, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), Grass);
            Assert.AreEqual(0b1010, mask);
        }

        [Test]
        public void EmptyTerrainString_OnlyMatchesEmptyEntries()
        {
            var grid = Grid((0, 1, ""), (1, 0, Grass));
            byte mask = BitmaskCalculator.CardinalMask(grid, new Vector2Int(0, 0), "");
            Assert.AreEqual(BitmaskCalculator.BitN, mask);
        }
    }
}
