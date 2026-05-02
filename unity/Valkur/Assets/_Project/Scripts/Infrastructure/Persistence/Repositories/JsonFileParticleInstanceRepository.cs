using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>Production <see cref="IParticleInstanceRepository"/> backed by
    /// <c>StreamingAssets/Particles/particles_instances.json</c>.</summary>
    public sealed class JsonFileParticleInstanceRepository
        : WorldStreamingFileRepositoryBase, IParticleInstanceRepository
    {
        protected override string Subdir   => "Particles";
        protected override string FileName => "particles_instances.json";

        public JsonFileParticleInstanceRepository() : this(null) { }

        public JsonFileParticleInstanceRepository(string streamingRootOverride)
            : base(streamingRootOverride) { }

        public bool   Exists(WorldId worldId)                    => ExistsFile(worldId);
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);
    }
}
