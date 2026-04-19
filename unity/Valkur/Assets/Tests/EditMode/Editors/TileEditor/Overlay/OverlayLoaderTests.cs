using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.TileEditor
{
    /// <summary>
    /// Tests for <see cref="OverlayLoader"/> — the JSON→Tilemap painter shared by
    /// <see cref="WorldLoader"/> and <see cref="TileOverlayPersistence"/>.
    ///
    /// Covers: Python row-major Y-flip orientation, per-zone bounds clipping
    /// (the regression that produced the "sand bleeding into Lobby" bug),
    /// offset translation, clear-then-paint semantics for overrides, and
    /// graceful handling of unknown layers / missing files.
    ///
    /// Note: <see cref="OverlayLoader"/> resolves tile names via
    /// <c>Resources.Load&lt;Sprite&gt;("Tiles/" + name)</c>. Tests therefore discover a
    /// real sprite from <c>Assets/_Project/Resources/Tiles/</c> at runtime; if the
    /// project ships without any tile sprites the tests are marked Inconclusive
    /// rather than failing.
    /// </summary>
    [TestFixture]
    public class OverlayLoaderTests
    {
        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private string _tileNameA;
        private string _tileNameB;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            (_tileNameA, _tileNameB) = DiscoverTwoResourceTileNames();

            _tempDir = Path.Combine(Application.temporaryCachePath,
                "ValkurOverlayLoaderTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);

            UnityEngine.Object.DestroyImmediate(_gridGo);
            TileRegistry.Instance.Load(null);
        }

        private static (string a, string b) DiscoverTwoResourceTileNames()
        {
            // OverlayLoader resolves tile names with Resources.Load<Sprite>("Tiles/" + name).
            // We must therefore find names that actually load via that exact path; the sprite's
            // .name property does NOT necessarily match the asset's Resources path.
            // Try a curated list of known top-level tile paths first.
            string[] candidates = {
                "wall", "floor", "floor_1", "floor_2", "floor_3",
                "floor_4", "floor_5", "dungeon_tunnel"
            };
            var found = new List<string>();
            foreach (var name in candidates)
            {
                var s = Resources.Load<Sprite>("Tiles/" + name);
                if (s == null) continue;
                found.Add(name);
                if (found.Count >= 2) return (found[0], found[1]);
            }
            return (found.Count > 0 ? found[0] : null,
                    found.Count > 1 ? found[1] : null);
        }

        private void RequireResourceTiles()
        {
            if (string.IsNullOrEmpty(_tileNameA) || string.IsNullOrEmpty(_tileNameB))
                Assert.Inconclusive(
                    "OverlayLoader resolves tile names via Resources.Load — at least 2 tile sprites must exist " +
                    "under Assets/_Project/Resources/Tiles/. Skipping test.");
        }

        private string WriteOverlay(string fileName, string[][] groundRows)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"layers\":{\"Ground\":[");
            for (int r = 0; r < groundRows.Length; r++)
            {
                if (r > 0) sb.Append(',');
                sb.Append('[');
                for (int c = 0; c < groundRows[r].Length; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append('"').Append(groundRows[r][c] ?? "").Append('"');
                }
                sb.Append(']');
            }
            sb.Append("]}}");
            string path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        // ── Y-flip orientation ───────────────────────────────────────────

        [Test]
        public void LoadOverlayFromPath_RowZeroOfJson_PaintsAtTopOfRegion()
        {
            RequireResourceTiles();
            string json = WriteOverlay("orientation.json", new[]
            {
                new[] { _tileNameA },
                new[] { _tileNameB },
                new[] { _tileNameB },
            });

            OverlayLoader.LoadOverlayFromPath(json, _grid, offsetX: 0, offsetY: 0,
                clearLayerRegion: false, regionWidth: 0, regionHeight: 0);

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            Assert.IsNotNull(ground.GetTile(new Vector3Int(0, 2, 0)),
                "Row 0 of the JSON must paint at the TOP (highest y) of the painted region.");
            Assert.IsNotNull(ground.GetTile(new Vector3Int(0, 1, 0)));
            Assert.IsNotNull(ground.GetTile(new Vector3Int(0, 0, 0)));

            Assert.AreEqual(_tileNameA, TileRegistry.Instance.GetName(ground.GetTile(new Vector3Int(0, 2, 0))));
            Assert.AreEqual(_tileNameB, TileRegistry.Instance.GetName(ground.GetTile(new Vector3Int(0, 0, 0))));
        }

        // ── Offset application ──────────────────────────────────────────

        [Test]
        public void LoadOverlayFromPath_AppliesOffsetToBothAxes()
        {
            RequireResourceTiles();
            string json = WriteOverlay("offset.json", new[]
            {
                new[] { _tileNameA, _tileNameA },
            });

            OverlayLoader.LoadOverlayFromPath(json, _grid, offsetX: 100, offsetY: 200,
                clearLayerRegion: false, regionWidth: 0, regionHeight: 0);

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            Assert.IsNotNull(ground.GetTile(new Vector3Int(100, 200, 0)));
            Assert.IsNotNull(ground.GetTile(new Vector3Int(101, 200, 0)));
            Assert.IsNull(ground.GetTile(new Vector3Int(0, 0, 0)),
                "Tiles must NOT be painted at (0,0) when offset is non-zero.");
        }

        // ── Bounds clipping (anti-bleed) ─────────────────────────────────

        [Test]
        public void LoadOverlayFromPath_ClippingDisabledByDefault_PaintsAllTiles()
        {
            RequireResourceTiles();
            string json = WriteOverlay("noclip.json", new[]
            {
                new[] { _tileNameA, _tileNameA, _tileNameA },
                new[] { _tileNameA, _tileNameA, _tileNameA },
                new[] { _tileNameA, _tileNameA, _tileNameA },
            });

            OverlayLoader.LoadOverlayFromPath(json, _grid, 0, 0,
                clearLayerRegion: false, regionWidth: 0, regionHeight: 0,
                maxWidth: 0, maxHeight: 0);

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            int painted = 0;
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    if (ground.GetTile(new Vector3Int(x, y, 0)) != null) painted++;
            Assert.AreEqual(9, painted);
        }

        [Test]
        public void LoadOverlayFromPath_ClippingEnabled_SkipsTilesOutsideZoneFootprint()
        {
            RequireResourceTiles();
            string json = WriteOverlay("clip.json", new[]
            {
                new[] { _tileNameA, _tileNameA, _tileNameA, _tileNameA },
                new[] { _tileNameA, _tileNameA, _tileNameA, _tileNameA },
                new[] { _tileNameA, _tileNameA, _tileNameA, _tileNameA },
                new[] { _tileNameA, _tileNameA, _tileNameA, _tileNameA },
            });

            LogAssert.ignoreFailingMessages = true;

            OverlayLoader.LoadOverlayFromPath(json, _grid, offsetX: 0, offsetY: 0,
                clearLayerRegion: false, regionWidth: 0, regionHeight: 0,
                maxWidth: 2, maxHeight: 2);

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            int painted = 0;
            for (int y = -2; y < 6; y++)
                for (int x = -2; x < 6; x++)
                    if (ground.GetTile(new Vector3Int(x, y, 0)) != null) painted++;

            Assert.AreEqual(4, painted,
                "Bounds-clipped overlay must paint at most maxWidth × maxHeight tiles. " +
                "This is the regression guard for the sand-bleeding-into-Lobby bug.");
        }

        // ── Clear-then-paint (override semantics) ────────────────────────

        [Test]
        public void LoadOverlayFromPath_ClearLayerRegion_ErasesOldTilesBeforePainting()
        {
            RequireResourceTiles();
            string preJson = WriteOverlay("pre.json", new[]
            {
                new[] { _tileNameB, _tileNameB },
                new[] { _tileNameB, _tileNameB },
            });
            OverlayLoader.LoadOverlayFromPath(preJson, _grid, 0, 0, false, 0, 0);

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            Assert.IsNotNull(ground.GetTile(new Vector3Int(0, 0, 0)), "Pre-paint sanity (0,0).");
            Assert.IsNotNull(ground.GetTile(new Vector3Int(1, 1, 0)), "Pre-paint sanity (1,1).");

            string json = WriteOverlay("clear.json", new[]
            {
                new[] { _tileNameA, ""         },
                new[] { "",         ""         },
            });

            OverlayLoader.LoadOverlayFromPath(json, _grid, 0, 0,
                clearLayerRegion: true, regionWidth: 2, regionHeight: 2);

            Assert.IsNotNull(ground.GetTile(new Vector3Int(0, 1, 0)),
                "Override must paint a tile at top-left.");
            Assert.AreEqual(_tileNameA,
                TileRegistry.Instance.GetName(ground.GetTile(new Vector3Int(0, 1, 0))));
            Assert.IsNull(ground.GetTile(new Vector3Int(1, 1, 0)),
                "Cell explicitly empty in override must be cleared.");
            Assert.IsNull(ground.GetTile(new Vector3Int(0, 0, 0)),
                "Cell explicitly empty in override must be cleared.");
            Assert.IsNull(ground.GetTile(new Vector3Int(1, 0, 0)),
                "Cell explicitly empty in override must be cleared.");
        }

        // ── Resilience ───────────────────────────────────────────────────

        [Test]
        public void LoadOverlayFromPath_MissingFile_LogsErrorAndDoesNotThrow()
        {
            string missing = Path.Combine(_tempDir, "nope.json");
            LogAssert.Expect(LogType.Error, $"[OverlayLoader] Overlay file not found: {missing}");

            Assert.DoesNotThrow(() =>
                OverlayLoader.LoadOverlayFromPath(missing, _grid, 0, 0, false, 0, 0));
        }

        [Test]
        public void LoadOverlayFromPath_UnknownLayer_LogsWarningAndSkips()
        {
            string path = Path.Combine(_tempDir, "badlayer.json");
            File.WriteAllText(path, "{\"layers\":{\"NotARealLayer\":[[\"anything\"]]}}");

            LogAssert.Expect(LogType.Warning, "[OverlayLoader] Unknown layer 'NotARealLayer', skipping.");
            LogAssert.ignoreFailingMessages = true;

            Assert.DoesNotThrow(() =>
                OverlayLoader.LoadOverlayFromPath(path, _grid, 0, 0, false, 0, 0));
        }
    }
}
