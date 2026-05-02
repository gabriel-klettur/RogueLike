namespace Valkur.Core.Persistence
{
    /// <summary>
    /// Contract every persistence DTO must implement to participate in a
    /// generic migration chain. Holding the version on the document itself
    /// (instead of derived from filename or external metadata) makes
    /// migration self-contained and tolerant to file renames.
    ///
    /// Lives in <c>Valkur.Core.Persistence</c> so any assembly — Data,
    /// Gameplay, Infrastructure — can implement it without inverting the
    /// dependency graph. The migration engine itself
    /// (<c>MigrationChain&lt;T&gt;</c>) lives in
    /// <c>Valkur.Infrastructure.Migrations</c> and consumes this contract.
    /// </summary>
    public interface IVersioned
    {
        /// <summary>Schema version string, e.g. "1.0", "1.4". Empty/null means
        /// pre-versioned legacy data — the chain interprets that as the
        /// chain's lowest registered version.</summary>
        string SchemaVersion { get; set; }
    }
}
