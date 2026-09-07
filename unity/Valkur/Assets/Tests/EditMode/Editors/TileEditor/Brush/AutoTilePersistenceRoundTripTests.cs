// The terrain layer's round trip to disk, and the seam between two zones.
//
// The layer is keyed by VERTEX and the zone is measured in CELLS, and those two counts differ by
// one on each axis. Writing only w x h dropped every vertex on a zone's top and right edge —
// 101 of them per 50x50 zone, measured — so a boundary painted against that edge came back with
// two corners unknown, could no longer be resolved or cured, and a later stroke there read the
// absent vertices as the primary terrain and drew a false edge. Nothing failed; the data was
// simply a row short, and only the far edge of the world showed it.
//
// These tests walk the composition rather than either half. A matrix that round-trips and a
// loader that reads it were both individually correct while the pair lost the rim.

using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Brush
{
    [TestFixture]
    public class AutoTilePersistenceRoundTripTests
    {
        private const int ZoneW = 50;
        private const int ZoneH = 50;

        /// <summary>Stamps every vertex the zone spans — (w+1) x (h+1) of them.</summary>
        private static TerrainMap FullyStamped(int ox, int oy, string terrain)
        {
            var map = new TerrainMap();
            for (int y = 0; y <= ZoneH; y++)
            for (int x = 0; x <= ZoneW; x++)
                map.SetTerrain(new Vector2Int(ox + x, oy + y), terrain);
            return map;
        }

        [Test]
        public void TheMatrixSpansTheZonesVERTICES_NotItsCells()
        {
            var matrix = new TerrainMap().BuildMatrix(0, 0, ZoneW, ZoneH);

            Assert.AreEqual(ZoneH + 1, matrix.GetLength(0),
                "a zone of h cells is bounded by h+1 rows of vertices");
            Assert.AreEqual(ZoneW + 1, matrix.GetLength(1),
                "and by w+1 columns");
        }

        [Test]
        public void EveryStampedVertexSurvivesTheRoundTrip()
        {
            var before = FullyStamped(0, 0, "grass");
            Assert.AreEqual((ZoneW + 1) * (ZoneH + 1), before.Count, "sanity: the map holds every vertex");

            var after = new TerrainMap();
            after.LoadMatrix(0, 0, before.BuildMatrix(0, 0, ZoneW, ZoneH));

            Assert.AreEqual(before.Count, after.Count,
                $"{before.Count - after.Count} vertices were lost writing the zone out");

            for (int y = 0; y <= ZoneH; y++)
            for (int x = 0; x <= ZoneW; x++)
                Assert.AreEqual("grass", after.GetTerrain(new Vector2Int(x, y)),
                    $"vertex ({x},{y}) did not survive");
        }

        [Test]
        public void TheZonesFarEdgeIsNotDropped()
        {
            // The specific loss: x = w and y = h are the far edges, and they are corners of the
            // last row and column of cells. Losing them makes those cells unresolvable.
            var before = FullyStamped(0, 0, "dirt");
            var after = new TerrainMap();
            after.LoadMatrix(0, 0, before.BuildMatrix(0, 0, ZoneW, ZoneH));

            for (int y = 0; y <= ZoneH; y++)
                Assert.AreEqual("dirt", after.GetTerrain(new Vector2Int(ZoneW, y)),
                    $"the right edge vertex ({ZoneW},{y}) was dropped");
            for (int x = 0; x <= ZoneW; x++)
                Assert.AreEqual("dirt", after.GetTerrain(new Vector2Int(x, ZoneH)),
                    $"the top edge vertex ({x},{ZoneH}) was dropped");
        }

        [Test]
        public void AZoneWhoseOnlyTerrainIsOnItsFarEdge_IsStillWritten()
        {
            // HasAnyInRect decides whether the zone gets a "terrains" field at all. Asking about
            // the CELL span would call such a zone empty and write nothing, losing the lot.
            var map = new TerrainMap();
            map.SetTerrain(new Vector2Int(ZoneW, ZoneH), "grass");

            Assert.IsTrue(map.HasAnyInRect(0, 0, ZoneW, ZoneH),
                "a zone whose only terrain sits on its far corner must not be skipped as empty");
        }

        [Test]
        public void TwoAdjacentZones_ShareTheSeamVertices()
        {
            // Zones are laid out 50 apart, so zone A's x=50 vertices are zone B's x=0 ones. Both
            // must write them, or the seam between two painted zones is decided by whichever was
            // saved last.
            var a = FullyStamped(0, 0, "water");
            var b = FullyStamped(ZoneW, 0, "water_deep");

            var loaded = new TerrainMap();
            loaded.LoadMatrix(0, 0, a.BuildMatrix(0, 0, ZoneW, ZoneH));
            loaded.LoadMatrix(ZoneW, 0, b.BuildMatrix(ZoneW, 0, ZoneW, ZoneH));

            for (int y = 0; y <= ZoneH; y++)
                Assert.IsFalse(string.IsNullOrEmpty(loaded.GetTerrain(new Vector2Int(ZoneW, y))),
                    $"the shared seam vertex ({ZoneW},{y}) must carry a terrain from one side or the other");

            // Every cell of the left zone, INCLUDING its last column, has all four corners.
            var corners = new Vector2Int[4];
            for (int y = 0; y < ZoneH; y++)
            for (int x = 0; x < ZoneW; x++)
            {
                BitmaskCalculator.CornersOf(new Vector2Int(x, y), corners);
                foreach (var c in corners)
                    Assert.IsFalse(string.IsNullOrEmpty(loaded.GetTerrain(c)),
                        $"cell ({x},{y}) is missing corner {c} after both zones loaded");
            }
        }

        [Test]
        public void AMatrixWrittenByAnOlderBuild_StillLoads()
        {
            // The loader takes the matrix's OWN dimensions, never the zone's, so a file saved at
            // w x h keeps loading as w x h vertices. Widening the writer must not orphan data an
            // author already has on disk.
            var legacy = new string[ZoneH, ZoneW];
            for (int r = 0; r < ZoneH; r++)
            for (int c = 0; c < ZoneW; c++)
                legacy[r, c] = "grass";

            var map = new TerrainMap();
            map.LoadMatrix(0, 0, legacy);

            Assert.AreEqual(ZoneW * ZoneH, map.Count, "every entry of the old matrix must load");
            Assert.AreEqual("grass", map.GetTerrain(new Vector2Int(0, 0)));
            Assert.AreEqual("grass", map.GetTerrain(new Vector2Int(ZoneW - 1, ZoneH - 1)));
        }

        [Test]
        public void RowZeroIsTheTop_InBothDirections()
        {
            // The writer and the loader must agree on which end of the matrix is high Y, or a
            // reload flips every zone vertically — a failure that looks like corrupted art.
            var map = new TerrainMap();
            map.SetTerrain(new Vector2Int(0, ZoneH), "top");
            map.SetTerrain(new Vector2Int(0, 0), "bottom");

            var matrix = map.BuildMatrix(0, 0, ZoneW, ZoneH);
            Assert.AreEqual("top", matrix[0, 0], "row 0 is the HIGHEST Unity Y");
            Assert.AreEqual("bottom", matrix[ZoneH, 0], "the last row is the lowest");

            var back = new TerrainMap();
            back.LoadMatrix(0, 0, matrix);
            Assert.AreEqual("top", back.GetTerrain(new Vector2Int(0, ZoneH)));
            Assert.AreEqual("bottom", back.GetTerrain(new Vector2Int(0, 0)));
        }

        [Test]
        public void ClearingAVertex_ClearsItOnDiskToo()
        {
            // An empty string means "no terrain", not "leave whatever was there". Otherwise
            // erasing terrain never reaches the file and comes back on the next load.
            var map = FullyStamped(0, 0, "grass");
            map.SetTerrain(new Vector2Int(5, 5), null);

            var matrix = map.BuildMatrix(0, 0, ZoneW, ZoneH);
            Assert.AreEqual(string.Empty, matrix[ZoneH - 5, 5], "the cleared vertex is written as empty");

            var back = FullyStamped(0, 0, "dirt");   // pre-loaded with something else
            back.LoadMatrix(0, 0, matrix);
            Assert.IsNull(back.GetTerrain(new Vector2Int(5, 5)),
                "and loading it must clear the entry rather than leave the old value");
        }
    }
}
