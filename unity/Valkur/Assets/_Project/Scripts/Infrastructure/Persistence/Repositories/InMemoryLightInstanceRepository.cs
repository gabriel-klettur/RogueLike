using System.Collections.Generic;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>In-memory <see cref="ILightInstanceRepository"/> for tests.</summary>
    public sealed class InMemoryLightInstanceRepository : ILightInstanceRepository
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
