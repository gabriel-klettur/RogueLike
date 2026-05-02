using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>Production <see cref="ISpawnerInstanceRepository"/> backed by
    /// <c>StreamingAssets/Spawners/spawners_instances.json</c>.</summary>
    public sealed class JsonFileSpawnerInstanceRepository
        : WorldStreamingFileRepositoryBase, ISpawnerInstanceRepository
    {
        protected override string Subdir   => "Spawners";
        protected override string FileName => "spawners_instances.json";

        public JsonFileSpawnerInstanceRepository() : this(null) { }

        public JsonFileSpawnerInstanceRepository(string streamingRootOverride)
            : base(streamingRootOverride) { }

        public bool   Exists(WorldId worldId)                    => ExistsFile(worldId);
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);
    }
}
