using System.Threading;
using System.Threading.Tasks;
using Valkur.Core.Coordinates;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Phase-2 contract: hand a coordinate, get back the chunk for that
    /// position. Implementations hide whether the chunk is generated
    /// procedurally, loaded from a hand-crafted asset, or pulled from
    /// the network.
    ///
    /// Sync entry point coexists with an async one because Phase 2 starts
    /// fully synchronous (proof-of-concept). Phase 2.5 adds the streamer
    /// that actually awaits and yields between batches; the contract
    /// already exposes the async surface so code written today survives
    /// that switch without reshape.
    /// </summary>
    public interface IChunkProvider
    {
        /// <summary>True iff this provider can produce a chunk for the
        /// given coordinate. Procedural providers usually return true
        /// for every coordinate; fixed providers only for chunks that
        /// have authored data.</summary>
        bool Has(ChunkCoord coord);

        /// <summary>Synchronous load. Throws if the provider cannot
        /// produce the chunk and has no fallback.</summary>
        ChunkData Get(ChunkCoord coord);

        /// <summary>Async load surface. Phase 2 implementations may
        /// complete inline; Phase 2.5 streaming will yield while reading
        /// from disk or networking.</summary>
        Task<ChunkData> GetAsync(ChunkCoord coord, CancellationToken ct = default);
    }
}
