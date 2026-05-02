using System;
using System.Threading;
using System.Threading.Tasks;
using Valkur.Core.Coordinates;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Phase-2 procedural <see cref="IChunkProvider"/>: every Get()
    /// re-runs the supplied <see cref="IBiome"/> against a fresh
    /// <see cref="BiomeContext"/>. Two consecutive Gets for the same
    /// coordinate produce the same <see cref="ChunkData"/> bit-for-bit
    /// because the biome is deterministic.
    ///
    /// Phase 2 keeps a single biome per provider; Phase 2.5 introduces
    /// an <c>IBiomeRouter</c> that maps coords to biomes and replaces
    /// this provider with a routed variant. The contract surface stays
    /// identical so the streamer doesn't have to know which provider
    /// shape is below it.
    /// </summary>
    public sealed class ProceduralChunkProvider : IChunkProvider
    {
        private readonly IBiome _biome;
        private readonly long _worldSeed;
        private readonly int _chunkSize;
        private readonly int _layerCount;
        private readonly ITileIdTable _tiles;

        public ProceduralChunkProvider(IBiome biome,
                                       long worldSeed,
                                       int chunkSize,
                                       int layerCount,
                                       ITileIdTable tiles)
        {
            _biome      = biome ?? throw new ArgumentNullException(nameof(biome));
            _worldSeed  = worldSeed;
            _chunkSize  = chunkSize > 0 ? chunkSize : ChunkData.DefaultChunkSize;
            _layerCount = layerCount > 0 ? layerCount : 1;
            _tiles      = tiles ?? new EmptyTileIdTable();
        }

        public bool Has(ChunkCoord coord) => true;

        public ChunkData Get(ChunkCoord coord)
        {
            var ctx = new BiomeContext(_worldSeed, coord, _chunkSize, _layerCount, _tiles);
            return _biome.GenerateChunk(coord, _worldSeed, ctx);
        }

        public Task<ChunkData> GetAsync(ChunkCoord coord, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Get(coord));
        }
    }
}
