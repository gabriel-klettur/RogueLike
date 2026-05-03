using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Data.Chunks
{
    /// <summary>
    /// Pins the Chebyshev-distance ring rule of <see cref="RingBiome"/>:
    /// the band around the world origin alternates primary / secondary
    /// tiles every <c>ringWidthTiles</c> steps. Independent of seed, RNG,
    /// or noise — same coord → same byte forever.
    /// </summary>
    [TestFixture]
    public class RingBiomeTests
    {
        private const int Size = 4;
        private const long Seed = 0L;

        private DictionaryTileIdTable _tiles;

        [SetUp]
        public void SetUp()
        {
            _tiles = new DictionaryTileIdTable();
            _tiles.Register("primary");
            _tiles.Register("secondary");
        }

        private static ChunkData Generate(RingBiome biome, DictionaryTileIdTable tiles,
                                          int cx, int cy)
        {
            var coord = new ChunkCoord(WorldId.Base, cx, cy);
            var ctx = new BiomeContext(Seed, coord, Size, layerCount: 1, tiles);
            return biome.GenerateChunk(coord, Seed, ctx);
        }

        [Test]
        public void Origin_FirstRing_IsPrimary()
        {
            var biome = new RingBiome("test.ring", "primary", "secondary", ringWidthTiles: 2);
            var chunk = Generate(biome, _tiles, 0, 0);
            // (0,0) Chebyshev = 0 → ring 0 → primary.
            // (1,1) Chebyshev = 1 → ring 0 (1/2 = 0) → primary.
            ushort primary = _tiles.GetId("primary");
            Assert.AreEqual(primary, chunk.Get(0, 0, 0));
            Assert.AreEqual(primary, chunk.Get(0, 1, 1));
        }

        [Test]
        public void SecondRing_FlipsToSecondary()
        {
            var biome = new RingBiome("test.ring", "primary", "secondary", ringWidthTiles: 2);
            var chunk = Generate(biome, _tiles, 0, 0);
            // (2,0) Chebyshev = 2 → ring 1 (2/2) → secondary.
            ushort secondary = _tiles.GetId("secondary");
            Assert.AreEqual(secondary, chunk.Get(0, 2, 0));
            Assert.AreEqual(secondary, chunk.Get(0, 0, 2));
        }

        [Test]
        public void NegativeCoords_UseAbsoluteDistance()
        {
            // (-1,0) of chunk (-1,0) → tx = -5, ty = 0 → cheb=5 → ring 2 → primary.
            var biome = new RingBiome("test.ring", "primary", "secondary", ringWidthTiles: 2);
            var chunk = Generate(biome, _tiles, -1, 0);
            // local x=3 of chunk (-1,0) → tx = -1*4+3 = -1, ty = 0 → cheb=1 → ring 0 → primary.
            ushort primary = _tiles.GetId("primary");
            Assert.AreEqual(primary, chunk.Get(0, 3, 0));
        }

        [Test]
        public void RingWidth_DefaultsTo16_WhenNonPositiveSupplied()
        {
            // 0 / negative ring widths would divide by zero — clamp must
            // protect callers. Pick a tile far inside the default 16-tile
            // ring 0 and verify it returns primary.
            var biome = new RingBiome("test.ring", "primary", "secondary", ringWidthTiles: 0);
            var chunk = Generate(biome, _tiles, 0, 0);
            // (3,3) cheb=3, ring 3/16 = 0 → primary.
            Assert.AreEqual(_tiles.GetId("primary"), chunk.Get(0, 3, 3));
        }
    }
}
