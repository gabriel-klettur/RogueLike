using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// Pins the semantics of block painting in <see cref="OverlayLoader"/>.
    ///
    /// PaintLayer used to issue one SetTile per named cell — around 248,000 of them for
    /// a full world load. It now reads the destination rectangle, fills it, and writes
    /// it back in one SetTilesBlock.
    ///
    /// That swap is only safe because of one detail worth a test of its own:
    /// SetTilesBlock writes EVERY cell in the rectangle, nulls included, while SetTile
    /// only ever touched the cells the JSON named. If the buffer were not seeded from
    /// the tilemap first, a gap in an overlay would silently erase whatever had already
    /// been painted there instead of leaving it alone — and only the override pass wants
    /// an erase, which it gets by clearing the region explicitly beforehand.
    ///
    /// These drive <see cref="OverlayLoader.LoadOverlayFromRoot"/> with hand-built roots
    /// rather than temp files, so the assertions are about painting and nothing else.
    /// </summary>
    [TestFixture]
    public class OverlayLoaderBlockPaintTests
    {
        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private string _tileName;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _gridGo = new GameObject("WorldGridBuilder_BlockPaint");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _tileName = DiscoverResourceTileName();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>A tile name that actually resolves through Resources, or Inconclusive.</summary>
        private static string DiscoverResourceTileName()
        {
            var sprites = Resources.LoadAll<Sprite>("Tiles");
            if (sprites == null || sprites.Length == 0)
                Assert.Inconclusive("No tile sprites under Resources/Tiles — nothing to paint with.");
            return sprites[0].name;
        }

        private Tilemap Ground => _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);

        /// <summary>Builds { "layers": { "Ground": rows } } the way an overlay file parses.</summary>
        private static Dictionary<string, object> Root(params string[][] rows)
        {
            var rowList = new List<object>();
            foreach (var row in rows)
            {
                var cells = new List<object>();
                foreach (var c in row) cells.Add(c);
                rowList.Add(cells);
            }
            return new Dictionary<string, object>
            {
                ["layers"] = new Dictionary<string, object> { ["Ground"] = rowList }
            };
        }

        private void Paint(Dictionary<string, object> root, bool clearRegion = false,
                           int regionW = 0, int regionH = 0)
            => OverlayLoader.LoadOverlayFromRoot(root, _grid, 0, 0, clearRegion, regionW, regionH);

        // ── The regression this whole change hinges on ───────────────────────────

        [Test]
        public void Paint_GapInOverlay_LeavesAnExistingTileAlone()
        {
            // Something is already on the map — a previous zone, or the base pass.
            var existing = Ground.GetTile(new Vector3Int(0, 0, 0));
            Paint(Root(new[] { _tileName }));
            existing = Ground.GetTile(new Vector3Int(0, 0, 0));
            Assert.IsNotNull(existing, "Sanity: the first paint must land.");

            // A second overlay covering the same rectangle, but with that cell empty.
            Paint(Root(new[] { (string)null, _tileName }));

            Assert.IsNotNull(Ground.GetTile(new Vector3Int(0, 0, 0)),
                "An empty cell means 'the overlay says nothing about this tile', not 'erase it'. " +
                "SetTilesBlock writes nulls too, so the destination must be read first.");
            Assert.IsNotNull(Ground.GetTile(new Vector3Int(1, 0, 0)),
                "Sanity: the cell the second overlay did name must be painted.");
        }

        [Test]
        public void Paint_WithClearRegion_DoesErase()
        {
            Paint(Root(new[] { _tileName, _tileName }));
            Assert.IsNotNull(Ground.GetTile(new Vector3Int(0, 0, 0)), "Sanity: painted.");

            // The override path asks for a true erase by clearing the region first.
            Paint(Root(new[] { (string)null, _tileName }), clearRegion: true, regionW: 2, regionH: 1);

            Assert.IsNull(Ground.GetTile(new Vector3Int(0, 0, 0)),
                "With clearLayerRegion the empty cell must erase — that is what makes deleting " +
                "a tile in the Map Editor stick.");
            Assert.IsNotNull(Ground.GetTile(new Vector3Int(1, 0, 0)));
        }

        // ── Orientation and geometry ─────────────────────────────────────────────

        [Test]
        public void Paint_RowMajorInput_IsFlippedSoRowZeroIsTheTop()
        {
            // Two rows, only the FIRST (top in JSON) is filled.
            Paint(Root(new[] { _tileName }, new[] { (string)null }));

            Assert.IsNotNull(Ground.GetTile(new Vector3Int(0, 1, 0)),
                "JSON row 0 is the top of the map, which is the HIGHER tilemap y.");
            Assert.IsNull(Ground.GetTile(new Vector3Int(0, 0, 0)));
        }

        [Test]
        public void Paint_RaggedRows_PaintWhatTheyHaveWithoutThrowing()
        {
            Assert.DoesNotThrow(() =>
                Paint(Root(new[] { _tileName, _tileName, _tileName },
                           new[] { _tileName })));

            // Widest row defines the block; the short row's missing cells stay empty.
            Assert.IsNotNull(Ground.GetTile(new Vector3Int(2, 1, 0)), "Long row, last cell.");
            Assert.IsNotNull(Ground.GetTile(new Vector3Int(0, 0, 0)), "Short row, only cell.");
            Assert.IsNull(Ground.GetTile(new Vector3Int(2, 0, 0)), "Short row does not reach here.");
        }

        [Test]
        public void Paint_AllEmptyOverlay_TouchesNothingAndDoesNotThrow()
        {
            Paint(Root(new[] { _tileName, _tileName }));
            Assert.IsNotNull(Ground.GetTile(new Vector3Int(0, 0, 0)), "Sanity: painted.");

            Assert.DoesNotThrow(() => Paint(Root(new[] { (string)null, (string)null })));

            Assert.IsNotNull(Ground.GetTile(new Vector3Int(0, 0, 0)),
                "An overlay that names no tile must not write the block at all.");
        }

        [Test]
        public void Paint_ZeroRows_IsASafeNoOp()
        {
            Assert.DoesNotThrow(() => Paint(Root()));
        }

        // ── Degenerate input ─────────────────────────────────────────────────────

        [Test]
        public void LoadOverlayFromRoot_NullRoot_IsASafeNoOp()
        {
            Assert.DoesNotThrow(() => OverlayLoader.LoadOverlayFromRoot(null, _grid, 0, 0, false, 0, 0));
        }

        [Test]
        public void LoadOverlayFromRoot_NullGridBuilder_IsASafeNoOp()
        {
            Assert.DoesNotThrow(() =>
                OverlayLoader.LoadOverlayFromRoot(Root(new[] { _tileName }), null, 0, 0, false, 0, 0));
        }

        // ── Parse seam ───────────────────────────────────────────────────────────

        [Test]
        public void ParseOverlay_MissingFile_ReturnsNullRatherThanThrowing()
        {
            Assert.IsNull(OverlayLoader.ParseOverlay(
                System.IO.Path.Combine(Application.temporaryCachePath, "definitely-not-here.json")));
        }
    }
}
