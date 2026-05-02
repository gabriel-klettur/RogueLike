using System;
using System.Threading;
using System.Threading.Tasks;
using Valkur.Core.Coordinates;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// The chunk provider gameplay actually consumes. Combines:
    ///   1. A procedural baseline from an <see cref="IBiome"/> (resolved
    ///      per-coord through an <see cref="IBiomeRouter"/>).
    ///   2. A persisted <see cref="ChunkDelta"/> read from an
    ///      <see cref="IChunkDeltaSource"/> (a thin abstraction over
    ///      <c>IChunkDeltaRepository</c> so this assembly does not have to
    ///      reference Infrastructure).
    ///
    /// Two coords produce the same <see cref="ChunkData"/> bit-for-bit
    /// when (worldSeed, biome.Version, persisted delta) match — the
    /// canonical Phase-4 client-prediction guarantee.
    ///
    /// Out of scope: this provider does NOT mutate the delta when the
    /// player edits a chunk. That belongs to a future tile-edit pipeline
    /// that re-diffs against the baseline and persists. Keeping write out
    /// of the read provider matches CQRS practice and prevents accidental
    /// double-writes if both client prediction and server reconciliation
    /// run through it.
    /// </summary>
    public sealed class DiffOverlayChunkProvider : IChunkProvider
    {
        private readonly IBiomeRouter _router;
        private readonly IChunkDeltaSource _deltas;
        private readonly long _worldSeed;
        private readonly int _chunkSize;
        private readonly int _layerCount;
        private readonly ITileIdTable _tiles;

        public DiffOverlayChunkProvider(IBiomeRouter router,
                                        IChunkDeltaSource deltas,
                                        long worldSeed,
                                        int chunkSize,
                                        int layerCount,
                                        ITileIdTable tiles)
        {
            _router    = router ?? throw new ArgumentNullException(nameof(router));
            _deltas    = deltas ?? new EmptyDeltaSource();
            _worldSeed = worldSeed;
            _chunkSize = chunkSize > 0 ? chunkSize : ChunkData.DefaultChunkSize;
            _layerCount = layerCount > 0 ? layerCount : 1;
            _tiles     = tiles ?? new EmptyTileIdTable();
        }

        public bool Has(ChunkCoord coord) => _router.Resolve(coord) != null;

        public ChunkData Get(ChunkCoord coord)
        {
            var biome = _router.Resolve(coord)
                ?? throw new InvalidOperationException(
                    $"DiffOverlayChunkProvider: router returned null for {coord}.");

            // 1. Regenerate baseline deterministically.
            var ctx = new BiomeContext(_worldSeed, coord, _chunkSize, _layerCount, _tiles);
            var data = biome.GenerateChunk(coord, _worldSeed, ctx);

            // 2. Apply any persisted player edits. A null/empty delta is a
            //    no-op, so virgin chunks pay zero overhead.
            var delta = _deltas.Read(coord);
            if (delta != null && !delta.IsEmpty)
            {
                // Stale-baseline guard: if the delta references a different
                // biome.Version, the baseline shape may have shifted under
                // it. Phase 2 keeps the conservative behaviour — apply
                // anyway and warn — so a player's edits are not silently
                // erased the moment a biome rule changes. Phase 3 migration
                // tool can rebake offline.
                if (delta.BiomeVersion != biome.Version &&
                    string.Equals(delta.BiomeId, biome.Id, StringComparison.Ordinal))
                {
                    UnityEngine.Debug.LogWarning(
                        $"[DiffOverlayChunkProvider] Stale delta at {coord}: " +
                        $"biome '{biome.Id}' is version {biome.Version}, " +
                        $"delta is version {delta.BiomeVersion}. Applying anyway.");
                }
                delta.ApplyTo(data, msg => UnityEngine.Debug.LogWarning("[DiffOverlay] " + msg));
            }
            return data;
        }

        public Task<ChunkData> GetAsync(ChunkCoord coord, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Get(coord));
        }
    }

    /// <summary>Read-only abstraction over the chunk-delta store. Keeps
    /// <see cref="DiffOverlayChunkProvider"/> in <c>Valkur.Data</c>
    /// without pulling in <c>Valkur.Infrastructure</c>; the
    /// <c>IChunkDeltaRepository</c> from the persistence layer adapts
    /// trivially through a thin wrapper.</summary>
    public interface IChunkDeltaSource
    {
        ChunkDelta Read(ChunkCoord coord);
    }

    /// <summary>Default empty source — every chunk reports no edits, the
    /// provider returns the pure baseline. Useful for tests of the
    /// procedural-only flow.</summary>
    public sealed class EmptyDeltaSource : IChunkDeltaSource
    {
        public ChunkDelta Read(ChunkCoord coord) => null;
    }
}
