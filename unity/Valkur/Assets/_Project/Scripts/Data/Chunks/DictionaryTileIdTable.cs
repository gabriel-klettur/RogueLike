using System;
using System.Collections.Generic;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Bidirectional name ↔ id table backed by a pair of dictionaries.
    /// Production worlds register their tile names once at boot
    /// (typically from a Resources scan or Addressables manifest); biomes
    /// resolve the ids during generation and only the ushort buffer
    /// travels through chunk data.
    ///
    /// Id 0 is reserved for "empty" — calling <see cref="Register"/> with
    /// the same name twice is idempotent and returns the same id.
    /// </summary>
    public sealed class DictionaryTileIdTable : ITileIdTable
    {
        private readonly Dictionary<string, ushort> _byName = new Dictionary<string, ushort>(StringComparer.Ordinal);
        private readonly List<string> _byId = new List<string> { null }; // index 0 = empty

        public int Count => _byId.Count - 1;

        /// <summary>Register a tile name and return its id. Returns the
        /// existing id if the name is already known.</summary>
        public ushort Register(string tileName)
        {
            if (string.IsNullOrEmpty(tileName)) return 0;
            if (_byName.TryGetValue(tileName, out var id)) return id;
            if (_byId.Count >= ushort.MaxValue)
                throw new InvalidOperationException(
                    "DictionaryTileIdTable cannot hold more than 65535 distinct tiles.");
            id = (ushort)_byId.Count;
            _byName[tileName] = id;
            _byId.Add(tileName);
            return id;
        }

        public ushort GetId(string tileName)
            => string.IsNullOrEmpty(tileName) ? (ushort)0
             : _byName.TryGetValue(tileName, out var id) ? id : (ushort)0;

        public string GetName(ushort tileId)
            => tileId == 0 || tileId >= _byId.Count ? null : _byId[tileId];
    }
}
