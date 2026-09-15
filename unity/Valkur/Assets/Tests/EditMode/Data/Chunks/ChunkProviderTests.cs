using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Data.Chunks
{
    /// <summary>
    /// Pins the <see cref="IChunkProvider"/> contract for the two Phase-2
    /// implementations: in-memory (test fixture stand-in) and procedural
    /// (real production path against a deterministic biome).
    /// </summary>
    [TestFixture]
    public class ChunkProviderTests
    {
        // ── InMemoryChunkProvider ──────────────────────────────────────────────

        [Test]
        public void InMemory_Get_AfterSet_ReturnsSameInstance()
        {
            var provider = new InMemoryChunkProvider();
            var coord = new ChunkCoord(WorldId.Base, 1, 2);
            var data  = new ChunkData(coord, 4, 1);
            data.Set(0, 0, 0, 7);

            provider.Set(data);

            Assert.IsTrue(provider.Has(coord));
            Assert.AreSame(data, provider.Get(coord),
                "InMemory provider stores by reference; tests rely on this to " +
                "assert mutations they make after Set are visible.");
        }

        [Test]
        public void InMemory_Get_MissingCoord_Throws()
        {
            var provider = new InMemoryChunkProvider();
            Assert.Throws<System.InvalidOperationException>(
                () => provider.Get(new ChunkCoord(WorldId.Base, 0, 0)));
        }

        // ── ProceduralChunkProvider ────────────────────────────────────────────

        [Test]
        public void Procedural_Has_AlwaysTrue()
        {
            var biome = new UniformFillBiome("u", "grass");
            var tiles = new DictionaryTileIdTable();
            tiles.Register("grass");
            var p = new ProceduralChunkProvider(biome, 42L, 8, 1, tiles);
            Assert.IsTrue(p.Has(new ChunkCoord(WorldId.Base, 0, 0)));
            Assert.IsTrue(p.Has(new ChunkCoord(WorldId.Base, 9999, -9999)),
                "Procedural providers can serve every coordinate by definition.");
        }

        [Test]
        public void Procedural_TwoGetsForSameCoord_ProduceIdenticalCrc()
        {
            var biome = new NoiseSplitBiome("split", "a", "b");
            var tiles = new DictionaryTileIdTable();
            tiles.Register("a"); tiles.Register("b");
            var p = new ProceduralChunkProvider(biome, 42L, 8, 1, tiles);
            var coord = new ChunkCoord(WorldId.Base, 0, 0);

            uint c1 = p.Get(coord).ComputeCrc32();
            uint c2 = p.Get(coord).ComputeCrc32();
            Assert.AreEqual(c1, c2,
                "Procedural provider stateless re-generation must reproduce " +
                "the chunk bit-for-bit (no cached randomness leak between calls).");
        }

        [Test]
        public void Procedural_GetAsync_AndGet_MatchByteForByte()
        {
            var biome = new NoiseSplitBiome("split", "a", "b");
            var tiles = new DictionaryTileIdTable();
            tiles.Register("a"); tiles.Register("b");
            var p = new ProceduralChunkProvider(biome, 42L, 8, 1, tiles);
            var coord = new ChunkCoord(WorldId.Base, 1, 1);

            uint sync = p.Get(coord).ComputeCrc32();
            uint async_ = p.GetAsync(coord).GetAwaiter().GetResult().ComputeCrc32();
            Assert.AreEqual(sync, async_,
                "Phase 2 sync/async surfaces must produce identical output. " +
                "The async path is reserved for Phase 2.5 streaming and must " +
                "not silently change semantics.");
        }
    }
}
