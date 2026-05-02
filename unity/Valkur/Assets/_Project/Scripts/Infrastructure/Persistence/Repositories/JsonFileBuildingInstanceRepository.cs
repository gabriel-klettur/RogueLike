using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Production <see cref="IBuildingInstanceRepository"/> backed by
    /// <c>StreamingAssets/Buildings/buildings_instances.json</c>. Path
    /// resolution and atomic write live in
    /// <see cref="WorldStreamingFileRepositoryBase"/>.
    /// </summary>
    public sealed class JsonFileBuildingInstanceRepository
        : WorldStreamingFileRepositoryBase, IBuildingInstanceRepository
    {
        protected override string Subdir   => "Buildings";
        protected override string FileName => "buildings_instances.json";

        public JsonFileBuildingInstanceRepository() : this(null) { }

        public JsonFileBuildingInstanceRepository(string streamingRootOverride)
            : base(streamingRootOverride) { }

        public bool   Exists(WorldId worldId)                    => ExistsFile(worldId);
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);
    }
}
