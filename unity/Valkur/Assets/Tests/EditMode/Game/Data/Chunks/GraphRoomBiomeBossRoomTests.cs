using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Game.Data.Chunks
{
    /// <summary>
    /// Pins the boss-room extension on <see cref="GraphRoomBiome"/>:
    /// boss supercells are a strict subset of regular rooms (never void),
    /// the boss roll is independent from the room roll, distinct tiles
    /// are painted in the boss room interior, and IsBossSupercell is
    /// deterministic per (seed, supercell).
    /// </summary>
    [TestFixture]
    public class GraphRoomBiomeBossRoomTests
    {
        private const int Size = 8;
        private const int SupercellTiles = 16;
        private const long Seed = 7777L;

        private DictionaryTileIdTable _tiles;

        [SetUp]
        public void SetUp()
        {
            _tiles = new DictionaryTileIdTable();
            _tiles.Register("floor");
            _tiles.Register("wall");
            _tiles.Register("boss_floor");
            _tiles.Register("boss_wall");
        }

        private static GraphRoomBiome MakeWithBossRooms(
            int roomProb = 1000, int bossProb = 1000)
        {
            return new GraphRoomBiome(
                id: "test.boss",
                floorTile: "floor",
                wallTile: "wall",
                supercellTiles: SupercellTiles,
                roomProbabilityPerMille: roomProb,
                wallThickness: 1,
                bossFloorTile: "boss_floor",
                bossWallTile:  "boss_wall",
                bossRoomProbabilityPerMille: bossProb);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void BossSupercellIsStrictSubsetOfRoomSupercell()
        {
            // Mock: 0% room probability ⇒ boss roll always rejected (no rooms,
            // no bosses). Demonstrates the AND gate.
            var biome = MakeWithBossRooms(roomProb: 0, bossProb: 1000);
            for (int sx = 0; sx < 5; sx++)
            for (int sy = 0; sy < 5; sy++)
                Assert.IsFalse(biome.IsBossSupercell(sx, sy, Seed),
                    "A void supercell can never be a boss — boss flag is gated by IsRoomSupercell.");
        }

        [Test]
        public void Determinism_SameInputsYieldSameBossFlag()
        {
            var biome = MakeWithBossRooms(roomProb: 1000, bossProb: 100);
            // 1000 room prob = every supercell is a room, so the boss flag
            // depends purely on the boss hash. Same supercell coord must
            // produce the same flag every call.
            for (int i = 0; i < 5; i++)
            {
                bool first  = biome.IsBossSupercell(3, 4, Seed);
                bool second = biome.IsBossSupercell(3, 4, Seed);
                Assert.AreEqual(first, second);
            }
        }

        [Test]
        public void BossRoll_IsIndependentFromRoomRoll()
        {
            // With room prob 1000 ALL supercells are rooms; boss prob 1000
            // ALL rooms become bosses; should yield true for every supercell.
            var allBoss = MakeWithBossRooms(roomProb: 1000, bossProb: 1000);
            for (int sx = -2; sx < 3; sx++)
            for (int sy = -2; sy < 3; sy++)
                Assert.IsTrue(allBoss.IsBossSupercell(sx, sy, Seed),
                    "When both probabilities are 1000, every supercell must be a boss room.");

            // With boss prob 0, no boss rooms regardless of room flag.
            var noBoss = MakeWithBossRooms(roomProb: 1000, bossProb: 0);
            for (int sx = -2; sx < 3; sx++)
            for (int sy = -2; sy < 3; sy++)
                Assert.IsFalse(noBoss.IsBossSupercell(sx, sy, Seed),
                    "Boss prob 0 must produce zero boss rooms even when every supercell is a room.");
        }

        [Test]
        public void BossSupercell_PaintsBossTilesInsteadOfRegularTiles()
        {
            var biome = MakeWithBossRooms(roomProb: 1000, bossProb: 1000);

            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            var ctx = new BiomeContext(Seed, coord, Size, layerCount: 1, _tiles);
            var chunk = biome.GenerateChunk(coord, Seed, ctx);

            ushort bossFloor = _tiles.GetId("boss_floor");
            ushort bossWall  = _tiles.GetId("boss_wall");

            // Border cell must be boss wall.
            Assert.AreEqual(bossWall, chunk.Get(0, 0, 0),
                "Boss-room border cell must use boss wall tile.");
            // Interior cell — pick (3,3) to avoid the corridor mid-line at (8,*).
            Assert.AreEqual(bossFloor, chunk.Get(0, 3, 3),
                "Boss-room interior cell must use boss floor tile.");
        }

        [Test]
        public void DefaultBossTiles_FallBackToRegularTiles()
        {
            // No boss tiles supplied: ctor falls back to regular floor/wall.
            // The biome still classifies cells as BossFloor/BossWall but the
            // painted tile is identical — designers who don't care about
            // visual differentiation get a working biome anyway.
            var biome = new GraphRoomBiome(
                id: "test",
                floorTile: "floor",
                wallTile:  "wall",
                supercellTiles: SupercellTiles,
                roomProbabilityPerMille: 1000,
                bossFloorTile: null,   // unset
                bossWallTile:  null,
                bossRoomProbabilityPerMille: 1000);

            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            var ctx = new BiomeContext(Seed, coord, Size, layerCount: 1, _tiles);
            var chunk = biome.GenerateChunk(coord, Seed, ctx);

            ushort floor = _tiles.GetId("floor");
            ushort wall  = _tiles.GetId("wall");

            Assert.AreEqual(wall,  chunk.Get(0, 0, 0));
            Assert.AreEqual(floor, chunk.Get(0, 3, 3));
        }

        [Test]
        public void Default_BossProbability_IsReasonablyRare()
        {
            // Default 50 per-mille = 5% of room supercells become bosses.
            // Sweep 200 rooms (room prob 1000) and verify the count is in
            // the 1%-15% range (loose bounds for stochastic stability).
            var biome = new GraphRoomBiome(
                id: "test",
                floorTile: "f",
                wallTile:  "w",
                supercellTiles: SupercellTiles,
                roomProbabilityPerMille: 1000);
                // Boss prob omitted → uses default of 50 per-mille.

            int rooms = 0;
            int bossRooms = 0;
            for (int sx = 0; sx < 20; sx++)
            for (int sy = 0; sy < 20; sy++)
            {
                if (biome.IsRoomSupercell(sx, sy, Seed)) rooms++;
                if (biome.IsBossSupercell(sx, sy, Seed)) bossRooms++;
            }

            Assert.AreEqual(400, rooms, "Sanity: 1000 per-mille → all 400 supercells are rooms.");
            Assert.Greater(bossRooms, 0,
                "5% boss rate over 400 supercells must produce at least one boss " +
                "room; if 0, the boss roll is collapsing.");
            Assert.Less(bossRooms, 60,
                "5% boss rate must NOT exceed 15% over 400 samples; if it does, " +
                "the threshold logic is wrong.");
        }
    }
}
