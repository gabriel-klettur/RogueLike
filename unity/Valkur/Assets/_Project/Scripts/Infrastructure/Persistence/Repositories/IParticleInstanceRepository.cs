using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for <c>StreamingAssets/Particles/particles_instances.json</c>
    /// — placement of every particle emitter in a world. Read by
    /// <c>ParticleInstancesLoader</c>; written by the particles runtime
    /// editor (<c>SaveInstancesToJson</c>) and the offline importer.
    /// </summary>
    public interface IParticleInstanceRepository
    {
        bool Exists(WorldId worldId);
        string ReadRawJson(WorldId worldId);
        void WriteRawJson(WorldId worldId, string json);
    }
}
