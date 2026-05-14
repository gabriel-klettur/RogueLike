using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Gameplay.World.Layering
{
    /// <summary>
    /// Validate <see cref="VisualLayerProbe"/> against a real
    /// <see cref="WorldGridBuilder"/> hierarchy. Pins:
    ///   • Null inputs return safe defaults (no NRE).
    ///   • Sample fills the buffer with one bool per layer at the queried point.
    ///   • GetTopmostLayer returns the highest index with a tile.
    /// </summary>
    [TestFixture]
    public class VisualLayerProbeTests
    {
        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private UnityEngine.Tilemaps.Tile _tile;
        private bool[] _buf;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _tile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
            _tile.name = "test_probe_tile";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            _tile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);

            _buf = new bool[9];
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gridGo);
            Object.DestroyImmediate(_tile);
        }

        [Test]
        public void Sample_NullGrid_ReturnsZeroAndClearsBuffer()
        {
            for (int i = 0; i < _buf.Length; i++) _buf[i] = true; // pre-dirty
            int populated = VisualLayerProbe.Sample(Vector3.zero, null, _buf);
            Assert.AreEqual(0, populated);
            for (int i = 0; i < _buf.Length; i++)
                Assert.IsFalse(_buf[i], "Null grid must clear the buffer to all-false.");
        }

        [Test]
        public void Sample_EmptyWorld_BufferAllFalse()
        {
            int populated = VisualLayerProbe.Sample(new Vector3(2.5f, 3.5f, 0f), _grid, _buf);
            Assert.AreEqual(0, populated);
            for (int i = 0; i < _buf.Length; i++) Assert.IsFalse(_buf[i]);
        }

        [Test]
        public void Sample_PaintedCell_FlagsAllLayersWithTiles()
        {
            // Paint a tile on Ground (0) AND Decorations (5) at the same cell.
            _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground).SetTile(new Vector3Int(4, 4, 0), _tile);
            _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Decorations).SetTile(new Vector3Int(4, 4, 0), _tile);

            int populated = VisualLayerProbe.Sample(new Vector3(4.5f, 4.5f, 0f), _grid, _buf);
            Assert.AreEqual(2, populated);
            Assert.IsTrue(_buf[0],  "Ground layer should be flagged.");
            Assert.IsTrue(_buf[5],  "Decorations layer should be flagged.");
            Assert.IsFalse(_buf[1], "Layers without tiles must stay false.");
            Assert.IsFalse(_buf[8]);
        }

        [Test]
        public void Sample_QueryNeighbouringCell_LayerWithTileNotFlagged()
        {
            _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground).SetTile(new Vector3Int(0, 0, 0), _tile);

            // Probe one cell away — no tile there → all false.
            int populated = VisualLayerProbe.Sample(new Vector3(5.5f, 5.5f, 0f), _grid, _buf);
            Assert.AreEqual(0, populated);
        }

        [Test]
        public void GetTopmostLayer_ReturnsHighestIndexWithTile()
        {
            _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground).SetTile(new Vector3Int(1, 1, 0), _tile);
            _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.WallsBottom).SetTile(new Vector3Int(1, 1, 0), _tile);
            _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.OverheadDetails).SetTile(new Vector3Int(1, 1, 0), _tile);

            int topmost = VisualLayerProbe.GetTopmostLayer(new Vector3(1.5f, 1.5f, 0f), _grid);
            Assert.AreEqual((int)TilemapLayerSetup.TilemapLayer.OverheadDetails, topmost);
        }

        [Test]
        public void GetTopmostLayer_NoTiles_ReturnsMinusOne()
        {
            int topmost = VisualLayerProbe.GetTopmostLayer(new Vector3(0.5f, 0.5f, 0f), _grid);
            Assert.AreEqual(-1, topmost);
        }

        [Test]
        public void GetTopmostLayer_NullGrid_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, VisualLayerProbe.GetTopmostLayer(Vector3.zero, null));
        }
    }
}
