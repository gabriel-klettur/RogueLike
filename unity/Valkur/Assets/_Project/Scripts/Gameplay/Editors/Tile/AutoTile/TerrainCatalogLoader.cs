using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Cached runtime accessor to the project's <see cref="TerrainCatalog"/>.
    /// The asset lives at <c>Resources/TerrainCatalog.asset</c> so it's available
    /// in standalone builds; the cache survives tool switches but resets on
    /// domain reload (irrelevant under Domain-Reload-OFF — first call after a
    /// scene reload re-populates).
    /// </summary>
    public static class TerrainCatalogLoader
    {
        private const string ResourcePath = "TerrainCatalog";
        private static TerrainCatalog _cached;

        public static TerrainCatalog Load()
        {
            if (_cached != null) return _cached;
            _cached = Resources.Load<TerrainCatalog>(ResourcePath);
            return _cached;
        }

        /// <summary>Tests + post-import hooks call this so the next <see cref="Load"/> hits disk again.</summary>
        public static void InvalidateCache() => _cached = null;
    }
}
