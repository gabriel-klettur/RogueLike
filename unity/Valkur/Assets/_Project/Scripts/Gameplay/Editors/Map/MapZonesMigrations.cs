using Valkur.Infrastructure.Migrations;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Pre-configured <see cref="MigrationChain{T}"/> for the
    /// <c>map_editor_zones.json</c> schema. Today the chain is empty (only
    /// v1.0 ever existed); the moment a shape change becomes necessary,
    /// register the step here and bump <see cref="MapZonesSchema.CurrentVersion"/>.
    ///
    /// Pre-versioned legacy files (no <c>schemaVersion</c> field on disk) are
    /// auto-tagged 1.0 by the chain's "treat empty version as lowest
    /// registered" rule.
    /// </summary>
    internal static class MapZonesMigrations
    {
        private static readonly MigrationChain<ZonePersistenceFile> _chain
            = new MigrationChain<ZonePersistenceFile>(MapZonesSchema.CurrentVersion);

        // Future migrations register here, e.g.:
        //   _chain.Register("1.0", "1.1", doc => { /* upgrade */ });

        public static int Migrate(ZonePersistenceFile doc) => _chain.Migrate(doc);
    }
}
