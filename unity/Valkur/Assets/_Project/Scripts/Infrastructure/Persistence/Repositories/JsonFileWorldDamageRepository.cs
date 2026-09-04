using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Production <see cref="IWorldDamageRepository"/>, backed by one JSON file inside the
    /// run's own save folder (<c>persistentDataPath/Saves/&lt;runId&gt;/WorldDamage/world_damage.json</c>).
    ///
    /// <para><see cref="WorldStreamingFileRepositoryBase.IsMapSlotAware"/> stays FALSE, which
    /// looks wrong for content that is authored per map slot and is not. The run root is
    /// PINNED — the constructor is handed <c>Saves/&lt;runId&gt;</c> — and slot routing is
    /// skipped for a pinned root by design, so turning the flag on would change nothing here
    /// and would silently start moving a run's damage into <c>Maps/&lt;slot&gt;/</c> the day
    /// somebody constructed one without a root. The slot a record belongs to is carried IN
    /// the record instead, which also lets one run hold damage for several slots at once —
    /// something a directory split could not express without re-reading every slot's file to
    /// answer a single lookup.</para>
    /// </summary>
    public sealed class JsonFileWorldDamageRepository
        : WorldStreamingFileRepositoryBase, IWorldDamageRepository
    {
        private const string DEFAULT_SUBDIR    = "WorldDamage";
        private const string DEFAULT_FILE_NAME = "world_damage.json";

        private readonly string _subdir;
        private readonly string _fileName;

        protected override string Subdir   => _subdir;
        protected override string FileName => _fileName;

        public JsonFileWorldDamageRepository(string rootOverride)
            : this(rootOverride, DEFAULT_SUBDIR, DEFAULT_FILE_NAME) { }

        public JsonFileWorldDamageRepository(string rootOverride, string subdir, string fileName)
            : base(rootOverride)
        {
            _subdir   = string.IsNullOrEmpty(subdir)   ? DEFAULT_SUBDIR    : subdir;
            _fileName = string.IsNullOrEmpty(fileName) ? DEFAULT_FILE_NAME : fileName;
        }

        public bool   Exists(WorldId worldId)                    => ExistsFile(worldId);
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);
    }
}
