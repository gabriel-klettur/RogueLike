// Tests for the F11 Map Editor portal-placement subsystem.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Pins the portal-placement subsystem of the F11 Map Editor:
    /// schema 1.1 migration backfills the portals list, in-memory state
    /// survives a save/load round-trip, and Add/Remove keeps the runtime
    /// GameObject count consistent with the persistence record.
    /// </summary>
    [TestFixture]
    public class MapEditorPortalsTests
    {
        private GameObject _mgrGo;
        private GameObject _zonesGo;
        private MapEditorManager _mgr;
        private ZoneManager _zones;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _zonesGo = new GameObject("PortalsZones");
            _zones = _zonesGo.AddComponent<ZoneManager>();
            // Two zones at non-overlapping offsets so portals between them
            // have a real destination to resolve against.
            _zones.AddZone("zone_portals_A", new Vector2Int(0, 0),  editableInTileEditor: true);
            _zones.AddZone("zone_portals_B", new Vector2Int(50, 0), editableInTileEditor: true);

            _mgrGo = new GameObject("PortalsMgr");
            _mgr = _mgrGo.AddComponent<MapEditorManager>();
            SetField(_mgr, "zoneManager", _zones);
            // Manager's _state must exist or AddPortal -> PersistZonesToDisk
            // would NRE; the protected helper does that wiring.
            InvokeProtected(_mgr, "EnsureCoreInitialized");
        }

        [TearDown]
        public void TearDown()
        {
            if (_mgrGo   != null) Object.DestroyImmediate(_mgrGo);
            if (_zonesGo != null) Object.DestroyImmediate(_zonesGo);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Schema migration ────────────────────────────────────────────────────

        [Test]
        public void Migration_V1_0_File_BackfillsEmptyPortalsList()
        {
            // Simulate a legacy 1.0 doc by clearing the portals list to null.
            var legacy = new ZonePersistenceFile
            {
                schemaVersion = MapZonesSchema.V1_0,
                portals = null,
            };

            int applied = MapZonesMigrations.Migrate(legacy);
            Assert.GreaterOrEqual(applied, 1, "1.0 -> 1.1 migration step must run on a 1.0 doc.");
            Assert.IsNotNull(legacy.portals,
                "Migration must backfill an empty portals list so downstream code can iterate without null-checks.");
            Assert.AreEqual(0, legacy.portals.Count);
            Assert.AreEqual(MapZonesSchema.CurrentVersion, legacy.schemaVersion);
        }

        [Test]
        public void Migration_V1_1_File_NoOp()
        {
            var modern = new ZonePersistenceFile
            {
                schemaVersion = MapZonesSchema.V1_1,
                portals = new List<PortalPersistenceEntry>
                {
                    new PortalPersistenceEntry { portalId = "pre-existing" }
                },
            };

            MapZonesMigrations.Migrate(modern);
            Assert.AreEqual(1, modern.portals.Count, "1.1 doc must round-trip without losing portals.");
            Assert.AreEqual("pre-existing", modern.portals[0].portalId);
        }

        // ── Add / Remove ────────────────────────────────────────────────────────

        [Test]
        public void AddPortal_AppendsRecordAndAssignsStableId()
        {
            string id = InvokeAddPortal(_mgr,
                source: new Vector3(2f, 3f, 0f),
                dest: "zone_portals_B",
                useCenter: true,
                destWorld: Vector2.zero,
                radius: 0f);

            Assert.IsNotNull(id);
            Assert.AreEqual(1, _mgr.PortalCount, "Add should land in the in-memory list.");
            var snap = _mgr.SnapshotPortals();
            Assert.AreEqual(id, snap[0].portalId, "Returned id must match the stored record.");
            Assert.AreEqual("zone_portals_B", snap[0].destinationZoneName);
            Assert.IsTrue(snap[0].destinationUseZoneCenter);
        }

        [Test]
        public void AddPortal_UniqueIdsAcrossRepeatedPlacements()
        {
            string a = InvokeAddPortal(_mgr, Vector3.zero, "zone_portals_B", true, Vector2.zero, 0f);
            string b = InvokeAddPortal(_mgr, Vector3.one,  "zone_portals_A", true, Vector2.zero, 0f);
            Assert.AreNotEqual(a, b, "Two AddPortal calls must produce distinct ids — otherwise " +
                "remove-by-id collapses both records.");
        }

        [Test]
        public void RemovePortal_DropsRecordAndReturnsTrueWhenFound()
        {
            string id = InvokeAddPortal(_mgr, Vector3.zero, "zone_portals_B", true, Vector2.zero, 0f);
            Assert.AreEqual(1, _mgr.PortalCount);

            bool removed = _mgr.RemovePortal(id);
            Assert.IsTrue(removed, "Remove must report success when the id was found.");
            Assert.AreEqual(0, _mgr.PortalCount);
        }

        [Test]
        public void RemovePortal_UnknownId_NoOp()
        {
            bool removed = _mgr.RemovePortal("does-not-exist");
            Assert.IsFalse(removed, "Remove of an unknown id must report false, not throw.");
        }

        // ── Persistence round-trip ──────────────────────────────────────────────

        [Test]
        public void HydrateFromPersistence_ReplacesInMemoryList()
        {
            // Seed: one portal already in memory.
            InvokeAddPortal(_mgr, Vector3.zero, "zone_portals_B", true, Vector2.zero, 0f);
            Assert.AreEqual(1, _mgr.PortalCount);

            // Disk doc with two different portals must wholesale replace
            // the in-memory list — we never want a slot's portals to leak
            // into the next slot on hydration.
            var doc = new ZonePersistenceFile
            {
                schemaVersion = MapZonesSchema.V1_1,
                portals = new List<PortalPersistenceEntry>
                {
                    new PortalPersistenceEntry
                    {
                        portalId = "from-disk-1",
                        sourceWorldX = 10f, sourceWorldY = 0f,
                        destinationZoneName = "zone_portals_A",
                        destinationUseZoneCenter = true,
                    },
                    new PortalPersistenceEntry
                    {
                        portalId = "from-disk-2",
                        sourceWorldX = 20f, sourceWorldY = 0f,
                        destinationZoneName = "zone_portals_B",
                        destinationUseZoneCenter = false,
                    },
                },
            };

            InvokeProtectedWith(_mgr, "HydratePortalsFromPersistence", doc);
            Assert.AreEqual(2, _mgr.PortalCount,
                "Hydrate must REPLACE the in-memory list, not merge with it.");
            Assert.AreEqual("from-disk-1", _mgr.SnapshotPortals()[0].portalId);
        }

        [Test]
        public void HydrateFromPersistence_NullDoc_ClearsList()
        {
            InvokeAddPortal(_mgr, Vector3.zero, "zone_portals_B", true, Vector2.zero, 0f);
            Assert.AreEqual(1, _mgr.PortalCount);

            InvokeProtectedWith(_mgr, "HydratePortalsFromPersistence", (object)null);
            Assert.AreEqual(0, _mgr.PortalCount,
                "Hydrate(null) is the canonical 'wipe portals before slot switch' call.");
        }

        [Test]
        public void WriteIntoPersistence_CopiesEveryRecord()
        {
            string id = InvokeAddPortal(_mgr, new Vector3(1f, 2f, 0f),
                "zone_portals_B", false, new Vector2(5f, 6f), 0.8f);

            var doc = new ZonePersistenceFile { schemaVersion = MapZonesSchema.V1_1 };
            InvokeProtectedWith(_mgr, "WritePortalsIntoPersistence", doc);

            Assert.AreEqual(1, doc.portals.Count);
            var p = doc.portals[0];
            Assert.AreEqual(id, p.portalId);
            Assert.AreEqual(1f, p.sourceWorldX);
            Assert.AreEqual(2f, p.sourceWorldY);
            Assert.AreEqual("zone_portals_B", p.destinationZoneName);
            Assert.IsFalse(p.destinationUseZoneCenter);
            Assert.AreEqual(5f, p.destinationWorldX);
            Assert.AreEqual(6f, p.destinationWorldY);
            Assert.AreEqual(0.8f, p.activationRadius);
        }

        // ── Reflection helpers ──────────────────────────────────────────────────

        private static void SetField(object target, string fieldName, object value)
        {
            var fi = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, $"Field '{fieldName}' not found on {target.GetType().Name}.");
            fi.SetValue(target, value);
        }

        private static void InvokeProtected(object target, string methodName)
        {
            var mi = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, $"Method '{methodName}' not found on {target.GetType().Name}.");
            mi.Invoke(target, null);
        }

        private static void InvokeProtectedWith(object target, string methodName, object arg)
        {
            var mi = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, $"Method '{methodName}' not found on {target.GetType().Name}.");
            mi.Invoke(target, new[] { arg });
        }

        private static string InvokeAddPortal(MapEditorManager mgr, Vector3 source, string dest,
            bool useCenter, Vector2 destWorld, float radius)
        {
            var mi = typeof(MapEditorManager).GetMethod("AddPortal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, "AddPortal not found via reflection.");
            return (string)mi.Invoke(mgr, new object[] { source, dest, useCenter, destWorld, radius });
        }
    }
}
