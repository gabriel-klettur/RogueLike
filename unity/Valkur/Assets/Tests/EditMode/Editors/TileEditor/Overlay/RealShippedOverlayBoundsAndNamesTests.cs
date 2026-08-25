using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// Data-integrity checks for the REAL <c>*.overlay.json</c> files shipped under
    /// <c>StreamingAssets/Maps/</c> — the ones the game actually loads, not synthetic
    /// fixtures. Complements <c>OverlayCollisionDataIntegrityTests</c> (which checks
    /// JSON parse-ability, the presence of a <c>layers</c> dictionary, and
    /// Collision-cell schema) with the two things it explicitly does NOT check:
    ///
    ///   1. Every layer/terrains/collisionTags/layerJumps matrix, for EVERY shipped
    ///      overlay (not just Collision), has the exact row/column dimensions the
    ///      canonical zone size declares in <c>zones_database.json</c> — no ragged
    ///      rows, no under/over-sized matrices. A mismatch here means either
    ///      <c>WorldLoader</c>'s unclipped base-map pass (it calls
    ///      <c>OverlayLoader.LoadOverlayFromPath</c> with <c>maxWidth</c>/<c>maxHeight</c>
    ///      left at the default 0 — no clipping) paints past the declared zone
    ///      footprint into a neighbour, or the override pass's <c>clearLayerRegion</c>
    ///      clears the wrong rectangle.
    ///   2. Every non-empty tile-name cell in every shipped overlay resolves to a
    ///      real sprite through the ACTUAL <see cref="OverlayLoader.LoadOverlay(string, WorldGridBuilder, int, int)"/>
    ///      pipeline — <c>OverlayCollisionDataIntegrityTests</c> explicitly defers
    ///      resolution to the PlayMode composite-bake fixture, which only ever
    ///      inspects the Collision layer's painted-cell COUNT, not per-cell name
    ///      resolvability, and never for the other 8 layers.
    /// </summary>
    [TestFixture]
    public class RealShippedOverlayBoundsAndNamesTests
    {
        private static readonly string MapsDir =
            Path.Combine(Application.streamingAssetsPath, "Maps");
        private static readonly string DatabasePath =
            Path.Combine(MapsDir, "zones_database.json");

        private GameObject _gridGo;
        private WorldGridBuilder _grid;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _gridGo = new GameObject("RealOverlayResolveGrid");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            LogAssert.ignoreFailingMessages = false;
        }

        private static IEnumerable<string> AllOverlayFiles()
        {
            if (!Directory.Exists(MapsDir)) yield break;
            foreach (var path in Directory.GetFiles(MapsDir, "*.overlay.json"))
                yield return path;
        }

        private static (int width, int height) ReadCanonicalZoneSize()
        {
            Assert.IsTrue(File.Exists(DatabasePath), $"zones_database.json missing: {DatabasePath}");
            var root = MiniJsonRuntime.Deserialize(File.ReadAllText(DatabasePath)) as Dictionary<string, object>;
            Assert.IsNotNull(root, "zones_database.json must parse.");
            Assert.IsTrue(root.ContainsKey("zone_width_tiles"), "zones_database.json must declare zone_width_tiles.");
            Assert.IsTrue(root.ContainsKey("zone_height_tiles"), "zones_database.json must declare zone_height_tiles.");
            int w = System.Convert.ToInt32(root["zone_width_tiles"]);
            int h = System.Convert.ToInt32(root["zone_height_tiles"]);
            Assert.Greater(w, 0, "Canonical zone width must be positive.");
            Assert.Greater(h, 0, "Canonical zone height must be positive.");
            return (w, h);
        }

        [Test]
        public void ZonesDatabase_DeclaresAPositiveCanonicalZoneSize()
        {
            var (w, h) = ReadCanonicalZoneSize();
            Assert.Greater(w, 0);
            Assert.Greater(h, 0);
        }

        // ── Dimensions / ragged rows, every layer, every shipped file ────────

        [Test]
        public void EveryShippedOverlay_EveryMatrix_MatchesCanonicalZoneSize_NoRaggedRows()
        {
            var (zoneW, zoneH) = ReadCanonicalZoneSize();
            var offenders = new List<string>();
            int filesInspected = 0;

            foreach (var path in AllOverlayFiles())
            {
                filesInspected++;
                string fileName = Path.GetFileName(path);
                var root = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                if (root == null) { offenders.Add($"{fileName}: failed to parse"); continue; }

                if (root.TryGetValue("layers", out var layersObj) &&
                    layersObj is Dictionary<string, object> layers)
                {
                    foreach (var kvp in layers)
                        CheckMatrix(fileName, kvp.Key, kvp.Value as List<object>, zoneW, zoneH, offenders);
                }
                else
                {
                    offenders.Add($"{fileName}: missing or malformed 'layers' dictionary");
                }

                foreach (var extraKey in new[] { "terrains", "collisionTags", "layerJumps" })
                {
                    if (root.TryGetValue(extraKey, out var extraObj))
                        CheckMatrix(fileName, extraKey, extraObj as List<object>, zoneW, zoneH, offenders);
                }
            }

            Assert.Greater(filesInspected, 0, "Did not find a single *.overlay.json to inspect.");
            if (offenders.Count > 0)
                Assert.Fail($"Overlay matrices not matching the canonical {zoneW}x{zoneH} zone size " +
                    "(or containing ragged rows):\n  - " + string.Join("\n  - ", offenders));
        }

        private static void CheckMatrix(string fileName, string matrixKey, List<object> rows,
            int zoneW, int zoneH, List<string> offenders)
        {
            if (rows == null)
            {
                offenders.Add($"{fileName}[{matrixKey}]: not a matrix (null or wrong JSON type)");
                return;
            }
            if (rows.Count != zoneH)
                offenders.Add($"{fileName}[{matrixKey}]: {rows.Count} rows, expected {zoneH}");

            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r] as List<object>;
                if (row == null)
                {
                    offenders.Add($"{fileName}[{matrixKey}] row {r}: not a list");
                    continue;
                }
                if (row.Count != zoneW)
                    offenders.Add($"{fileName}[{matrixKey}] row {r}: {row.Count} cols, expected {zoneW} (ragged row)");
            }
        }

        // ── Tile-name resolvability, through the real production loader ─────

        [Test]
        public void EveryShippedOverlay_EveryTileName_ResolvesThroughRealLoader()
        {
            int unresolved = 0;
            var messages = new List<string>();
            Application.LogCallback handler = (condition, stack, type) =>
            {
                if (type == LogType.Warning && condition != null &&
                    condition.Contains("[OverlayLoader] Could not resolve tile"))
                {
                    unresolved++;
                    if (messages.Count < 20) messages.Add(condition);
                }
            };

            int filesInspected = 0;
            Application.logMessageReceived += handler;
            try
            {
                foreach (var path in AllOverlayFiles())
                {
                    OverlayLoader.LoadOverlay(Path.GetFileName(path), _grid, 0, 0);
                    filesInspected++;
                }
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            Assert.Greater(filesInspected, 0, "Did not find a single *.overlay.json to inspect.");
            Assert.AreEqual(0, unresolved,
                "Every non-empty tile-name cell in every shipped overlay must resolve to a real " +
                "sprite under Resources/Tiles/ through the production OverlayLoader pipeline. " +
                $"First (of {unresolved}) unresolved warning(s):\n  - " + string.Join("\n  - ", messages));
        }
    }
}
