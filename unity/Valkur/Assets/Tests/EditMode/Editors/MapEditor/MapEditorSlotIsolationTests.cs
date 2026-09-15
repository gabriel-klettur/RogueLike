using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;
using Valkur.Tests.Support;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Pins the cross-slot isolation invariants.
    ///
    /// The multi-map system promises that editing slot A never silently
    /// modifies slot B's persistence file. Two regressions broke that promise
    /// historically:
    ///
    ///   1. <c>MirrorWorkingCopyToActiveSlot</c> wrote the working copy to
    ///      whichever slot was active. If a save fired with a half-loaded
    ///      scene (boot window) it polluted the wrong slot.
    ///   2. The shared <c>_spawnedBuildings</c> list missed BuildingObjects
    ///      placed via the runtime editor, so <c>ClearSpawned</c> left them
    ///      alive across slot switches and the next save serialised them
    ///      into the new slot's JSON via FindObjectsOfType.
    ///
    /// (1) is regression-tested in
    /// <see cref="MapEditorBootSyncTests"/>; (2) in
    /// <see cref="Valkur.Tests.EditMode.Gameplay.World.Buildings.BuildingLoaderClearSpawnedTests"/>.
    /// This fixture pins the higher-level "round-trip preservation" property
    /// so a future refactor that dilutes either guard surfaces here too.
    /// </summary>
    [TestFixture]
    public class MapEditorSlotIsolationTests
    {
        private const string SLOT_A = "zzz_isolation_slot_a";
        private const string SLOT_B = "zzz_isolation_slot_b";

        private GameObject _mgrGo;
        private GameObject _zonesGo;
        private MapEditorManager _mgr;
        private ZoneManager _zones;

        // Parked user state.
        private string _activeSlotPath;
        private string _activeSlotParkedContent;
        private bool   _hadExistingActiveSlot;
        private string _slotAPath;
        private string _slotBPath;
        private bool   _hadExistingSlotA;
        private bool   _hadExistingSlotB;
        // The default-active persist below mirrors into the REAL Maps/default.zones.json.
        private string _defaultMirrorPath;
        private byte[] _defaultMirrorBytes;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearMapEditorSingleton();

            string mapsDir = Path.Combine(Application.persistentDataPath, "Maps");
            Directory.CreateDirectory(mapsDir);
            _defaultMirrorPath = Path.Combine(mapsDir, "default.zones.json");
            _defaultMirrorBytes = File.Exists(_defaultMirrorPath) ? File.ReadAllBytes(_defaultMirrorPath) : null;
            _activeSlotPath = Path.Combine(mapsDir, "_active.txt");
            _slotAPath      = Path.Combine(mapsDir, SLOT_A + ".zones.json");
            _slotBPath      = Path.Combine(mapsDir, SLOT_B + ".zones.json");

            _hadExistingActiveSlot = File.Exists(_activeSlotPath);
            _hadExistingSlotA      = File.Exists(_slotAPath);
            _hadExistingSlotB      = File.Exists(_slotBPath);

            if (_hadExistingActiveSlot)
            {
                _activeSlotParkedContent = File.ReadAllText(_activeSlotPath);
                File.Delete(_activeSlotPath);
            }
            if (_hadExistingSlotA) File.Delete(_slotAPath);
            if (_hadExistingSlotB) File.Delete(_slotBPath);

            Valkur.Core.MapEditorActiveSlot.SetOverrideForTests("default");
            // These slot files and the pointer are parked above, so the store may write them; a test
            // run refuses every other Maps write.
            Valkur.Core.WorldDataWriteGuard.AllowRealPathWritesFlag = true;
        }

        [TearDown]
        public void TearDown()
        {
            Valkur.Core.WorldDataWriteGuard.AllowRealPathWritesFlag = false;
            try { if (!_hadExistingSlotA && File.Exists(_slotAPath)) File.Delete(_slotAPath); } catch { }
            try { if (!_hadExistingSlotB && File.Exists(_slotBPath)) File.Delete(_slotBPath); } catch { }
            try
            {
                if (_defaultMirrorBytes != null) File.WriteAllBytes(_defaultMirrorPath, _defaultMirrorBytes);
                else if (File.Exists(_defaultMirrorPath)) File.Delete(_defaultMirrorPath);
            }
            catch { }
            try
            {
                if (_hadExistingActiveSlot && _activeSlotParkedContent != null)
                    File.WriteAllText(_activeSlotPath, _activeSlotParkedContent);
                else if (!_hadExistingActiveSlot && File.Exists(_activeSlotPath))
                    File.Delete(_activeSlotPath);
            }
            catch { }

            Valkur.Core.MapEditorActiveSlot.SetOverrideForTests(null);
            if (_mgrGo != null)   Object.DestroyImmediate(_mgrGo);
            if (_zonesGo != null) Object.DestroyImmediate(_zonesGo);
            ClearMapEditorSingleton();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void CreateManager()
        {
            _zonesGo = new GameObject("IsolationZones");
            _zones = _zonesGo.AddComponent<ZoneManager>();

            _mgrGo = new GameObject("IsolationMgr");
            _mgr = _mgrGo.AddComponent<MapEditorManager>();
            TestReflection.SetField(_mgr, "zoneManager", _zones);
            TestReflection.Invoke(_mgr, "EnsureCoreInitialized");
            _mgr.SetZonesRepository(new InMemoryMapEditorZonesRepository());
        }

        private static void WriteActiveSlotMarker(string slot)
        {
            string mapsDir = Path.Combine(Application.persistentDataPath, "Maps");
            Directory.CreateDirectory(mapsDir);
            File.WriteAllText(Path.Combine(mapsDir, "_active.txt"), slot);
        }

        // ── Tests ────────────────────────────────────────────────────────────

        [Test]
        public void Persist_WhileActiveSlotIsSlotA_DoesNotTouchSlotB()
        {
            // Active slot = A. PersistZonesToDisk must mirror to A's file
            // and absolutely must NOT touch B's file. This is the headline
            // cross-slot isolation contract.
            WriteActiveSlotMarker(SLOT_A);
            CreateManager();
            _zones.AddZone("zone_a", Vector2Int.zero, editableInTileEditor: true);

            // Pre-condition: B's file does not exist yet (TearDown / SetUp).
            Assert.IsFalse(File.Exists(_slotBPath), "Sanity: slot B file must not exist before test.");

            TestReflection.Invoke(_mgr, "PersistZonesToDisk");

            Assert.IsTrue(File.Exists(_slotAPath),
                "Persist must mirror to the ACTIVE slot's file (A) — that's how " +
                "auto-save lands the user's edits.");
            Assert.IsFalse(File.Exists(_slotBPath),
                "Persist while A is active MUST NOT touch slot B's file. " +
                "Cross-slot writes were the canonical regression that mirrored " +
                "scene state into the wrong map.");
        }

        [Test]
        public void Persist_DefaultSlotActive_DoesNotTouchCustomSlots()
        {
            // Default slot active. Mirror writes the file `Maps/default.zones.json`
            // (legitimate) but must not touch our test custom slots.
            WriteActiveSlotMarker("default");
            CreateManager();
            _zones.AddZone("zone_default", Vector2Int.zero, editableInTileEditor: true);

            TestReflection.Invoke(_mgr, "PersistZonesToDisk");

            Assert.IsFalse(File.Exists(_slotAPath),
                "Default-active persist must not touch unrelated custom slot files.");
            Assert.IsFalse(File.Exists(_slotBPath),
                "Default-active persist must not touch unrelated custom slot files.");
        }

        [Test]
        public void RoundTrip_PersistThenRereadProducesSameZoneSet()
        {
            // Writing a slot via PersistZonesToDisk and reading it back via
            // MapEditorMapSlots.ReadSlot must round-trip every zone — this is
            // the contract the Maps F11 explorer relies on for "save A,
            // switch to B, switch back to A, see A again".
            WriteActiveSlotMarker(SLOT_A);
            CreateManager();
            _zones.AddZone("rt_alpha", new Vector2Int(0, 0),  editableInTileEditor: true);
            _zones.AddZone("rt_beta",  new Vector2Int(50, 0), editableInTileEditor: false);
            _zones.AddZone("rt_gamma", new Vector2Int(100, 0), editableInTileEditor: true);

            TestReflection.Invoke(_mgr, "PersistZonesToDisk");

            string json = File.ReadAllText(_slotAPath);
            Assert.That(json, Does.Contain("rt_alpha"),
                "Round-tripped slot file must contain every persisted zone.");
            Assert.That(json, Does.Contain("rt_beta"),
                "Round-tripped slot file must contain every persisted zone.");
            Assert.That(json, Does.Contain("rt_gamma"),
                "Round-tripped slot file must contain every persisted zone.");
        }

        [Test]
        public void ActiveWorldId_DefaultSlot_IsBase()
        {
            // The implicit "default" slot maps to WorldId.Base so the legacy
            // flat persistence layout (StreamingAssets/Buildings/...,
            // persistentDataPath/MapOverrides/<zone>.overlay.json) keeps
            // working byte-for-byte. Pin this so a future "all slots are
            // routed" refactor can't silently regress single-map saves.
            WriteActiveSlotMarker("default");
            CreateManager();
            Assert.IsTrue(_mgr.ActiveWorldId.IsBase,
                "Default slot must resolve to WorldId.Base — anything else " +
                "breaks single-world byte-compat for the entire codebase.");
        }

        [Test]
        public void ActiveWorldId_CustomSlot_IsNotBase()
        {
            // Custom slots get a non-base WorldId so per-slot routing kicks
            // in. Without this property holding, every slot would share the
            // legacy flat persistence layout and the multi-map promise breaks.
            WriteActiveSlotMarker(SLOT_A);
            CreateManager();
            Assert.IsFalse(_mgr.ActiveWorldId.IsBase,
                "Custom slot must NOT map to WorldId.Base — that would route " +
                "its persistence into the legacy default layout and re-create " +
                "the cross-slot leak.");
            Assert.AreEqual(SLOT_A, _mgr.ActiveWorldId.Slug,
                "Custom slot's WorldId must keep its name as the slug for " +
                "human-readable on-disk paths.");
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        private static void ClearMapEditorSingleton()
        {
            var type = typeof(MapEditorManager).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }
    }
}
