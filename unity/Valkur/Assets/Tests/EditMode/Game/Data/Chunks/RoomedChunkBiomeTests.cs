using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Game.Data.Chunks
{
    /// <summary>
    /// Pins the determinism + shape contracts of <see cref="RoomedChunkBiome"/>:
    /// same seed + coord must produce identical chunks (Phase-4 networking
    /// parity), rooms have a wall border, corridors are pure floor, voids
    /// are empty, and the probability extremes (0 and 1000 per-mille) force
    /// the chunk into a single deterministic shape.
    /// </summary>
    [TestFixture]
    public class RoomedChunkBiomeTests
    {
        private const int Size = 8;

        private DictionaryTileIdTable _tiles;
        private ushort _floor, _wall;

        [SetUp]
        public void SetUp()
        {
            _tiles = new DictionaryTileIdTable();
            _tiles.Register("floor");
            _tiles.Register("wall");
            _floor = _tiles.GetId("floor");
            _wall  = _tiles.GetId("wall");
        }

        private static ChunkData Generate(RoomedChunkBiome b, DictionaryTileIdTable tiles,
                                          long seed, int cx, int cy)
        {
            var coord = new ChunkCoord(WorldId.Base, cx, cy);
            var ctx = new BiomeContext(seed, coord, Size, layerCount: 1, tiles);
            return b.GenerateChunk(coord, seed, ctx);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void AllRoom_PaintsBorderWallAndInteriorFloor()
        {
            // 1000-per-mille forces the room branch every roll.
            var biome = new RoomedChunkBiome("test.allroom", "floor", "wall",
                roomProbabilityPerMille: 1000,
                corridorProbabilityPerMille: 0);
            var chunk = Generate(biome, _tiles, seed: 1L, cx: 0, cy: 0);

            // Corners and edges → wall.
            Assert.AreEqual(_wall, chunk.Get(0, 0, 0));
            Assert.AreEqual(_wall, chunk.Get(0, Size - 1, 0));
            Assert.AreEqual(_wall, chunk.Get(0, 0, Size - 1));
            Assert.AreEqual(_wall, chunk.Get(0, Size - 1, Size - 1));
            // Interior cell → floor.
            Assert.AreEqual(_floor, chunk.Get(0, Size / 2, Size / 2));
        }

        [Test]
        public void AllCorridor_FillsWithFloor_NoWalls()
        {
            // 0% room, 1000% corridor (clamped to 1000) → every chunk is corridor.
            var biome = new RoomedChunkBiome("test.allcorridor", "floor", "wall",
                roomProbabilityPerMille: 0,
                corridorProbabilityPerMille: 1000);
            var chunk = Generate(biome, _tiles, seed: 1L, cx: 0, cy: 0);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                Assert.AreEqual(_floor, chunk.Get(0, x, y),
                    $"Corridor chunk must be pure floor; got non-floor at ({x},{y}).");
        }

        [Test]
        public void AllVoid_LeavesChunkEmpty()
        {
            // 0% room, 0% corridor → 100% void → all zero ids.
            var biome = new RoomedChunkBiome("test.allvoid", "floor", "wall",
                roomProbabilityPerMille: 0,
                corridorProbabilityPerMille: 0);
            var chunk = Generate(biome, _tiles, seed: 1L, cx: 0, cy: 0);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                Assert.AreEqual((ushort)0, chunk.Get(0, x, y),
                    $"Void chunk must hold id 0 (no tile); got non-zero at ({x},{y}).");
        }

        [Test]
        public void SameSeedAndCoord_ProduceIdenticalChunks()
        {
            var biome = new RoomedChunkBiome("test.det", "floor", "wall");
            var a = Generate(biome, _tiles, seed: 42L, cx: 3, cy: 5);
            var b = Generate(biome, _tiles, seed: 42L, cx: 3, cy: 5);
            Assert.AreEqual(a.ComputeCrc32(), b.ComputeCrc32(),
                "Same seed + coord must produce identical chunks (Phase-4 networking parity).");
        }

        [Test]
        public void DifferentCoords_ProduceDifferentLayouts()
        {
            // With balanced probabilities, two distant chunks must produce
            // distinct rolls. A single-coord rule that hashes the same value
            // for every chunk would silently break network parity — this test
            // catches that regression.
            var biome = new RoomedChunkBiome("test.distinct", "floor", "wall",
                roomProbabilityPerMille: 500,
                corridorProbabilityPerMille: 250);

            // Sweep enough chunk pairs that at least one must differ.
            uint reference = Generate(biome, _tiles, seed: 1L, cx: 0, cy: 0).ComputeCrc32();
            bool foundDistinct = false;
            for (int i = 1; i <= 8 && !foundDistinct; i++)
            {
                uint other = Generate(biome, _tiles, seed: 1L, cx: i, cy: i).ComputeCrc32();
                if (other != reference) foundDistinct = true;
            }
            Assert.IsTrue(foundDistinct,
                "At least one of 8 distinct chunk coords must yield a different " +
                "CRC than the reference; if all match, the per-chunk roll is broken.");
        }

        [Test]
        public void IsHandcrafted_False_AndIdMatches()
        {
            var biome = new RoomedChunkBiome("dungeon.test", "f", "w");
            Assert.IsFalse(biome.IsHandcrafted);
            Assert.AreEqual(1, biome.Version);
            Assert.AreEqual("dungeon.test", biome.Id);
        }
    }
}
