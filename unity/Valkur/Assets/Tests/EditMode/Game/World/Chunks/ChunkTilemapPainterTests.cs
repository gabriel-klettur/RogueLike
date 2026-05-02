using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;
using Valkur.Gameplay.World.Chunks;

namespace Valkur.Tests.EditMode.Game.World.Chunks
{
    /// <summary>
    /// End-to-end paint test: build a real Grid + Tilemap, paint a known
    /// ChunkData into it, query individual cells back. Proves the
    /// translation chain (id -> name -> asset -> Tilemap.SetTile)
    /// completes without losing data.
    /// </summary>
    [TestFixture]
    public class ChunkTilemapPainterTests
    {
        private GameObject _gridGo;
        private Tilemap _tilemap;
        private DictionaryTileIdTable _idTable;
        private Tile _grass, _dirt;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("PainterTestGrid");
            _gridGo.AddComponent<Grid>();
            var tmGo = new GameObject("PainterTestTilemap");
            tmGo.transform.SetParent(_gridGo.transform);
            _tilemap = tmGo.AddComponent<Tilemap>();
            tmGo.AddComponent<TilemapRenderer>();

            _idTable = new DictionaryTileIdTable();
            _idTable.Register("grass");
            _idTable.Register("dirt");
            _grass = ScriptableObject.CreateInstance<Tile>(); _grass.name = "grass";
            _dirt  = ScriptableObject.CreateInstance<Tile>(); _dirt.name  = "dirt";
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            if (_grass  != null) Object.DestroyImmediate(_grass);
            if (_dirt   != null) Object.DestroyImmediate(_dirt);
        }

        private TileBase NameLookup(string n) => n == "grass" ? (TileBase)_grass
                                              : n == "dirt"  ? (TileBase)_dirt
                                              : null;

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void Paint_FillsEveryCellAtChunkOrigin()
        {
            // Build a small 4x4 chunk fully covered with "grass".
            int size = 4;
            var data = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), size, 1);
            ushort grassId = _idTable.GetId("grass");
            for (int i = 0; i < size * size; i++) data.Layers[0][i] = grassId;

            var resolver = new TileIdTableResolver(_idTable, NameLookup);
            ChunkTilemapPainter.Paint(data, new[] { _tilemap }, resolver);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    Assert.AreSame(_grass, _tilemap.GetTile(new Vector3Int(x, y, 0)),
                        $"Cell ({x},{y}) must hold the grass tile after Paint.");
        }

        [Test]
        public void Paint_OffsetsCorrectlyByChunkCoordinate()
        {
            // Chunk at (3, -1) should paint into world tiles starting at
            // (3*size, -1*size). Verify a couple of edge cells.
            int size = 4;
            var coord = new ChunkCoord(WorldId.Base, 3, -1);
            var data = new ChunkData(coord, size, 1);
            ushort grassId = _idTable.GetId("grass");
            data.Set(0, 0, 0, grassId);             // bottom-left of chunk
            data.Set(0, size - 1, size - 1, grassId); // top-right of chunk

            var resolver = new TileIdTableResolver(_idTable, NameLookup);
            ChunkTilemapPainter.Paint(data, new[] { _tilemap }, resolver);

            int wx0 = coord.Cx * size;
            int wy0 = coord.Cy * size;
            Assert.AreSame(_grass, _tilemap.GetTile(new Vector3Int(wx0, wy0, 0)),
                "Chunk (cx,cy) bottom-left must land at world tile (cx*size, cy*size).");
            Assert.AreSame(_grass, _tilemap.GetTile(new Vector3Int(wx0 + size - 1, wy0 + size - 1, 0)),
                "Chunk top-right must land at the opposite corner of the chunk footprint.");
        }

        [Test]
        public void Paint_EmptyIds_LeaveCellsClear()
        {
            int size = 2;
            var data = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), size, 1);
            // One painted cell; the rest stay empty (id 0).
            data.Set(0, 0, 0, _idTable.GetId("grass"));

            var resolver = new TileIdTableResolver(_idTable, NameLookup);
            ChunkTilemapPainter.Paint(data, new[] { _tilemap }, resolver);

            Assert.AreSame(_grass, _tilemap.GetTile(new Vector3Int(0, 0, 0)));
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(1, 0, 0)),
                "Empty id must NOT paint a real tile — leaves the cell clear.");
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(0, 1, 0)));
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(1, 1, 0)));
        }

        [Test]
        public void Clear_RemovesEveryCellInTheChunkFootprint()
        {
            int size = 3;
            var data = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), size, 1);
            ushort grassId = _idTable.GetId("grass");
            for (int i = 0; i < size * size; i++) data.Layers[0][i] = grassId;

            var resolver = new TileIdTableResolver(_idTable, NameLookup);
            ChunkTilemapPainter.Paint(data, new[] { _tilemap }, resolver);

            ChunkTilemapPainter.Clear(data.Coord, size, new[] { _tilemap });
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    Assert.IsNull(_tilemap.GetTile(new Vector3Int(x, y, 0)),
                        $"Cell ({x},{y}) must be cleared after Clear() — chunk drop " +
                        "must not leave phantom tiles behind.");
        }
    }
}
