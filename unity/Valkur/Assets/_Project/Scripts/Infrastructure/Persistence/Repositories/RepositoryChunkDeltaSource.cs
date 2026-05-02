using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Adapts an <see cref="IChunkDeltaRepository"/> (Infrastructure layer)
    /// to an <see cref="IChunkDeltaSource"/> (Data layer) so the
    /// <see cref="DiffOverlayChunkProvider"/> can read persisted deltas
    /// without Data taking a dependency on Infrastructure.
    ///
    /// Scoped to a single <see cref="WorldId"/> at construction so the
    /// provider does not have to thread the world through every Read
    /// call — one source per active world matches the per-world
    /// IWorldContext model from Phase 1.
    /// </summary>
    public sealed class RepositoryChunkDeltaSource : IChunkDeltaSource
    {
        private readonly IChunkDeltaRepository _repository;
        private readonly WorldId _worldId;

        public RepositoryChunkDeltaSource(IChunkDeltaRepository repository, WorldId worldId)
        {
            _repository = repository ?? throw new System.ArgumentNullException(nameof(repository));
            _worldId    = worldId;
        }

        public ChunkDelta Read(ChunkCoord coord) => _repository.Read(_worldId, coord);
    }
}
