using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Terrain
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
            var matrix = m.BuildMatrix(0, 0, 3, 2);
            Assert.AreEqual(2, matrix.GetLength(0));
            Assert.AreEqual(3, matrix.GetLength(1));
            for (int r = 0; r < 2; r++)
            for (int c = 0; c < 3; c++)
                Assert.AreEqual("", matrix[r, c]);
        }

        [Test]
        public void BuildMatrix_RowZeroIsTopOfZone()
        {
            var m = new TerrainMap();
            // origin (0,0) and h=2 → unityY for row 0 = 0 + (2-1-0) = 1.
            // Stamp 'grass' at (0,1) so it should land at matrix[0,0].
            m.SetTerrain(new Vector2Int(0, 1), "grass");
            m.SetTerrain(new Vector2Int(0, 0), "dirt");

            var matrix = m.BuildMatrix(0, 0, 1, 2);
            Assert.AreEqual("grass", matrix[0, 0], "row 0 = top of zone (highest unity y).");
            Assert.AreEqual("dirt", matrix[1, 0]);
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
