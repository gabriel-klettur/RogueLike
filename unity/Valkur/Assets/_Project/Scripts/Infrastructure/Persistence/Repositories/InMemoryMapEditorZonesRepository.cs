using System.Collections.Generic;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// In-memory <see cref="IMapEditorZonesRepository"/> for tests. Stores
    /// the "primary" and "sidecar" payloads independently so test fixtures
    /// can simulate the corrupt-primary recovery path without disk.
    /// </summary>
    public sealed class InMemoryMapEditorZonesRepository : IMapEditorZonesRepository
    {
        private readonly Dictionary<WorldId, string> _primary = new Dictionary<WorldId, string>();
        private readonly Dictionary<WorldId, string> _sidecar = new Dictionary<WorldId, string>();

        public bool Exists(WorldId worldId)
            => _primary.ContainsKey(worldId) || _sidecar.ContainsKey(worldId);

        public string ReadWithSidecarFallback(WorldId worldId, out bool recoveredFromSidecar)
        {
            recoveredFromSidecar = false;
            if (_primary.TryGetValue(worldId, out var primary)) return primary;
            if (_sidecar.TryGetValue(worldId, out var sidecar))
            {
                recoveredFromSidecar = true;
                return sidecar;
            }
            return null;
        }

        public void WriteAtomic(WorldId worldId, string json)
        {
            // Mirror File.Replace semantics: previous primary becomes the
            // new sidecar, the new content becomes the primary.
            if (_primary.TryGetValue(worldId, out var prev))
                _sidecar[worldId] = prev;
            _primary[worldId] = json ?? string.Empty;
        }

        // Test helpers — bypass the atomic semantics to simulate disk states.
        public void SeedPrimary(WorldId worldId, string json) => _primary[worldId] = json ?? string.Empty;
        public void SeedSidecar(WorldId worldId, string json) => _sidecar[worldId] = json ?? string.Empty;
        public void DeletePrimary(WorldId worldId)            => _primary.Remove(worldId);
        public void DeleteSidecar(WorldId worldId)            => _sidecar.Remove(worldId);
    }
}
