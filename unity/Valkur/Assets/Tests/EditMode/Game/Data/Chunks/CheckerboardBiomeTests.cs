using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Game.Data.Chunks
{
    /// <summary>
    /// Pins the deterministic positional rule of <see cref="CheckerboardBiome"/>:
    /// (Tx + Ty) parity selects between primary and secondary tile, with
    /// no seam between adjacent chunks. The biome carries no state, so
    /// "deterministic" here also means same coord → same byte every time.
    /// </summary>
    [TestFixture]
    public class CheckerboardBiomeTests
    {
        private const int Size = 4;
        private const long Seed = 0L; // checkerboard ignores seed; pin to 0 for clarity

        private DictionaryTileIdTable _tiles;
        private CheckerboardBiome _biome;

        [SetUp]
        public void SetUp()
        {
            _tiles = new DictionaryTileIdTable();
            _tiles.Register("primary");
            _tiles.Register("secondary");
            _biome = new CheckerboardBiome("test.checker", "primary", "secondary");
        }

        private ChunkData Generate(int cx, int cy)
        {
            var coord = new ChunkCoord(WorldId.Base, cx, cy);
            var ctx = new BiomeContext(Seed, coord, Size, layerCount: 1, _tiles);
            return _biome.GenerateChunk(coord, Seed, ctx);
        }

        [Test]
        public void Origin00_PaintsPrimaryAtParityEvenCells()
        {
            var chunk = Generate(0, 0);
            ushort primary   = _tiles.GetId("primary");
            ushort secondary = _tiles.GetId("secondary");

            // (0,0) → tx+ty = 0 even → primary.
            Assert.AreEqual(primary, chunk.Get(0, 0, 0));
            // (1,0) → 1 odd → secondary.
            Assert.AreEqual(secondary, chunk.Get(0, 1, 0));
            // (0,1) → 1 odd → secondary.
            Assert.AreEqual(secondary, chunk.Get(0, 0, 1));
            // (1,1) → 2 even → primary.
            Assert.AreEqual(primary, chunk.Get(0, 1, 1));
        }

        [Test]
        public void AdjacentChunks_ShareSeam()
        {
            // Right edge of chunk (0,0) at local x=3 corresponds to absolute
            // tx=3. Left edge of chunk (1,0) at local x=0 corresponds to
            // absolute tx=4. Both at ty=0. Parities: 3 odd, 4 even — so the
            // pattern alternates correctly across the chunk boundary.
            var left  = Generate(0, 0);
            var right = Generate(1, 0);
            ushort primary   = _tiles.GetId("primary");
            ushort secondary = _tiles.GetId("secondary");

            Assert.AreEqual(secondary, left.Get(0, 3, 0),
                "Right edge of (0,0) at tx=3 must be secondary (odd parity).");
            Assert.AreEqual(primary, right.Get(0, 0, 0),
                "Left edge of (1,0) at tx=4 must be primary (even parity).");
        }

        [Test]
        public void NegativeCoords_PreserveParityCorrectly()
        {
            // tx = -1, ty = 0 → -1+0 = -1 → low bit of two's-complement is 1
            // → secondary. Parity must work for negative coords too — the
            // bitwise XOR trick handles this correctly without modulo wrap.
            var chunk = Generate(-1, 0);
            ushort secondary = _tiles.GetId("secondary");

            // Local x=3 of chunk (-1,0) → tx = -1*4 + 3 = -1.
            Assert.AreEqual(secondary, chunk.Get(0, 3, 0),
                "Negative-coord chunks must follow the same parity rule.");
        }

        [Test]
        public void IsHandcrafted_False_AndVersion_One()
        {
            Assert.IsFalse(_biome.IsHandcrafted);
            Assert.AreEqual(1, _biome.Version);
            Assert.AreEqual("test.checker", _biome.Id);
        }
    }
}
