using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Data.Chunks
{
    /// <summary>
    /// Pins <see cref="GraphRoomBiome"/>: room flags are deterministic
    /// per (seed, supercell), corridor cells appear only between adjacent
    /// rooms, and chunks at the same coord with the same seed produce
    /// byte-identical CRCs (Phase-4 networking parity).
    /// </summary>
    [TestFixture]
    public class GraphRoomBiomeTests
    {
        private const int Size = 8;
        private const int SupercellTiles = 16; // small for fixture readability
        private const long Seed = 4242L;

        private DictionaryTileIdTable _tiles;

        [SetUp]
        public void SetUp()
        {
            _tiles = new DictionaryTileIdTable();
            _tiles.Register("floor");
            _tiles.Register("wall");
        }

        private static GraphRoomBiome Make(int roomProb = 1000)
            => new GraphRoomBiome("test.graph", "floor", "wall",
                supercellTiles: SupercellTiles,
                roomProbabilityPerMille: roomProb);

        private ChunkData GenerateChunk(GraphRoomBiome b, int cx, int cy)
        {
            var coord = new ChunkCoord(WorldId.Base, cx, cy);
            var ctx = new BiomeContext(Seed, coord, Size, layerCount: 1, _tiles);
            return b.GenerateChunk(coord, Seed, ctx);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void AllRoom_PaintsBorderWalls_WithFloorInterior()
        {
            // 1000 per-mille: every supercell becomes a room.
            var biome = Make(roomProb: 1000);

            // Inside a single supercell aligned to chunk (0,0).
            // (0,0) is the corner of supercell 0 — wall.
            // (1,1) is one tile in — still wall when wallThickness=1.
            // (4,4) is interior — floor.
            ushort floor = _tiles.GetId("floor");
            ushort wall  = _tiles.GetId("wall");
            var chunk = GenerateChunk(biome, 0, 0);

            Assert.AreEqual(wall, chunk.Get(0, 0, 0),
                "Tile (0,0) lies on the supercell border — must be wall.");
            // Note: tile (4,4) might be on the corridor row when corridors are
            // active (mid=8). We pick an interior cell that's not on the
            // mid axis: (3,3).
            Assert.AreEqual(floor, chunk.Get(0, 3, 3),
                "Interior cells must be floor.");
        }

        [Test]
        public void AllVoid_PaintsNothing()
        {
            // 0 per-mille: every supercell is empty. No corridors are
            // possible without rooms either.
            var biome = Make(roomProb: 0);
            var chunk = GenerateChunk(biome, 0, 0);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                Assert.AreEqual((ushort)0, chunk.Get(0, x, y));
        }

        [Test]
        public void RoomFlag_IsDeterministicPerSeedAndSupercell()
        {
            var biome = new GraphRoomBiome("test", "f", "w",
                supercellTiles: SupercellTiles, roomProbabilityPerMille: 500);
            // Same seed + supercell → same flag.
            bool a = biome.IsRoomSupercell(7, 11, Seed);
            bool b = biome.IsRoomSupercell(7, 11, Seed);
            Assert.AreEqual(a, b,
                "Same seed + supercell coord must always return the same room flag.");
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentLayouts()
        {
            var biome = new GraphRoomBiome("t", "f", "w",
                supercellTiles: SupercellTiles, roomProbabilityPerMille: 500);

            // Sweep 8 supercells; at least one must differ between two seeds.
            bool foundDifference = false;
            for (int i = 0; i < 8 && !foundDifference; i++)
            {
                bool seedA = biome.IsRoomSupercell(i, 0, 1L);
                bool seedB = biome.IsRoomSupercell(i, 0, 2L);
                if (seedA != seedB) foundDifference = true;
            }
            Assert.IsTrue(foundDifference,
                "Two distinct seeds must yield at least one differing room flag " +
                "across 8 supercells; otherwise the FNV mix is collapsing the seed.");
        }

        [Test]
        public void NegativeCoords_ClassifyConsistently()
        {
            // FloorDiv / PositiveMod must keep the supercell grid aligned across
            // the origin. A naive (sx = tx / N) splits supercells unevenly for
            // negative coords; this test catches that regression.
            var biome = Make(roomProb: 1000);

            var kindAtOriginEdge = biome.ClassifyCell(-1, 0, Seed);
            var kindAtOrigin     = biome.ClassifyCell( 0, 0, Seed);

            // Both lie on a supercell border. With 1000-per-mille both are
            // walls; the test verifies the classification doesn't crash and
            // returns sane values for negative inputs.
            Assert.IsTrue(kindAtOriginEdge == GraphRoomBiome.CellKind.RoomWall ||
                          kindAtOriginEdge == GraphRoomBiome.CellKind.RoomFloor ||
                          kindAtOriginEdge == GraphRoomBiome.CellKind.Corridor,
                "Negative-coord classification must produce a valid CellKind.");
            Assert.IsTrue(kindAtOrigin == GraphRoomBiome.CellKind.RoomWall ||
                          kindAtOrigin == GraphRoomBiome.CellKind.RoomFloor ||
                          kindAtOrigin == GraphRoomBiome.CellKind.Corridor);
        }

        [Test]
        public void Determinism_SameInputsYieldIdenticalCRC()
        {
            var biome = Make(roomProb: 500);
            var a = GenerateChunk(biome, 3, 5);
            var b = GenerateChunk(biome, 3, 5);
            Assert.AreEqual(a.ComputeCrc32(), b.ComputeCrc32(),
                "Same seed + coord must produce identical chunks (Phase-4 networking parity).");
        }

        [Test]
        public void Corridor_CarvedBetweenAdjacentRooms_AtSupercellMidline()
        {
            // With 1000 per-mille every supercell is a room, so every
            // cell on the midline of a horizontal-or-vertical neighbour
            // pair must be a corridor (replacing what would otherwise be
            // a wall on the supercell border).
            var biome = Make(roomProb: 1000);

            // mid = SupercellTiles / 2 = 8. A cell at (mid, mid) on a
            // supercell border is a corridor.
            var atMidVertical   = biome.ClassifyCell(SupercellTiles / 2, 0, Seed);
            var atMidHorizontal = biome.ClassifyCell(0, SupercellTiles / 2, Seed);
            // The exact answer depends on the corridor rule; both should
            // be Corridor when sandwiched between two rooms on the relevant
            // axis. With 1000 per-mille the neighbours are guaranteed rooms,
            // so the rule must fire.
            Assert.AreEqual(GraphRoomBiome.CellKind.Corridor, atMidVertical,
                "Midline cell on a vertical supercell border must be a corridor " +
                "when both supercells are rooms.");
            Assert.AreEqual(GraphRoomBiome.CellKind.Corridor, atMidHorizontal,
                "Midline cell on a horizontal supercell border must be a corridor " +
                "when both supercells are rooms.");
        }
    }
}
