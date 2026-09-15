using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.AutoTile
{
    /// <summary>
    /// Tests <see cref="TerrainMap.BuildMatrix"/> / <see cref="TerrainMap.LoadMatrix"/>
    /// round-trip semantics: row 0 = top of zone, empty cells stay empty, partial
    /// loads don't disturb cells outside the rectangle.
    /// </summary>
    [TestFixture]
    public class TerrainMapMatrixTests
    {
        [Test]
        public void BuildMatrix_Empty_ReturnsAllEmptyStrings()
        {
            var m = new TerrainMap();
            // A w x h block of CELLS is bounded by (w+1) x (h+1) VERTICES, and this map is keyed
            // by vertex. The matrix spanning only w x h dropped every vertex on the zone's top
            // and right edge — 101 of them per shipped 50x50 zone, measured — so the cells there
            // came back with unknown corners.
            var matrix = m.BuildMatrix(0, 0, 3, 2);
            Assert.AreEqual(3, matrix.GetLength(0), "h + 1 rows of vertices");
            Assert.AreEqual(4, matrix.GetLength(1), "w + 1 columns of vertices");
            for (int r = 0; r < matrix.GetLength(0); r++)
            for (int c = 0; c < matrix.GetLength(1); c++)
                Assert.AreEqual("", matrix[r, c]);
        }

        [Test]
        public void BuildMatrix_RowZeroIsTopOfZone()
        {
            var m = new TerrainMap();
            // h=2 spans THREE rows of vertices (y = 2, 1, 0), so row 0 is y=2 — the top edge of
            // the top row of cells, which belongs to the zone as much as its bottom edge does.
            m.SetTerrain(new Vector2Int(0, 2), "top_edge");
            m.SetTerrain(new Vector2Int(0, 1), "grass");
            m.SetTerrain(new Vector2Int(0, 0), "dirt");

            var matrix = m.BuildMatrix(0, 0, 1, 2);
            Assert.AreEqual(3, matrix.GetLength(0), "h + 1 rows of vertices");
            Assert.AreEqual("top_edge", matrix[0, 0], "row 0 = top of zone (highest unity y).");
            Assert.AreEqual("grass", matrix[1, 0]);
            Assert.AreEqual("dirt", matrix[2, 0], "the last row is the lowest unity y.");
        }

        [Test]
        public void LoadMatrix_RoundTripsBuildMatrix()
        {
            var src = new TerrainMap();
            src.SetTerrain(new Vector2Int(0, 0), "grass");
            src.SetTerrain(new Vector2Int(1, 0), "dirt");
            src.SetTerrain(new Vector2Int(0, 1), "sand");
            src.SetTerrain(new Vector2Int(1, 1), "rock");

            var matrix = src.BuildMatrix(0, 0, 2, 2);
            var dst = new TerrainMap();
            dst.LoadMatrix(0, 0, matrix);

            Assert.AreEqual("grass", dst.GetTerrain(new Vector2Int(0, 0)));
            Assert.AreEqual("dirt",  dst.GetTerrain(new Vector2Int(1, 0)));
            Assert.AreEqual("sand",  dst.GetTerrain(new Vector2Int(0, 1)));
            Assert.AreEqual("rock",  dst.GetTerrain(new Vector2Int(1, 1)));
        }

        [Test]
        public void LoadMatrix_EmptyStringsClearCells()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector2Int(0, 0), "grass");
            var matrix = new string[1, 1];
            matrix[0, 0] = "";
            m.LoadMatrix(0, 0, matrix);
            Assert.IsNull(m.GetTerrain(new Vector2Int(0, 0)),
                "Empty-string entries must clear, not preserve.");
        }

        [Test]
        public void LoadMatrix_DoesNotTouchCellsOutsideRect()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector2Int(10, 10), "rock");

            var patch = new string[1, 1];
            patch[0, 0] = "grass";
            m.LoadMatrix(0, 0, patch);

            Assert.AreEqual("rock", m.GetTerrain(new Vector2Int(10, 10)),
                "loading a 1×1 patch at (0,0) shouldn't touch (10,10).");
            Assert.AreEqual("grass", m.GetTerrain(new Vector2Int(0, 0)));
        }

        [Test]
        public void HasAnyInRect_True_WhenCellInside()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector2Int(2, 3), "grass");
            Assert.IsTrue(m.HasAnyInRect(0, 0, 5, 5));
        }

        [Test]
        public void HasAnyInRect_False_WhenAllCellsEmpty()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector2Int(10, 10), "grass");
            Assert.IsFalse(m.HasAnyInRect(0, 0, 5, 5));
        }

        [Test]
        public void HasAnyInRect_False_WhenMapEmpty()
        {
            var m = new TerrainMap();
            Assert.IsFalse(m.HasAnyInRect(0, 0, 100, 100));
        }
    }
}
