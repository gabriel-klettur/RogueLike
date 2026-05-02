using System;
using System.Collections.Generic;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// In-memory <see cref="ITileOverrideRepository"/> backed by a dictionary.
    /// Tests use this to avoid disk I/O entirely; the production flow is
    /// exercised through <see cref="JsonFileTileOverrideRepository"/>.
    ///
    /// Keys are case-insensitive on the zone name to mirror the production
    /// repository (filesystem on Windows is case-insensitive by default).
    /// </summary>
    public sealed class InMemoryTileOverrideRepository : ITileOverrideRepository
    {
        // Keyed by (WorldId, zoneName). WorldId equality is by Guid only; the
        // outer dictionary lets us check Exists in O(1) per world without
        // scanning every entry.
        private readonly Dictionary<WorldId, Dictionary<string, string>> _store
            = new Dictionary<WorldId, Dictionary<string, string>>();

        private Dictionary<string, string> GetOrCreateScope(WorldId worldId)
        {
            if (!_store.TryGetValue(worldId, out var scope))
            {
                scope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _store[worldId] = scope;
            }
            return scope;
        }

        public bool Exists(WorldId worldId, string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return false;
            return _store.TryGetValue(worldId, out var scope) && scope.ContainsKey(zoneName);
        }

        public string Read(WorldId worldId, string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return null;
            if (!_store.TryGetValue(worldId, out var scope)) return null;
            scope.TryGetValue(zoneName, out var json);
            return json;
        }

        public void Write(WorldId worldId, string zoneName, string overlayJson)
        {
            if (string.IsNullOrEmpty(zoneName))
                throw new ArgumentException("zoneName must be set", nameof(zoneName));
            GetOrCreateScope(worldId)[zoneName] = overlayJson ?? string.Empty;
        }

        public bool Delete(WorldId worldId, string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return false;
            return _store.TryGetValue(worldId, out var scope) && scope.Remove(zoneName);
        }

        public bool Rename(WorldId worldId, string fromZoneName, string toZoneName)
        {
            if (string.IsNullOrEmpty(fromZoneName) || string.IsNullOrEmpty(toZoneName)) return false;
            if (string.Equals(fromZoneName, toZoneName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!_store.TryGetValue(worldId, out var scope)) return true;       // nothing to move
            if (!scope.TryGetValue(fromZoneName, out var existing)) return true; // nothing to move
            if (scope.ContainsKey(toZoneName)) return false;                     // destination occupied
            scope.Remove(fromZoneName);
            scope[toZoneName] = existing;
            return true;
        }

        public IEnumerable<string> ListAvailableZones(WorldId worldId)
        {
            if (!_store.TryGetValue(worldId, out var scope)) return Array.Empty<string>();
            return scope.Keys;
        }
    }
}
