using System.Collections.Generic;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>In-memory <see cref="IChunkDeltaRepository"/> for tests.
    /// Stores deep copies on Write so callers cannot accidentally mutate
    /// the persisted version after the fact (matches the JSON backend's
    /// implicit serialise-then-store isolation).</summary>
    public sealed class InMemoryChunkDeltaRepository : IChunkDeltaRepository
    {
        // Keyed by (WorldId, ChunkCoord). Inner dict scoped per world so
        // ListEdited can return only that world's chunks without scanning.
        private readonly Dictionary<WorldId, Dictionary<ChunkCoord, ChunkDelta>> _store
            = new Dictionary<WorldId, Dictionary<ChunkCoord, ChunkDelta>>();

        private Dictionary<ChunkCoord, ChunkDelta> Scope(WorldId worldId)
        {
            if (!_store.TryGetValue(worldId, out var s))
            {
                s = new Dictionary<ChunkCoord, ChunkDelta>();
                _store[worldId] = s;
            }
            return s;
        }

        public bool Exists(WorldId worldId, ChunkCoord coord)
            => _store.TryGetValue(worldId, out var s) && s.ContainsKey(coord);

        public ChunkDelta Read(WorldId worldId, ChunkCoord coord)
        {
            if (!_store.TryGetValue(worldId, out var s)) return null;
            s.TryGetValue(coord, out var d);
            return d == null ? null : Clone(d);
        }

        public void Write(WorldId worldId, ChunkCoord coord, ChunkDelta delta)
        {
            // Empty deltas are not persisted — same contract as the file
            // backend, so the InMemory fixture can stand in for it 1:1.
            if (delta == null || delta.IsEmpty)
            {
                Delete(worldId, coord);
                return;
            }
            Scope(worldId)[coord] = Clone(delta);
        }

        public bool Delete(WorldId worldId, ChunkCoord coord)
            => _store.TryGetValue(worldId, out var s) && s.Remove(coord);

        public IEnumerable<ChunkCoord> ListEdited(WorldId worldId)
        {
            if (!_store.TryGetValue(worldId, out var s)) yield break;
            foreach (var k in s.Keys) yield return k;
        }

        private static ChunkDelta Clone(ChunkDelta src)
        {
            var c = new ChunkDelta(src.Coord, src.BiomeId, src.BiomeVersion);
            if (src.Tiles != null)
                for (int i = 0; i < src.Tiles.Count; i++) c.Tiles.Add(src.Tiles[i]);
            return c;
        }
    }
}
