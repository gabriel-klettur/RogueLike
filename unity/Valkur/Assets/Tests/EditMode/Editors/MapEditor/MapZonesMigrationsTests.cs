using NUnit.Framework;
using Valkur.Core.Persistence;
using Valkur.Gameplay.MapEditor;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Pins the migration contract for <c>map_editor_zones.json</c>.
    ///
    /// Today the chain has zero registered steps because only v1.0 ever
    /// existed, but the wiring (DTO implements IVersioned, persistence
    /// path runs Migrate, an empty chain still tags the document version)
    /// must be in place before a future shape change so the upgrade can
    /// land as a one-line registration.
    /// </summary>
    [TestFixture]
    public class MapZonesMigrationsTests
    {
        [Test]
        public void ZonePersistenceFile_ImplementsIVersioned()
        {
            // Reflection-free contract check: the cast must succeed at compile time.
            IVersioned versioned = new ZonePersistenceFile();
            Assert.IsNotNull(versioned,
                "ZonePersistenceFile must implement IVersioned so MigrationChain<T> can drive it.");
        }

        [Test]
        public void SchemaVersion_GetterDelegatesToField()
        {
            var doc = new ZonePersistenceFile();
            doc.schemaVersion = "9.9";
            Assert.AreEqual("9.9", ((IVersioned)doc).SchemaVersion,
                "Reading via IVersioned must reflect the live schemaVersion field " +
                "(critical so JsonUtility-roundtripped docs work with the chain).");
        }

        [Test]
        public void SchemaVersion_SetterWritesField()
        {
            var doc = new ZonePersistenceFile();
            ((IVersioned)doc).SchemaVersion = "2.0";
            Assert.AreEqual("2.0", doc.schemaVersion,
                "Writing via IVersioned must update the field so the next save " +
                "persists the new tag.");
        }

        [Test]
        public void Migrate_FreshDoc_StaysAtCurrentVersion()
        {
            var doc = new ZonePersistenceFile { schemaVersion = MapZonesSchema.CurrentVersion };
            int applied = MapZonesMigrations.Migrate(doc);
            Assert.AreEqual(0, applied);
            Assert.AreEqual(MapZonesSchema.CurrentVersion, doc.schemaVersion);
        }

        [Test]
        public void Migrate_PreVersionedDoc_GetsTaggedToCurrent()
        {
            // A doc loaded from a build that never wrote schemaVersion has
            // null/empty there. The chain treats that as the lowest registered
            // version, runs zero steps (none registered), and stamps the tag
            // so the next save writes it explicitly. No warning expected
            // because the empty chain genuinely has nothing to do here.
            var doc = new ZonePersistenceFile { schemaVersion = null };
            MapZonesMigrations.Migrate(doc);
            Assert.AreEqual(MapZonesSchema.CurrentVersion, doc.schemaVersion,
                "After Migrate, even a pre-versioned doc must carry an explicit " +
                "current-version tag so future loads skip migration.");
        }

        [Test]
        public void GameSaveData_ImplementsIVersioned()
        {
            // GameSaveData lives in Valkur.Data; pinning the contract here
            // catches accidental removal of the interface during refactors.
            IVersioned versioned = new Valkur.Data.GameSaveData();
            Assert.IsNotNull(versioned);
            Assert.AreEqual("1.0", versioned.SchemaVersion,
                "GameSaveData defaults to the current save schema version.");
        }
    }
}
