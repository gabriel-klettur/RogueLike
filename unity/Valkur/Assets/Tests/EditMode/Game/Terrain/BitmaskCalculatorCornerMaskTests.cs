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
    /// <para>The grid is keyed by VERTEX. The tile drawn at cell <c>(x, y)</c> takes its
    /// four corners from the four vertices <c>(x, y+1)</c>, <c>(x+1, y+1)</c>,
    /// <c>(x+1, y)</c>, <c>(x, y)</c> — one vertex per corner, no vote and no tie-break.
    /// The terrain layer is therefore offset half a cell from the render layer, which is
    /// the textbook dual grid, while each drawn tile still occupies one whole Unity
    /// tilemap cell.</para>
    ///
    /// <para>This file used to pin a CELL-keyed majority vote: each corner read the 2x2
    /// block of cells touching it and a 2-2 split was broken by the painted cell's own
    /// terrain. That convention is what made a straight border between two areas of the
    /// same pack render as solid primary butted against solid secondary — measured on the
    /// shipped water pack, exactly two signatures across the whole boundary and no
    /// transition tile anywhere along it. The tests below are the same questions asked of
    /// the model that can actually draw that border.</para>
    /// </summary>
    [TestFixture]
    public class BitmaskCalculatorCornerMaskTests
    {
        private const string Grass = "grass";
        private const string Dirt = "dirt";

        /// <summary>Builds a VERTEX-keyed terrain grid.</summary>
        private static IReadOnlyDictionary<Vector2Int, string> Grid(params (int x, int y, string t)[] vertices)
        {
            var d = new Dictionary<Vector2Int, string>(vertices.Length);
            foreach (var (x, y, t) in vertices)
                d[new Vector2Int(x, y)] = t;
            return d;
        }

        /// <summary>The four corners of the tile at (0,0): SW (0,0), SE (1,0), NW (0,1), NE (1,1).</summary>
        private static byte MaskAtOrigin(IReadOnlyDictionary<Vector2Int, string> grid) =>
            BitmaskCalculator.CornerMask(grid, new Vector2Int(0, 0), Dirt);

        [Test]
        public void NullGrid_ReturnsZero()
        {
            Assert.AreEqual(0, BitmaskCalculator.CornerMask(null, new Vector2Int(0, 0), Dirt));
        }

        [Test]
        public void EmptyGrid_UnknownCell_ReturnsZero_DoesNotThrow()
        {
            var grid = Grid();
            byte mask = 0;
            Assert.DoesNotThrow(() => mask = BitmaskCalculator.CornerMask(grid, new Vector2Int(5, 5), Dirt));
            Assert.AreEqual(0, mask, "A tile none of whose corners was painted must read as CornerNone.");
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

        // ── Each corner is exactly one vertex ────────────────────────────────────

        [Test]
        public void CornersOf_NamesTheFourVerticesInBitOrder()
        {
            var corners = new Vector2Int[4];
            BitmaskCalculator.CornersOf(new Vector2Int(3, 7), corners);

            Assert.AreEqual(new Vector2Int(3, 8), corners[0], "NW");
            Assert.AreEqual(new Vector2Int(4, 8), corners[1], "NE");
            Assert.AreEqual(new Vector2Int(4, 7), corners[2], "SE");
            Assert.AreEqual(new Vector2Int(3, 7), corners[3], "SW");
        }

        [TestCase(0, 1, 0b1000, TestName = "OneVertex_NW")]
        [TestCase(1, 1, 0b0100, TestName = "OneVertex_NE")]
        [TestCase(1, 0, 0b0010, TestName = "OneVertex_SE")]
        [TestCase(0, 0, 0b0001, TestName = "OneVertex_SW")]
        public void ASingleSecondaryVertex_SetsExactlyItsOwnBit(int vx, int vy, int expected)
        {
            // The whole point of the model: one vertex answers one corner. Under the old
            // majority vote a lone secondary cell set NO bit at all — an isolated speck
            // was invisible, and so was every straight border.
            Assert.AreEqual(expected, MaskAtOrigin(Grid((vx, vy, Dirt))));
        }

        [Test]
        public void AllFourCornersSecondary_ReturnsCornerFull()
        {
            Assert.AreEqual(0b1111, MaskAtOrigin(Grid(
                (0, 0, Dirt), (1, 0, Dirt), (0, 1, Dirt), (1, 1, Dirt))));
        }

        [Test]
        public void NoSecondaryAnywhere_ReturnsCornerNone()
        {
            Assert.AreEqual(0b0000, MaskAtOrigin(Grid(
                (0, 0, Grass), (1, 0, Grass), (0, 1, Grass), (1, 1, Grass))));
        }

        [Test]
        public void AVertexNotAdjacentToTheCell_IsIgnored()
        {
            // (2,2) is a corner of the tile at (1,1) and (2,2) and (1,2) and (2,1) —
            // never of the tile at (0,0). A model that read neighbours rather than
            // corners would leak it in.
            Assert.AreEqual(0b0000, MaskAtOrigin(Grid((2, 2, Dirt), (-1, -1, Dirt))));
        }

        // ── The border the old model could not draw ──────────────────────────────

        [Test]
        public void StraightVerticalBorder_ProducesAHalfAndHalfTile()
        {
            // Vertices at x >= 1 are the secondary terrain. The tile at (0,0) then has
            // its two EAST corners secondary and its two WEST corners primary, which is
            // the vertical seam tile. Measured on the shipped water pack, the old
            // cell-keyed vote produced only 0000 and 1111 across this same boundary.
            var grid = Grid(
                (0, 0, Grass), (0, 1, Grass),
                (1, 0, Dirt), (1, 1, Dirt),
                (2, 0, Dirt), (2, 1, Dirt));

            Assert.AreEqual(BitmaskCalculator.BitCornerNE | BitmaskCalculator.BitCornerSE,
                MaskAtOrigin(grid), "east half secondary");
            Assert.AreEqual(0b1111, BitmaskCalculator.CornerMask(grid, new Vector2Int(1, 0), Dirt),
                "the tile fully inside the secondary area stays solid");
        }

        [Test]
        public void StraightHorizontalBorder_ProducesAHalfAndHalfTile()
        {
            var grid = Grid(
                (0, 0, Grass), (1, 0, Grass),
                (0, 1, Dirt), (1, 1, Dirt));

            Assert.AreEqual(BitmaskCalculator.BitCornerNW | BitmaskCalculator.BitCornerNE,
                MaskAtOrigin(grid), "north half secondary");
        }

        [Test]
        public void AVertexAbsentFromTheGrid_ReadsAsPrimary()
        {
            var explicitPrimary = Grid((0, 0, Dirt), (1, 0, Grass), (0, 1, Grass), (1, 1, Grass));
            var absent = Grid((0, 0, Dirt));

            Assert.AreEqual(MaskAtOrigin(explicitPrimary), MaskAtOrigin(absent),
                "An unpainted vertex must be indistinguishable from one stamped with the " +
                "primary terrain, so an untouched world reads as solid primary.");
        }

        [Test]
        public void OneStampedVertex_IsACornerOfFourDifferentTiles()
        {
            // This is the property the 2x2 brush exists for: a vertex is shared, so
            // painting one moves four tiles at once and each sees it in a different corner.
            var grid = Grid((0, 0, Dirt));

            Assert.AreEqual(BitmaskCalculator.BitCornerSW,
                BitmaskCalculator.CornerMask(grid, new Vector2Int(0, 0), Dirt));
            Assert.AreEqual(BitmaskCalculator.BitCornerSE,
                BitmaskCalculator.CornerMask(grid, new Vector2Int(-1, 0), Dirt));
            Assert.AreEqual(BitmaskCalculator.BitCornerNW,
                BitmaskCalculator.CornerMask(grid, new Vector2Int(0, -1), Dirt));
            Assert.AreEqual(BitmaskCalculator.BitCornerNE,
                BitmaskCalculator.CornerMask(grid, new Vector2Int(-1, -1), Dirt));
        }
    }
}
