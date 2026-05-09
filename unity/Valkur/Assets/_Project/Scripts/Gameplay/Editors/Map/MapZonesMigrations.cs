using System.Collections.Generic;
using Valkur.Infrastructure.Migrations;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Pre-configured <see cref="MigrationChain{T}"/> for the
    /// <c>map_editor_zones.json</c> schema.
    ///
    /// Pre-versioned legacy files (no <c>schemaVersion</c> field on disk) are
    /// auto-tagged 1.0 by the chain's "treat empty version as lowest
    /// registered" rule.
    /// </summary>
    internal static class MapZonesMigrations
    {
        private static readonly MigrationChain<ZonePersistenceFile> _chain
            = BuildChain();

        private static MigrationChain<ZonePersistenceFile> BuildChain()
        {
            var chain = new MigrationChain<ZonePersistenceFile>(MapZonesSchema.CurrentVersion);

            // 1.0 → 1.1: introduce per-slot portals list. Older files have no
            // portals field at all (or a null one after JsonUtility round-trip);
            // we backfill an empty list so downstream code can iterate without
            // null-checks.
            chain.Register(MapZonesSchema.V1_0, MapZonesSchema.V1_1, doc =>
            {
                if (doc.portals == null)
                    doc.portals = new List<PortalPersistenceEntry>();
            });

            return chain;
        }

        public static int Migrate(ZonePersistenceFile doc) => _chain.Migrate(doc);
    }
}
