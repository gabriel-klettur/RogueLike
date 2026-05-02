using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Valkur.Core.Coordinates;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// In-memory <see cref="IChunkProvider"/>. Tests preload chunks via
    /// <see cref="Set"/>; production-side fixtures use it as a stand-in
    /// for the real procedural provider when they only care about the
    /// streamer / consumer behaviour.
    /// </summary>
    public sealed class InMemoryChunkProvider : IChunkProvider
    {
        private readonly Dictionary<ChunkCoord, ChunkData> _store
            = new Dictionary<ChunkCoord, ChunkData>();

        public void Set(ChunkData data)
        {
            if (data == null) return;
            _store[data.Coord] = data;
        }

        public bool Has(ChunkCoord coord) => _store.ContainsKey(coord);

        public ChunkData Get(ChunkCoord coord)
        {
            if (_store.TryGetValue(coord, out var d)) return d;
            throw new System.InvalidOperationException(
                $"InMemoryChunkProvider has no chunk at {coord}.");
        }

        public Task<ChunkData> GetAsync(ChunkCoord coord, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Get(coord));
        }
    }
}
