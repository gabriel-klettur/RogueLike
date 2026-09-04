using System.Collections.Generic;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// <see cref="IWorldDamageRepository"/> backed by a dictionary. For tests and for any
    /// sandbox scene that wants the damage layer's behaviour without a file on disk.
    ///
    /// <para>It counts its writes, because most of what is worth asserting about this layer is
    /// not what it stored but WHETHER it stored — a save that runs when it should not is the
    /// failure mode this project has already paid for, and it is invisible in the content.</para>
    /// </summary>
    public sealed class InMemoryWorldDamageRepository : IWorldDamageRepository
    {
        private readonly Dictionary<string, string> _byWorld = new Dictionary<string, string>();

        /// <summary>How many times <see cref="WriteRawJson"/> has been called.</summary>
        public int WriteCount { get; private set; }

        public bool Exists(WorldId worldId) => _byWorld.ContainsKey(KeyOf(worldId));

        public string ReadRawJson(WorldId worldId)
            => _byWorld.TryGetValue(KeyOf(worldId), out var json) ? json : null;

        public void WriteRawJson(WorldId worldId, string json)
        {
            _byWorld[KeyOf(worldId)] = json;
            WriteCount++;
        }

        /// <summary>Seed content as though a previous run had written it.</summary>
        public void Seed(WorldId worldId, string json) => _byWorld[KeyOf(worldId)] = json;

        private static string KeyOf(WorldId worldId) => worldId.Slug ?? "";
    }
}
