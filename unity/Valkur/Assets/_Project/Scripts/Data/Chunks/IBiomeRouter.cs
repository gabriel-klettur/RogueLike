using Valkur.Core.Coordinates;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Decides which <see cref="IBiome"/> generates the chunk at a given
    /// coordinate. Phase 2 ships a single trivial router
    /// (<see cref="SingleBiomeRouter"/>); Phase 2.5 will introduce
    /// noise/Voronoi-driven routers that pick between multiple biomes.
    ///
    /// Routers MUST be deterministic: the same coord must always resolve
    /// to the same biome (otherwise neighbouring chunks would not line
    /// up at their shared boundary, and re-loads would shuffle the world).
    /// </summary>
    public interface IBiomeRouter
    {
        IBiome Resolve(ChunkCoord coord);
    }

    /// <summary>Default trivial router: every chunk routes to the same
    /// biome. Useful for worlds that contain a single biome (test
    /// dimensions, dungeon-only worlds, etc.) and as the fallback when
    /// a richer router has no opinion about a given coord.</summary>
    public sealed class SingleBiomeRouter : IBiomeRouter
    {
        private readonly IBiome _biome;
        public SingleBiomeRouter(IBiome biome) { _biome = biome; }
        public IBiome Resolve(ChunkCoord coord) => _biome;
    }
}
