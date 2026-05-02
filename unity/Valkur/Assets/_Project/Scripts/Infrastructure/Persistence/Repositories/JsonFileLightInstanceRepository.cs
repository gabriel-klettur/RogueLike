using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>Production <see cref="ILightInstanceRepository"/> backed by
    /// <c>StreamingAssets/Lights/light_instances.json</c>.</summary>
    public sealed class JsonFileLightInstanceRepository
        : WorldStreamingFileRepositoryBase, ILightInstanceRepository
    {
        protected override string Subdir   => "Lights";
        protected override string FileName => "light_instances.json";

        public JsonFileLightInstanceRepository() : this(null) { }

        public JsonFileLightInstanceRepository(string streamingRootOverride)
            : base(streamingRootOverride) { }

        public bool   Exists(WorldId worldId)                    => ExistsFile(worldId);
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);
    }
}
