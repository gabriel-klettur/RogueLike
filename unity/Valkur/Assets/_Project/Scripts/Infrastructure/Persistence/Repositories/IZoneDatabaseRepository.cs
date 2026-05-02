using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for <c>StreamingAssets/Maps/zones_database.json</c>
    /// — the master manifest declaring every zone in a world (name, offset,
    /// overlay file, collision file, plus global zone width/height and
    /// world origin).
    ///
    /// Read by <c>ZoneDatabaseLoader</c> at boot. Written by the offline
    /// Python -> Unity importer; the runtime currently does not modify it.
    /// Phase 1 (multi-world) introduces additional databases per world; the
    /// repository is per-world from day one so adding worlds doesn't churn
    /// the loader.
    ///
    /// Same raw-JSON shape as the other instance repositories — keeps
    /// MiniJsonRuntime out of <c>Valkur.Infrastructure</c>.
    /// </summary>
    public interface IZoneDatabaseRepository
    {
        bool Exists(WorldId worldId);
        string ReadRawJson(WorldId worldId);
        void WriteRawJson(WorldId worldId, string json);
    }
}
