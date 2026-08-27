using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>Production <see cref="IEntityInstanceRepository"/> backed by
    /// <c>StreamingAssets/Entities/entities_instances.json</c>.</summary>
    public sealed class JsonFileEntityInstanceRepository
        : WorldStreamingFileRepositoryBase, IEntityInstanceRepository
    {
        protected override string Subdir   => "Entities";
        protected override string FileName => "entities_instances.json";

        // Placed entity instances are authored per map slot: the F11 Map Editor
        // creates independent maps and each must own its own file on disk. Same
        // opt-in Buildings, Lights, Particles and Spawners already use.
        protected override bool IsMapSlotAware => true;

        public JsonFileEntityInstanceRepository() : this(null) { }

        public JsonFileEntityInstanceRepository(string streamingRootOverride)
            : base(streamingRootOverride) { }

        public bool   Exists(WorldId worldId)                    => ExistsFile(worldId);
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);
    }
}
