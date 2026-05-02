using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for <c>StreamingAssets/Lights/light_instances.json</c>
    /// — placement of every URP 2D point light in a world. Read by
    /// <c>WorldLightLoader</c> at boot; written by the lights importer.
    ///
    /// Same raw-JSON contract as the other instance-file repositories
    /// (buildings, spawners, particles): the production parser lives in
    /// Gameplay, so the repo abstracts file IO only and lets callers own
    /// parsing / serialisation.
    /// </summary>
    public interface ILightInstanceRepository
    {
        bool Exists(WorldId worldId);
        string ReadRawJson(WorldId worldId);
        void WriteRawJson(WorldId worldId, string json);
    }
}
