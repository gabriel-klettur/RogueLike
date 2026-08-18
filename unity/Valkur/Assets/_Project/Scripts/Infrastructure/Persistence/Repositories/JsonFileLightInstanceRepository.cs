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

        // Placed light instances are authored per map slot: the F11 Map Editor
        // creates independent maps and each must own its own file on disk.
        protected override bool IsMapSlotAware => true;

        public JsonFileLightInstanceRepository() : this(null) { }

        public JsonFileLightInstanceRepository(string streamingRootOverride)
            : base(streamingRootOverride) { }

        public bool   Exists(WorldId worldId)                    => ExistsFile(worldId);
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);
    }
}
