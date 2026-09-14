using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Pepitoria's zone list has exactly one file — the base working copy — and no other map may
    /// write into it, merge it into its own file, or stand in for it.
    ///
    /// On 2026-09-14 all three happened in one afternoon of Seed World tests: persisting while a
    /// generated world was live wrote its 102 zones over the base working copy (45 -> 126 zones,
    /// eight real zones lost with the buildings standing in them), the shelved-zone merge read the
    /// BASE file into the generated world's file, and "loading default" applied
    /// <c>Maps/default.zones.json</c> — a mirror a fixture had left holding one "Alpha" zone — as if
    /// it were the base world. Each contract below is one of those, plus the Map editor's blank-map
    /// button, which could empty the base world outright.
    /// </summary>
    [TestFixture]
    public class MapEditorBaseWorldIsolationTests
    {
        private const string OTHER = "zzz_baseiso_other";

        private GameObject _mgrGo;
        private GameObject _zonesGo;
        private MapEditorManager _mgr;
        private ZoneManager _zones;
        private InMemoryMapEditorZonesRepository _repo;

        private string _mapsDir;
        private readonly ParkedFile _activeMarker = new ParkedFile();
        private readonly ParkedFile _defaultMirror = new ParkedFile();
        private readonly ParkedFile _otherSlot = new ParkedFile();

        private GameObject _player;
        private GameObject _previousPlayer;

        private string OtherSlotPath => Path.Combine(_mapsDir, OTHER + ".zones.json");
        private string DefaultMirrorPath => Path.Combine(_mapsDir, "default.zones.json");

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearMapEditorSingleton();

            // MapEditorMapSlots bakes persistentDataPath into its constructor, so the real Maps
            // directory is used — with every file this fixture can touch parked first.
            _mapsDir = Path.Combine(Application.persistentDataPath, "Maps");
            Directory.CreateDirectory(_mapsDir);
            _activeMarker.Park(Path.Combine(_mapsDir, "_active.txt"));
            _defaultMirror.Park(DefaultMirrorPath);
            _otherSlot.Park(OtherSlotPath);

            Valkur.Core.MapEditorActiveSlot.SetOverrideForTests("default");
            WorldExcursion.ResetForTests();
            _previousPlayer = Valkur.Core.EntityRegistry.Player;
        }

        [TearDown]
        public void TearDown()
        {
            WorldExcursion.ResetForTests();
            if (_player != null)
            {
                Valkur.Core.EntityRegistry.UnregisterPlayer(_player);
                Object.DestroyImmediate(_player);
            }
            if (_previousPlayer != null) Valkur.Core.EntityRegistry.RegisterPlayer(_previousPlayer);
            _otherSlot.Restore();
            _defaultMirror.Restore();
            _activeMarker.Restore();

            Valkur.Core.MapEditorActiveSlot.SetOverrideForTests(null);
            if (_mgrGo != null) Object.DestroyImmediate(_mgrGo);
            if (_zonesGo != null) Object.DestroyImmediate(_zonesGo);
            ClearMapEditorSingleton();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Contracts ────────────────────────────────────────────────────────────

        [Test]
        public void APersistWhileAnotherMapIsLive_LeavesTheBaseWorkingCopyUntouched()
        {
            string baseWorld = ZonesJson(("pepitoria_lobby", new Vector2Int(150, 50)),
                                         ("pepitoria_outskirts", new Vector2Int(200, -50)));
            CreateManager(activeSlot: OTHER, workingCopy: baseWorld);
            _zones.AddZone("generated_ocean", new Vector2Int(150, 50), editableInTileEditor: true);

            InvokePrivate(_mgr, "PersistZonesToDisk");

            Assert.AreEqual(baseWorld, _repo.ReadWithSidecarFallback(WorldId.Base, out _),
                "While another map is live, a persist must not write the base world's working copy. " +
                "It did, and the next boot of the base world read that map's zones back as its own.");
            Assert.That(File.ReadAllText(OtherSlotPath), Does.Contain("generated_ocean"),
                "The live map's zones must still land in that map's own file.");
        }

        [Test]
        public void APersistWhileAnotherMapIsLive_DoesNotAdoptTheBaseWorldsZones()
        {
            // The shelved-zone merge keeps on-disk entries whose offset collides with a live zone.
            // Reading the BASE file there stamped Pepitoria's catalog zones into generated worlds.
            CreateManager(activeSlot: OTHER,
                          workingCopy: ZonesJson(("pepitoria_lobby", new Vector2Int(150, 50))));
            _zones.AddZone("generated_ocean", new Vector2Int(150, 50), editableInTileEditor: true);

            InvokePrivate(_mgr, "PersistZonesToDisk");

            Assert.That(File.ReadAllText(OtherSlotPath), Does.Not.Contain("pepitoria_lobby"),
                "Another map's file must never collect the base world's zones through the shelved merge.");
        }

        [Test]
        public void WhileBootHoldsTheBaseWorld_APersistWritesTheWorkingCopy_NotTheActiveSlot()
        {
            // Boot loads the database + working copy BEFORE the slot sync replaces them, whatever
            // _active.txt says. A persist in that window describes the base world.
            CreateManager(activeSlot: OTHER, workingCopy: ZonesJson());
            SetField(_mgr, "_liveZonesOwnerPin", "default");
            _zones.AddZone("pepitoria_lobby", new Vector2Int(150, 50), editableInTileEditor: true);

            InvokePrivate(_mgr, "PersistZonesToDisk");

            Assert.That(_repo.ReadWithSidecarFallback(WorldId.Base, out _), Does.Contain("pepitoria_lobby"),
                "Base zones persisted during boot belong in the base working copy.");
            Assert.IsFalse(File.Exists(OtherSlotPath),
                "They must not be written into the slot _active.txt points at — that slot is not live yet.");
            Assert.IsFalse(File.Exists(DefaultMirrorPath),
                "Nor mirrored into default.zones.json while the pointer names another map.");
        }

        [Test]
        public void ReturningToTheBaseWorld_RebuildsItFromTheWorkingCopy_NotFromItsMirrorFile()
        {
            File.WriteAllText(OtherSlotPath, ZonesJson(("generated_ocean", new Vector2Int(-200, -200))));
            File.WriteAllText(DefaultMirrorPath, ZonesJson(("Alpha", new Vector2Int(0, 0))));
            string baseWorld = ZonesJson(("pepitoria_lobby", new Vector2Int(150, 50)),
                                         ("pepitoria_outskirts", new Vector2Int(200, -50)));
            CreateManager(activeSlot: "default", workingCopy: baseWorld);
            _zones.AddZone("pepitoria_lobby", new Vector2Int(150, 50), editableInTileEditor: true);
            _zones.AddZone("pepitoria_outskirts", new Vector2Int(200, -50), editableInTileEditor: true);

            Assert.IsTrue(_mgr.LoadMapSlot(OTHER), "Sanity: the other map loads.");
            Assert.IsTrue(_zones.TryGetZone("generated_ocean", out _), "Sanity: the other map is live.");

            Assert.IsTrue(_mgr.LoadMapSlot("default"), "Returning to the base world must succeed.");

            CollectionAssert.AreEquivalent(new[] { "pepitoria_lobby", "pepitoria_outskirts" }, LiveZoneNames(),
                "The base world comes back as its working copy describes it — not as the mirror file's " +
                "single fixture zone, and with nothing of the map just left.");
            Assert.That(_repo.ReadWithSidecarFallback(WorldId.Base, out _), Does.Not.Contain("generated_ocean"),
                "The round trip must leave the base working copy free of the other map's zones.");
            Assert.That(File.ReadAllText(OtherSlotPath), Does.Not.Contain("pepitoria_"),
                "And the other map's file free of the base world's.");
            Assert.AreEqual("default", _mgr.ActiveMapSlot, "The pointer must name the base world again.");
        }

        [Test]
        public void ATripOutOfPepitoria_ComesBackToTheExactSpotItLeftFrom()
        {
            File.WriteAllText(OtherSlotPath, ZonesJson(("generated_ocean", new Vector2Int(-200, -200))));
            CreateManager(activeSlot: "default",
                          workingCopy: ZonesJson(("pepitoria_lobby", new Vector2Int(150, 50))));
            _zones.AddZone("pepitoria_lobby", new Vector2Int(150, 50), editableInTileEditor: true);

            var leftFrom = new Vector3(171.5f, 62.25f, 0f);
            _player = new GameObject("TripPlayer");
            _player.transform.position = leftFrom;
            Valkur.Core.EntityRegistry.RegisterPlayer(_player);

            Assert.IsTrue(_mgr.LoadMapSlot(OTHER), "Sanity: the trip out succeeds.");
            Assert.IsTrue(WorldExcursion.IsAway, "Leaving Pepitoria for another map must write the return ticket.");
            Assert.AreEqual(OTHER, WorldExcursion.Destination);

            _player.transform.position = new Vector3(-180f, -170f, 0f); // wandered around the other map

            Assert.IsTrue(_mgr.LoadMapSlot("default"), "Sanity: the trip back succeeds.");
            Assert.AreEqual((Vector2)leftFrom, (Vector2)_player.transform.position,
                "Coming home must put the player EXACTLY where they left Pepitoria from, whichever load brought them.");
            Assert.IsFalse(WorldExcursion.IsAway, "Arriving home spends the ticket.");
        }

        [Test]
        public void TheBaseWorld_CannotBeBlankedByANewMap()
        {
            string baseWorld = ZonesJson(("pepitoria_lobby", new Vector2Int(150, 50)));
            CreateManager(activeSlot: "default", workingCopy: baseWorld);
            _zones.AddZone("pepitoria_lobby", new Vector2Int(150, 50), editableInTileEditor: true);

            Assert.IsFalse(_mgr.BeginNewMap("default"), "A blank map named 'default' must be refused.");
            Assert.IsFalse(_mgr.BeginNewMap(""), "An unnamed blank map resolves to 'default' and must be refused too.");

            Assert.AreEqual(baseWorld, _repo.ReadWithSidecarFallback(WorldId.Base, out _),
                "A refused blank map must not have persisted zero zones over the base working copy.");
            Assert.IsTrue(_zones.TryGetZone("pepitoria_lobby", out _), "Nor emptied the live base world.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void CreateManager(string activeSlot, string workingCopy)
        {
            File.WriteAllText(Path.Combine(_mapsDir, "_active.txt"), activeSlot);

            _zonesGo = new GameObject("BaseIsoZones");
            _zones = _zonesGo.AddComponent<ZoneManager>();

            _mgrGo = new GameObject("BaseIsoMgr");
            _mgr = _mgrGo.AddComponent<MapEditorManager>();
            SetField(_mgr, "zoneManager", _zones);
            InvokePrivate(_mgr, "EnsureCoreInitialized");

            _repo = new InMemoryMapEditorZonesRepository();
            _repo.SeedPrimary(WorldId.Base, workingCopy);
            _mgr.SetZonesRepository(_repo);
        }

        private List<string> LiveZoneNames()
        {
            var names = new List<string>();
            foreach (var z in _zones.GetZonesSnapshot()) names.Add(z.zoneName);
            return names;
        }

        /// <summary>The JSON shape <c>ZonePersistenceFile</c> round-trips, written by hand (the DTO is internal).</summary>
        private static string ZonesJson(params (string name, Vector2Int offset)[] zones)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"schemaVersion\": \"1.3\",\n  \"restrictTileEditingToEditableZones\": false,\n");
            sb.Append("  \"nextZoneIndex\": 1,\n  \"zones\": [\n");
            for (int i = 0; i < zones.Length; i++)
            {
                var (name, offset) = zones[i];
                sb.Append($"    {{ \"zoneName\": \"{name}\", \"gridOffsetX\": {offset.x}, " +
                          $"\"gridOffsetY\": {offset.y}, \"editableInTileEditor\": true }}");
                sb.Append(i == zones.Length - 1 ? "\n" : ",\n");
            }
            sb.Append("  ],\n  \"hasLastPlayerPosition\": false,\n  \"lastPlayerWorldX\": 0.0,\n");
            sb.Append("  \"lastPlayerWorldY\": 0.0,\n  \"portals\": [],\n  \"biomeBuildings\": [],\n");
            sb.Append("  \"databaseZoneRenames\": []\n}\n");
            return sb.ToString();
        }

        /// <summary>A file of the user's that a test may overwrite, moved aside and put back byte for byte.</summary>
        private sealed class ParkedFile
        {
            private string _path;
            private byte[] _bytes;

            public void Park(string path)
            {
                _path = path;
                _bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
                if (_bytes != null) File.Delete(path);
            }

            public void Restore()
            {
                if (_path == null) return;
                try
                {
                    if (File.Exists(_path)) File.Delete(_path);
                    if (_bytes != null) File.WriteAllBytes(_path, _bytes);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[MapEditorBaseWorldIsolationTests] Could not restore '{_path}': {ex.Message}");
                }
            }
        }

        private static void SetField(object obj, string name, object value)
        {
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) { f.SetValue(obj, value); return; }
            }
            Assert.Fail($"Field '{name}' not found on {obj.GetType().Name}.");
        }

        private static void InvokePrivate(object obj, string name)
        {
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                                    null, System.Type.EmptyTypes, null);
                if (m != null) { m.Invoke(obj, null); return; }
            }
            Assert.Fail($"Method '{name}' not found on {obj.GetType().Name}.");
        }

        private static void ClearMapEditorSingleton()
        {
            for (var type = typeof(MapEditorManager).BaseType; type != null; type = type.BaseType)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
            }
        }
    }
}
