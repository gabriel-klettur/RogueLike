using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for <c>StreamingAssets/Spawners/spawners_instances.json</c>
    /// — placement of every monster spawner in a world. Read by
    /// <c>SpawnerInstanceLoader</c> at boot.
    /// </summary>
    public interface ISpawnerInstanceRepository
    {
        bool Exists(WorldId worldId);
        string ReadRawJson(WorldId worldId);
        void WriteRawJson(WorldId worldId, string json);
    }
}
