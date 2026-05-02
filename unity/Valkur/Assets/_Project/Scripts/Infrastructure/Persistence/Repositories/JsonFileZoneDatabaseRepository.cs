using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>Production <see cref="IZoneDatabaseRepository"/> backed by
    /// <c>StreamingAssets/Maps/zones_database.json</c>.</summary>
    public sealed class JsonFileZoneDatabaseRepository
        : WorldStreamingFileRepositoryBase, IZoneDatabaseRepository
    {
        protected override string Subdir   => "Maps";
        protected override string FileName => "zones_database.json";

        public JsonFileZoneDatabaseRepository() : this(null) { }

        public JsonFileZoneDatabaseRepository(string streamingRootOverride)
            : base(streamingRootOverride) { }

        public bool   Exists(WorldId worldId)                    => ExistsFile(worldId);
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);
    }
}
