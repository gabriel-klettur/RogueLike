using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.AutoTile
{
    /// <summary>
    /// Focused integration tests for the terrain side of overlay persistence:
    /// serialize a synthetic terrain matrix into the JSON format that
    /// <c>TileOverlayPersistence.SerializeOverlay</c> emits, then verify
    /// <see cref="OverlayLoader.ApplyTerrainsFromPath"/> parses it back into
    /// the same cells. Backwards-compat is also exercised by parsing a JSON
    /// that contains <c>layers</c> only — older overlays must still load
    /// without throwing when the new field is absent.
    /// </summary>
    [TestFixture]
    public class TerrainOverlayLoaderTests
    {
        private string _tempPath;

        [SetUp]
        public void SetUp()
        {
            _tempPath = Path.Combine(Path.GetTempPath(),
                $"valkur_terrain_overlay_test_{System.Guid.NewGuid():N}.overlay.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
        }

        // The shape that TileOverlayPersistence.SerializeOverlay produces when
        // a TerrainMap is present (verified by reading the static partial).
        private const string OverlayWithTerrains =
            "{\n" +
            "  \"layers\": {\n" +
            "    \"Ground\": [\n" +
            "      [\"\", \"\"],\n" +
            "      [\"\", \"\"]\n" +
            "    ]\n" +
            "  },\n" +
            "  \"terrains\": [\n" +
            "    [\"grass\", \"grass\"],\n" +
            "    [\"\", \"dirt\"]\n" +
            "  ]\n" +
            "}";

        private const string OverlayWithoutTerrains =
            "{\n" +
            "  \"layers\": {\n" +
            "    \"Ground\": [\n" +
            "      [\"floor\", \"floor\"],\n" +
            "      [\"floor\", \"floor\"]\n" +
            "    ]\n" +
            "  }\n" +
            "}";

        [Test]
        public void ApplyTerrainsFromPath_LoadsMatrixIntoTerrainMap()
        {
            File.WriteAllText(_tempPath, OverlayWithTerrains);
            var map = new TerrainMap();
            int written = OverlayLoader.ApplyTerrainsFromPath(_tempPath, map, 10, 20);

            Assert.AreEqual(3, written, "3 non-empty cells in the matrix.");

            // Layer matrices and terrain matrices both put row 0 = top of zone.
            // h=2 → row 0 unityY = 20 + (2-1-0) = 21; row 1 unityY = 20.
            Assert.AreEqual("grass", map.GetTerrain(new Vector2Int(10, 21)));
            Assert.AreEqual("grass", map.GetTerrain(new Vector2Int(11, 21)));
            Assert.IsNull   (map.GetTerrain(new Vector2Int(10, 20)), "empty string clears.");
            Assert.AreEqual("dirt",  map.GetTerrain(new Vector2Int(11, 20)));
        }

        [Test]
        public void ApplyTerrainsFromPath_LegacyOverlayWithoutField_NoOp()
        {
            File.WriteAllText(_tempPath, OverlayWithoutTerrains);
            var map = new TerrainMap();
            int written = OverlayLoader.ApplyTerrainsFromPath(_tempPath, map, 0, 0);
            Assert.AreEqual(0, written);
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void ApplyTerrainsFromPath_NullMap_ReturnsZero()
        {
            File.WriteAllText(_tempPath, OverlayWithTerrains);
            int written = OverlayLoader.ApplyTerrainsFromPath(_tempPath, null, 0, 0);
            Assert.AreEqual(0, written);
        }

        [Test]
        public void ApplyTerrainsFromPath_MissingFile_ReturnsZero()
        {
            var map = new TerrainMap();
            int written = OverlayLoader.ApplyTerrainsFromPath("Z:/does/not/exist.json", map, 0, 0);
            Assert.AreEqual(0, written);
        }

        [Test]
        public void ApplyTerrainsFromPath_RoundTripWithBuildMatrix()
        {
            // Build a matrix from a TerrainMap, hand-format the JSON the way the
            // persistence layer would, and verify the loader rebuilds an identical
            // map.
            var src = new TerrainMap();
            src.SetTerrain(new Vector2Int(0, 1), "grass");  // top-left
            src.SetTerrain(new Vector2Int(1, 1), "grass");  // top-right
            src.SetTerrain(new Vector2Int(0, 0), "dirt");   // bottom-left
            // bottom-right left empty

            // Emit the matrix AT ITS OWN DIMENSIONS, exactly as the production serializer's
            // AppendMetadataMatrix does. Bounding this loop by the zone's cell count instead is
            // the defect that shipped: BuildMatrix spans (w+1) x (h+1) vertices, so a w x h
            // writer truncates the LAST row — which, with row 0 as the top, is the BOTTOM one —
            // and the reader, which does take the matrix's own row count, then places every
            // remaining row one tile too high.
            var matrix = src.BuildMatrix(0, 0, 2, 2);
            int rows = matrix.GetLength(0), cols = matrix.GetLength(1);
            string json = "{\n  \"layers\": {},\n  \"terrains\": [\n";
            for (int row = 0; row < rows; row++)
            {
                json += "    [";
                for (int col = 0; col < cols; col++)
                {
                    if (col > 0) json += ", ";
                    json += "\"" + (matrix[row, col] ?? "") + "\"";
                }
                json += row == rows - 1 ? "]\n" : "],\n";
            }
            json += "  ]\n}";
            File.WriteAllText(_tempPath, json);

            var dst = new TerrainMap();
            int written = OverlayLoader.ApplyTerrainsFromPath(_tempPath, dst, 0, 0);
            Assert.AreEqual(3, written);

            Assert.AreEqual("grass", dst.GetTerrain(new Vector2Int(0, 1)));
            Assert.AreEqual("grass", dst.GetTerrain(new Vector2Int(1, 1)));
            Assert.AreEqual("dirt",  dst.GetTerrain(new Vector2Int(0, 0)));
            Assert.IsNull(dst.GetTerrain(new Vector2Int(1, 0)));
        }
    }
}
