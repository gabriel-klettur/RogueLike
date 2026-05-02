namespace Valkur.Infrastructure.Migrations
{
    /// <summary>
    /// Contract every persistence DTO must implement to participate in a
    /// <see cref="MigrationChain{T}"/>. Holding the version on the document
    /// itself (instead of derived from filename or external metadata) makes
    /// migration self-contained and tolerant to file renames.
    ///
    /// Phase 0 introduces this interface; existing types like
    /// <c>GameSaveData</c> can adopt it without breaking — the existing
    /// <c>schemaVersion</c> field is exactly what the implementation returns.
    /// </summary>
    public interface IVersioned
    {
        /// <summary>Schema version string, e.g. "1.0", "1.4". Empty/null means
        /// pre-versioned legacy data — the chain interprets that as the
        /// chain's lowest registered version.</summary>
        string SchemaVersion { get; set; }
    }
}
