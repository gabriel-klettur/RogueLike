using System.Collections.Generic;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>In-memory <see cref="IEntityInstanceRepository"/> for tests.</summary>
    public sealed class InMemoryEntityInstanceRepository : IEntityInstanceRepository
    {
        private readonly Dictionary<WorldId, string> _store = new Dictionary<WorldId, string>();

        public bool Exists(WorldId worldId) => _store.ContainsKey(worldId);

        public string ReadRawJson(WorldId worldId)
        {
            _store.TryGetValue(worldId, out var json);
            return json;
        }

        public void WriteRawJson(WorldId worldId, string json)
        {
            _store[worldId] = json ?? string.Empty;
        }
    }
}
