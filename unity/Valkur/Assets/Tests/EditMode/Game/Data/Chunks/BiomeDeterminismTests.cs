using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Data.Chunks
{
    /// <summary>
    /// Pins the deterministic-generation contract that Phase-4 networking
    /// relies on. Three properties every biome MUST satisfy:
    ///
    ///   1. Reproducibility: same (seed, coord, biome.Version) -> identical
    ///      ChunkData every time.
    ///   2. Coordinate isolation: different chunk coords with the same
    ///      seed produce DIFFERENT chunks (no global noise alias).
    ///   3. Seed isolation: same coord with different world seeds produces
    ///      DIFFERENT chunks (worlds do not share bedrock).
    ///
    /// These are independent of the specific biome — both UniformFill and
    /// NoiseSplit need them, just with different "differs" definitions.
    /// </summary>
    [TestFixture]
    public class BiomeDeterminismTests
    {
        private const int Size       = 16;
        private const int LayerCount = 1;

        private static DictionaryTileIdTable BuildTable(params string[] names)
        {
            var t = new DictionaryTileIdTable();
            foreach (var n in names) t.Register(n);
            return t;
        }

        private static ChunkData Generate(IBiome biome, ChunkCoord coord, long seed,
                                          ITileIdTable tiles)
        {
            var ctx = new BiomeContext(seed, coord, Size, LayerCount, tiles);
            return biome.GenerateChunk(coord, seed, ctx);
        }

        // ── UniformFill: simplest deterministic biome ───────────────────────────

        [Test]
        public void UniformFill_AllCellsAreTheRegisteredTile()
        {
            var tiles = BuildTable("grass");
            var biome = new UniformFillBiome("uniform", "grass");
            var data = Generate(biome, new ChunkCoord(WorldId.Base, 0, 0), 42L, tiles);

            ushort grassId = tiles.GetId("grass");
            Assert.AreNotEqual(0, grassId, "Tile must be registered first.");
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    Assert.AreEqual(grassId, data.Get(0, x, y),
                        $"Cell ({x},{y}) should be the uniform tile.");
        }

        [Test]
        public void UniformFill_ProducesEmptyChunk_WhenTileNotRegistered()
        {
            var tiles = BuildTable(); // empty table
            var biome = new UniformFillBiome("uniform", "missing");
            var data = Generate(biome, new ChunkCoord(WorldId.Base, 0, 0), 42L, tiles);
            Assert.IsTrue(data.IsEmpty(),
                "When the tile is unknown the biome must NOT poison the buffer " +
                "with a fake id; the chunk stays empty so the registry mistake " +
                "is visible at runtime instead of corrupting persistence.");
        }

        // ── NoiseSplit: full deterministic noise pipeline ───────────────────────

        [Test]
        public void NoiseSplit_SameSeedAndCoord_ProducesIdenticalChunks()
        {
            var tiles = BuildTable("grass", "dirt");
            var biome = new NoiseSplitBiome("split", "grass", "dirt");
            var coord = new ChunkCoord(WorldId.Base, 3, 5);

            uint crc1 = Generate(biome, coord, 42L, tiles).ComputeCrc32();
            uint crc2 = Generate(biome, coord, 42L, tiles).ComputeCrc32();

            Assert.AreEqual(crc1, crc2,
                "Reproducibility: the same (seed, coord) must always yield the " +
                "same chunk byte-for-byte. Phase 4 client prediction depends on " +
                "this — without it, a freshly-connected client and the server " +
                "would disagree about terrain.");
        }

        [Test]
        public void NoiseSplit_DifferentCoords_ProduceDifferentChunks()
        {
            var tiles = BuildTable("grass", "dirt");
            var biome = new NoiseSplitBiome("split", "grass", "dirt");
            uint crcA = Generate(biome, new ChunkCoord(WorldId.Base, 0, 0), 42L, tiles).ComputeCrc32();
            uint crcB = Generate(biome, new ChunkCoord(WorldId.Base, 1, 0), 42L, tiles).ComputeCrc32();
            Assert.AreNotEqual(crcA, crcB,
                "Coordinate isolation: two chunks at different positions must " +
                "produce different patterns. If they collide, every chunk in " +
                "the world would tile the same noise — useless for terrain.");
        }

        [Test]
        public void NoiseSplit_DifferentSeeds_ProduceDifferentChunks()
        {
            var tiles = BuildTable("grass", "dirt");
            var biome = new NoiseSplitBiome("split", "grass", "dirt");
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            uint crc42  = Generate(biome, coord, 42L,  tiles).ComputeCrc32();
            uint crc99  = Generate(biome, coord, 99L,  tiles).ComputeCrc32();
            Assert.AreNotEqual(crc42, crc99,
                "Seed isolation: each world seed must produce a distinct world. " +
                "Without this, every world with the same biome would look identical.");
        }

        [Test]
        public void NoiseSplit_BothTilesAppear_GivenAReasonableThreshold()
        {
            // Sanity: with a 0.5 threshold the chunk should hold a non-trivial
            // mix of both ids — otherwise the noise sampler is biased and the
            // determinism tests above could pass while the output is a single
            // colour.
            var tiles = BuildTable("grass", "dirt");
            var biome = new NoiseSplitBiome("split", "grass", "dirt", threshold: 0.5f);
            var data  = Generate(biome, new ChunkCoord(WorldId.Base, 0, 0), 42L, tiles);

            ushort grass = tiles.GetId("grass");
            ushort dirt  = tiles.GetId("dirt");
            int g = 0, d = 0;
            for (int i = 0; i < Size * Size; i++)
            {
                if (data.Layers[0][i] == grass) g++;
                else if (data.Layers[0][i] == dirt) d++;
            }
            Assert.Greater(g, 0, "At least one cell must be 'grass'.");
            Assert.Greater(d, 0, "At least one cell must be 'dirt'.");
        }
    }
}
