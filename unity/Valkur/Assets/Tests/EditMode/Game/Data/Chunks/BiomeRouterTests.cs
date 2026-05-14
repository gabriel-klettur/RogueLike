using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Game.Data.Chunks
{
    /// <summary>
    /// Pins the IBiomeRouter contract: every query must be deterministic
    /// (same coord always resolves to the same biome) so adjacent chunks
    /// line up at their boundary and reload produces an identical world.
    /// </summary>
    [TestFixture]
    public class BiomeRouterTests
    {
        [Test]
        public void SingleBiomeRouter_ResolvesToTheConfiguredBiome()
        {
            var biome = new UniformFillBiome("u", "grass");
            var router = new SingleBiomeRouter(biome);
            Assert.AreSame(biome, router.Resolve(new ChunkCoord(WorldId.Base, 0, 0)));
            Assert.AreSame(biome, router.Resolve(new ChunkCoord(WorldId.Base, 99, -99)));
        }

        [Test]
        public void SingleBiomeRouter_IsDeterministicAcrossCalls()
        {
            var biome = new UniformFillBiome("u", "grass");
            var router = new SingleBiomeRouter(biome);
            var coord = new ChunkCoord(WorldId.Base, 1, 2);
            Assert.AreSame(router.Resolve(coord), router.Resolve(coord),
                "Same coord must always resolve to the same biome instance.");
        }
    }
}
