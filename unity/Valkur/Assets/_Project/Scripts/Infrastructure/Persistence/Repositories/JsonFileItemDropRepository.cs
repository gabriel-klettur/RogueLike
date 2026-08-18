using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Production <see cref="IItemDropRepository"/> backed by
    /// <c>StreamingAssets/Items/item_drops.json</c>. Subdir is configurable so
    /// the same class can serve both the authoring store (default subdir
    /// <c>"Items"</c>) and a run-scoped store (subdir like <c>"Saves/{runId}"</c>)
    /// without forking the implementation.
    ///
    /// Path resolution and atomic writes inherit from
    /// <see cref="WorldStreamingFileRepositoryBase"/>.
    /// </summary>
    public sealed class JsonFileItemDropRepository
        : WorldStreamingFileRepositoryBase, IItemDropRepository
    {
        private const string DEFAULT_SUBDIR    = "Items";
        private const string DEFAULT_FILE_NAME = "item_drops.json";

        private readonly string _subdir;
        private readonly string _fileName;

        protected override string Subdir   => _subdir;
        protected override string FileName => _fileName;

        // Authoring drops (StreamingAssets/Items) belong to the map being
        // edited, so they follow the active slot. The run-scoped store passes a
        // pinned root (Saves/<runId>) and the base class skips slot routing for
        // it, keeping a player's in-progress run drops where the save system
        // expects them.
        protected override bool IsMapSlotAware => true;

        public JsonFileItemDropRepository()
            : this(null, DEFAULT_SUBDIR, DEFAULT_FILE_NAME) { }

        public JsonFileItemDropRepository(string streamingRootOverride)
            : this(streamingRootOverride, DEFAULT_SUBDIR, DEFAULT_FILE_NAME) { }

        public JsonFileItemDropRepository(
            string streamingRootOverride,
            string subdir,
            string fileName)
            : base(streamingRootOverride)
        {
            _subdir   = string.IsNullOrEmpty(subdir)   ? DEFAULT_SUBDIR    : subdir;
            _fileName = string.IsNullOrEmpty(fileName) ? DEFAULT_FILE_NAME : fileName;
        }

        public bool   Exists(WorldId worldId)                    => ExistsFile(worldId);
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);
    }
}
