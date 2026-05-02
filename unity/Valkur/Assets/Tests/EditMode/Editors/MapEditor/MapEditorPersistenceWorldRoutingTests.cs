using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Phase 1 contract: MapEditorManager.Persistence must route saves
    /// through the WorldId set via SetPersistenceWorld so multi-world
    /// users do not see their custom zones bleed into the base world's
    /// map_editor_zones.json file.
    /// </summary>
    [TestFixture]
    public class MapEditorPersistenceWorldRoutingTests
    {
        private GameObject _mgrGo;
        private GameObject _zonesGo;
        private MapEditorManager _mgr;
        private ZoneManager _zones;

        [SetUp]
        public void SetUp()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            ClearMapEditorSingleton();

            _zonesGo = new GameObject("WorldRoutingZones");
            _zones = _zonesGo.AddComponent<ZoneManager>();

            _mgrGo = new GameObject("WorldRoutingMgr");
            _mgr = _mgrGo.AddComponent<MapEditorManager>();
            SetField(_mgr, "zoneManager", _zones);
            // Initialize the private state field the persistence read/write code paths touch.
            InvokeProtected(_mgr, "EnsureCoreInitialized");
        }

        [TearDown]
        public void TearDown()
        {
            if (_mgrGo   != null) Object.DestroyImmediate(_mgrGo);
            if (_zonesGo != null) Object.DestroyImmediate(_zonesGo);
            ClearMapEditorSingleton();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void DefaultWorld_IsBase()
        {
            Assert.AreEqual(WorldId.Base, _mgr.PersistenceWorldId,
                "Default WorldId must be Base so legacy single-world boot is " +
                "byte-compatible with the existing map_editor_zones.json layout.");
        }

        [Test]
        public void SetPersistenceWorld_StoresValue()
        {
            var alt = new WorldId(System.Guid.NewGuid(), "the_abyss");
            _mgr.SetPersistenceWorld(alt);
            Assert.AreEqual(alt, _mgr.PersistenceWorldId);
        }

        [Test]
        public void Persist_RoutesToActiveWorld()
        {
            var repo = new InMemoryMapEditorZonesRepository();
            _mgr.SetZonesRepository(repo);

            var alt = new WorldId(System.Guid.NewGuid(), "the_abyss");
            _mgr.SetPersistenceWorld(alt);

            // Seed at least one zone so the saved JSON is non-empty enough
            // to be observable in the repo store.
            _zones.AddZone("alpha", Vector2Int.zero, editableInTileEditor: true);

            InvokePrivate(_mgr, "PersistZonesToDisk");

            Assert.IsTrue(repo.Exists(alt),
                "Persist must land in the active world's slot in the repo.");
            Assert.IsFalse(repo.Exists(WorldId.Base),
                "Persist must NOT bleed into WorldId.Base when the manager is " +
                "scoped to another world. This guards multi-world data isolation.");
        }

        [Test]
        public void Persist_DefaultsToBase_PreservesLegacyLayout()
        {
            var repo = new InMemoryMapEditorZonesRepository();
            _mgr.SetZonesRepository(repo);

            _zones.AddZone("alpha", Vector2Int.zero, editableInTileEditor: true);

            InvokePrivate(_mgr, "PersistZonesToDisk");

            Assert.IsTrue(repo.Exists(WorldId.Base),
                "Without SetPersistenceWorld, the manager must persist to " +
                "WorldId.Base — legacy single-world data must not regress.");
        }

        // ── Reflection helpers ──────────────────────────────────────────────────

        private static void SetField(object obj, string name, object value)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { f.SetValue(obj, value); return; }
                t = t.BaseType;
            }
        }

        private static void InvokePrivate(object obj, string methodName)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, null); return; }
                t = t.BaseType;
            }
        }

        private static void InvokeProtected(object obj, string methodName)
            => InvokePrivate(obj, methodName);

        private static void ClearMapEditorSingleton()
        {
            var type = typeof(MapEditorManager).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) { field.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }
    }
}
