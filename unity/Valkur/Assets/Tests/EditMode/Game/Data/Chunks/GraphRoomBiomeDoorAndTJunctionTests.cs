using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Game.Data.Chunks
{
    /// <summary>
    /// Pins the Door + T-junction extensions on <see cref="GraphRoomBiome"/>:
    /// boundary corridor cells become Door (visually distinct), cells where
    /// both corridor axes meet become TJunction (crossroad placement),
    /// and the door tile defaults sensibly when not supplied.
    /// </summary>
    [TestFixture]
    public class GraphRoomBiomeDoorAndTJunctionTests
    {
        private const int Size = 16;
        private const int SupercellTiles = 16;
        private const long Seed = 9999L;

        private DictionaryTileIdTable _tiles;

        [SetUp]
        public void SetUp()
        {
            _tiles = new DictionaryTileIdTable();
            _tiles.Register("floor");
            _tiles.Register("wall");
            _tiles.Register("door");
        }

        private static GraphRoomBiome Make(string doorTile = null)
        {
            return new GraphRoomBiome(
                id: "doors_test",
                floorTile: "floor",
                wallTile:  "wall",
                supercellTiles: SupercellTiles,
                roomProbabilityPerMille: 1000,
                wallThickness: 1,
                doorTile: doorTile);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void DoorCell_AppearsAtSupercellBorder_OnCorridorAxis()
        {
            var biome = Make("door");
            // (0, mid) lies on the supercell border (ox=0) AND on the
            // horizontal corridor mid-line (oy=mid). Should be a door.
            var kind = biome.ClassifyCell(0, SupercellTiles / 2, Seed);
            Assert.AreEqual(GraphRoomBiome.CellKind.Door, kind);
        }

        [Test]
        public void TJunctionCell_AppearsAtSupercellCentre_WhenBothAxesCarve()
        {
            var biome = Make();
            // (mid, mid) is the supercell centre. With every neighbour a
            // room (1000 per-mille), both axes fire → TJunction.
            var kind = biome.ClassifyCell(SupercellTiles / 2, SupercellTiles / 2, Seed);
            Assert.AreEqual(GraphRoomBiome.CellKind.TJunction, kind);
        }

        [Test]
        public void StraightCorridor_NotOnBorder_StaysAsCorridor()
        {
            var biome = Make();
            // Supercell (0,0) interior cell on the horizontal axis but NOT
            // on the vertical axis: e.g. (3, mid). Not a border, not both
            // axes — must be plain Corridor.
            var kind = biome.ClassifyCell(3, SupercellTiles / 2, Seed);
            Assert.AreEqual(GraphRoomBiome.CellKind.Corridor, kind,
                "Interior corridor cell on a single axis must stay Corridor — " +
                "Door is reserved for the supercell border.");
        }

        [Test]
        public void DoorTile_PaintedWhenSupplied()
        {
            var biome = Make("door");
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            var ctx = new BiomeContext(Seed, coord, Size, layerCount: 1, _tiles);
            var chunk = biome.GenerateChunk(coord, Seed, ctx);

            // Find at least one cell painted with the door tile.
            ushort doorId = _tiles.GetId("door");
            int doorCells = 0;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (chunk.Get(0, x, y) == doorId) doorCells++;

            Assert.Greater(doorCells, 0,
                "With every neighbour a room and a custom door tile, at least " +
                "the corridor entry cells must paint with the door tile.");
        }

        [Test]
        public void DoorTile_DefaultsToFloor_WhenNotSupplied()
        {
            // Without a door tile, doors paint the floor tile so undecorated
            // worlds still look continuous (no missing-tile holes).
            var biome = Make(doorTile: null);
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            var ctx = new BiomeContext(Seed, coord, Size, layerCount: 1, _tiles);
            var chunk = biome.GenerateChunk(coord, Seed, ctx);

            // (0, mid) is a Door cell. Without a custom door tile it must
            // be the floor id.
            ushort floorId = _tiles.GetId("floor");
            Assert.AreEqual(floorId, chunk.Get(0, 0, SupercellTiles / 2));
        }

        [Test]
        public void Determinism_DoorAndTJunctionClassificationIsStable()
        {
            var biome = Make();
            for (int i = 0; i < 5; i++)
            {
                var first  = biome.ClassifyCell(0, SupercellTiles / 2, Seed);
                var second = biome.ClassifyCell(0, SupercellTiles / 2, Seed);
                Assert.AreEqual(first, second);

                first  = biome.ClassifyCell(SupercellTiles / 2, SupercellTiles / 2, Seed);
                second = biome.ClassifyCell(SupercellTiles / 2, SupercellTiles / 2, Seed);
                Assert.AreEqual(first, second);
            }
        }
    }
}
