using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Pins the boot-time slot-sync regression that ate user data.
    ///
    /// Symptom: starting the game with a custom slot active
    /// (<c>_active.txt = "TEST 2"</c>) showed a Frankenstein scene mixing
    /// the default world's DB zones with whatever zones happened to live
    /// in the working-copy persistence file. Worse, the next
    /// <see cref="MapEditorManager.PersistZonesToDisk"/> firing mirrored
    /// that mixed state into the custom slot's file, silently overwriting
    /// the user's saved zones. Buildings and tiles followed the same
    /// breakage pattern.
    ///
    /// Contracts pinned here:
    ///   1. Boot with a custom slot active: zones come from the slot file,
    ///      not from the working copy + DB merge.
    ///   2. Boot with the default slot active: legacy behaviour is preserved
    ///      (DB zones authoritative).
    ///   3. While the boot-sync flag is held, PersistZonesToDisk's mirror
    ///      step is a no-op so a half-loaded scene cannot pollute the slot
    ///      file.
    ///   4. The defensive guard inside BootSyncWithActiveSlotIfNeeded refuses
    ///      to run outside the boot window — a future caller invoking it
    ///      from gameplay code would silently corrupt slots without it.
    /// </summary>
    [TestFixture]
    public class MapEditorBootSyncTests
    {
        // We re-use the real persistentDataPath because MapEditorMapSlots
        // bakes Application.persistentDataPath into its constructor. SetUp
        // parks the user's _active.txt and any slot file we touch; TearDown
        // restores them. The test slot names are intentionally distinctive
        // ("zzz_*") so a debugger looking at persistentDataPath can tell
        // them apart from real user slots if a tear-down ever leaks.
        private const string CUSTOM_SLOT_NAME  = "zzz_bootsync_custom";
        private const string DEFAULT_SLOT_NAME = "default";

        private GameObject _mgrGo;
        private GameObject _zonesGo;
        private GameObject _gridGo;
        private MapEditorManager _mgr;
        private ZoneManager _zones;
        private WorldGridBuilder _grid;

        // Parked user state.
        private string _activeSlotPath;
        private string _activeSlotParkedContent;
        private bool   _hadExistingActiveSlot;
        private string _customSlotPath;
        private bool   _hadExistingCustomSlot;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearMapEditorSingleton();

            // Park user's _active.txt so the boot-time read in
            // MapEditorMapSlots' constructor sees whatever this test wants.
            string mapsDir = Path.Combine(Application.persistentDataPath, "Maps");
            Directory.CreateDirectory(mapsDir);
            _activeSlotPath = Path.Combine(mapsDir, "_active.txt");
            _hadExistingActiveSlot = File.Exists(_activeSlotPath);
            if (_hadExistingActiveSlot)
            {
                _activeSlotParkedContent = File.ReadAllText(_activeSlotPath);
                File.Delete(_activeSlotPath);
            }

            // Park any leftover slot file with our test name (defensive — a
            // crashed prior run can have leaked one).
            _customSlotPath = Path.Combine(mapsDir, CUSTOM_SLOT_NAME + ".zones.json");
            _hadExistingCustomSlot = File.Exists(_customSlotPath);
            if (_hadExistingCustomSlot) File.Delete(_customSlotPath);

            // Pin the active map slot for any code path that consults the
            // shared MapEditorActiveSlot helper (Buildings, lights). Tests
            // live in the streamingAssets default to avoid touching user
            // persistentDataPath buildings.
            Valkur.Core.MapEditorActiveSlot.SetOverrideForTests(DEFAULT_SLOT_NAME);
        }

        [TearDown]
        public void TearDown()
        {
            // Drop test-created slot file regardless of test outcome.
            try
            {
                if (File.Exists(_customSlotPath) && !_hadExistingCustomSlot)
                    File.Delete(_customSlotPath);
            }
            catch { }

            // Restore user's active-slot pointer.
            try
            {
                if (_hadExistingActiveSlot && _activeSlotParkedContent != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_activeSlotPath));
                    File.WriteAllText(_activeSlotPath, _activeSlotParkedContent);
                }
                else if (!_hadExistingActiveSlot && File.Exists(_activeSlotPath))
                {
                    File.Delete(_activeSlotPath);
                }
            }
            catch { }

            Valkur.Core.MapEditorActiveSlot.SetOverrideForTests(null);

            if (_mgrGo   != null) UnityEngine.Object.DestroyImmediate(_mgrGo);
            if (_zonesGo != null) UnityEngine.Object.DestroyImmediate(_zonesGo);
            if (_gridGo  != null) UnityEngine.Object.DestroyImmediate(_gridGo);
            ClearMapEditorSingleton();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void CreateManager()
        {
            _zonesGo = new GameObject("BootSyncZones");
            _zones = _zonesGo.AddComponent<ZoneManager>();

            _gridGo = new GameObject("BootSyncGrid");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _mgrGo = new GameObject("BootSyncMgr");
            _mgr = _mgrGo.AddComponent<MapEditorManager>();
            SetField(_mgr, "zoneManager", _zones);
            SetField(_mgr, "worldGridBuilder", _grid);
            InvokePrivate(_mgr, "EnsureCoreInitialized");
            // Use an in-memory zones repository so PersistZonesToDisk doesn't
            // touch the user's working copy on disk during the test.
            _mgr.SetZonesRepository(new InMemoryMapEditorZonesRepository());
        }

        private void WriteSlotFile(string slot, params (string name, Vector2Int offset)[] zonesToWrite)
        {
            // Build the JSON shape JsonUtility.FromJson<ZonePersistenceFile>
            // consumes. Kept hand-written so this test does not couple to the
            // internal DTO type (which is internal to Valkur.Gameplay).
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"schemaVersion\": \"1.2\",\n");
            sb.Append("  \"restrictTileEditingToEditableZones\": false,\n");
            sb.Append("  \"nextZoneIndex\": 1,\n");
            sb.Append("  \"zones\": [\n");
            for (int i = 0; i < zonesToWrite.Length; i++)
            {
                var (name, offset) = zonesToWrite[i];
                sb.Append("    {\n");
                sb.Append($"      \"zoneName\": \"{name}\",\n");
                sb.Append($"      \"gridOffsetX\": {offset.x},\n");
                sb.Append($"      \"gridOffsetY\": {offset.y},\n");
                sb.Append("      \"editableInTileEditor\": true\n");
                sb.Append(i == zonesToWrite.Length - 1 ? "    }\n" : "    },\n");
            }
            sb.Append("  ],\n");
            sb.Append("  \"hasLastPlayerPosition\": false,\n");
            sb.Append("  \"lastPlayerWorldX\": 0.0,\n");
            sb.Append("  \"lastPlayerWorldY\": 0.0,\n");
            sb.Append("  \"portals\": [],\n");
            sb.Append("  \"biomeBuildings\": []\n");
            sb.Append("}\n");
            string mapsDir = Path.Combine(Application.persistentDataPath, "Maps");
            Directory.CreateDirectory(mapsDir);
            File.WriteAllText(Path.Combine(mapsDir, slot + ".zones.json"), sb.ToString());
        }

        private void WriteActiveSlotMarker(string slot)
        {
            string mapsDir = Path.Combine(Application.persistentDataPath, "Maps");
            Directory.CreateDirectory(mapsDir);
            File.WriteAllText(Path.Combine(mapsDir, "_active.txt"), slot);
        }

        // ── Tests: mirror guard ───────────────────────────────────────────────

        [Test]
        public void IsBootSyncInProgress_DefaultsFalse()
        {
            CreateManager();
            Assert.IsFalse(_mgr.IsBootSyncInProgress,
                "Boot-sync flag must be false outside the Start() boot window — " +
                "a stale 'true' would silently disable PersistZonesToDisk's " +
                "mirror at runtime.");
        }

        [Test]
        public void MirrorWorkingCopyToActiveSlot_NoOp_WhenBootSyncInProgress()
        {
            CreateManager();
            // Pre-condition: no slot file exists (TearDown / SetUp guarantees
            // CUSTOM_SLOT_NAME has no leftover file).
            Assert.IsFalse(File.Exists(_customSlotPath), "Sanity: slot file must not exist before test.");

            // Pin the active slot so the mirror code path even has somewhere
            // to write to (a no-active-slot guard inside the mirror would
            // mask the test by accident).
            WriteActiveSlotMarker(CUSTOM_SLOT_NAME);

            // Force-set the boot flag and call the mirror directly with a
            // synthetic JSON. Verify the slot file did NOT appear.
            SetField(_mgr, "_isBootSyncInProgress", true);
            InvokePrivateWith(_mgr, "MirrorWorkingCopyToActiveSlot",
                "{\"schemaVersion\":\"1.2\",\"zones\":[]}");

            Assert.IsFalse(File.Exists(_customSlotPath),
                "MirrorWorkingCopyToActiveSlot must short-circuit while the boot " +
                "sync is in progress — without that guard, a half-loaded scene " +
                "would clobber the active slot's file with mixed DB+working-copy state. " +
                "This is the canonical regression that ate slot data on every launch.");
        }

        [Test]
        public void MirrorWorkingCopyToActiveSlot_RunsNormally_OutsideBootWindow()
        {
            CreateManager();
            WriteActiveSlotMarker(CUSTOM_SLOT_NAME);

            // Force-load slot store now (it caches _active.txt at construction).
            string activeSlot = _mgr.ActiveMapSlot;
            Assert.AreEqual(CUSTOM_SLOT_NAME, activeSlot, "Sanity: active slot reads the marker.");

            // Flag is OFF — mirror should run.
            SetField(_mgr, "_isBootSyncInProgress", false);
            InvokePrivateWith(_mgr, "MirrorWorkingCopyToActiveSlot",
                "{\"schemaVersion\":\"1.2\",\"zones\":[]}");

            Assert.IsTrue(File.Exists(_customSlotPath),
                "Mirror must write the slot file when boot sync is not in progress " +
                "— the auto-save mirror is the mechanism the user relies on for " +
                "every zone op to land in the active slot's file.");
        }

        // ── Tests: defensive guard on boot sync helper ────────────────────────

        [Test]
        public void BootSync_AbortsIfInvokedOutsideBootWindow()
        {
            // The helper must refuse to run when the boot flag isn't held.
            // Without this, a future caller from gameplay code would mirror
            // half-loaded state and corrupt slots silently — but the LogError
            // makes the misuse obvious in the console.
            WriteActiveSlotMarker(CUSTOM_SLOT_NAME);
            WriteSlotFile(CUSTOM_SLOT_NAME, ("alpha", new Vector2Int(50, 50)));

            CreateManager();
            // Flag is OFF here — call must abort with a LogError.
            SetField(_mgr, "_isBootSyncInProgress", false);

            // Capture the expected error so the test doesn't fail on
            // "unhandled error log".
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "BootSyncWithActiveSlotIfNeeded called outside the boot window"));
            InvokePrivate(_mgr, "BootSyncWithActiveSlotIfNeeded");

            // Sanity: zones should NOT have been replaced because we aborted.
            // ZoneManager started empty (CreateManager doesn't seed) so it
            // should still be empty.
            Assert.AreEqual(0, _zones.GetZonesSnapshot().Length,
                "Aborted boot-sync must not replace zones — it must short-circuit " +
                "before ApplySlotToZoneManager.");
        }

        // ── Tests: end-to-end boot flow ───────────────────────────────────────

        [Test]
        public void BootSync_DefaultSlot_NoOp()
        {
            // No active marker (or marker = "default") — boot sync must not
            // run because the legacy DB-as-source-of-truth is correct for
            // the default slot.
            WriteActiveSlotMarker("default");
            // A slot file for default would still exist in production, but
            // the path the boot-sync helper takes is gated on slot != default
            // so the file's contents don't matter here.

            CreateManager();
            // Seed a zone so we can detect whether boot sync ran (it would
            // wipe the seeded zone with a DB-backed snapshot).
            _zones.AddZone("seeded_zone", new Vector2Int(0, 0), editableInTileEditor: true);

            SetField(_mgr, "_isBootSyncInProgress", true);
            try { InvokePrivate(_mgr, "BootSyncWithActiveSlotIfNeeded"); }
            finally { SetField(_mgr, "_isBootSyncInProgress", false); }

            Assert.IsTrue(_zones.TryGetZone("seeded_zone", out _),
                "Default slot path must skip boot-sync — running it would wipe " +
                "the DB zones the WorldLoader already populated.");
        }

        [Test]
        public void BootSync_CustomSlot_ReplacesZonesFromSlotFile()
        {
            // The hard contract: with _active.txt = "X" and X.zones.json
            // present, the boot-sync replaces whatever the ZoneManager has
            // (DB+working-copy merge) with the slot's snapshot.
            WriteActiveSlotMarker(CUSTOM_SLOT_NAME);
            WriteSlotFile(CUSTOM_SLOT_NAME,
                ("custom_zone_a", new Vector2Int(100, 0)),
                ("custom_zone_b", new Vector2Int(150, 0)));

            CreateManager();
            // Seed a "default-DB-style" zone so we can prove it gets replaced.
            _zones.AddZone("db_zone_should_be_replaced", new Vector2Int(0, 0), editableInTileEditor: true);

            SetField(_mgr, "_isBootSyncInProgress", true);
            try { InvokePrivate(_mgr, "BootSyncWithActiveSlotIfNeeded"); }
            finally { SetField(_mgr, "_isBootSyncInProgress", false); }

            Assert.IsTrue(_zones.TryGetZone("custom_zone_a", out _),
                "Boot sync must apply zones from the slot file.");
            Assert.IsTrue(_zones.TryGetZone("custom_zone_b", out _),
                "All zones from the slot file must be applied, not just the first.");
            Assert.IsFalse(_zones.TryGetZone("db_zone_should_be_replaced", out _),
                "Zones already in the ZoneManager (the DB+working-copy merge from " +
                "WorldLoader/LoadZonesFromDisk) must be REPLACED — merging would " +
                "produce the Frankenstein scene that triggered this regression.");
        }

        [Test]
        public void BootSync_CustomSlot_MissingSlotFile_NoCorruption()
        {
            // _active.txt points at a slot whose file doesn't exist (the user
            // could have deleted it manually, or a crashed prior session
            // wrote the marker without writing the zones). The boot-sync
            // must NOT crash and must NOT create a stub slot file from the
            // current ZoneManager state — the mirror guard already covers
            // that, but pin it with an explicit test.
            WriteActiveSlotMarker(CUSTOM_SLOT_NAME);
            // Intentionally NO WriteSlotFile call.

            CreateManager();
            _zones.AddZone("seeded", new Vector2Int(0, 0), editableInTileEditor: true);

            SetField(_mgr, "_isBootSyncInProgress", true);
            try
            {
                Assert.DoesNotThrow(() => InvokePrivate(_mgr, "BootSyncWithActiveSlotIfNeeded"),
                    "Missing slot file must not crash boot-sync — the user can " +
                    "always re-pick a slot via F11 if their data is gone.");
            }
            finally { SetField(_mgr, "_isBootSyncInProgress", false); }

            Assert.IsFalse(File.Exists(_customSlotPath),
                "Boot sync must not silently create the missing slot file — that " +
                "would seed it with the wrong (default) zones the next time the " +
                "user re-loads it.");
            Assert.IsTrue(_zones.TryGetZone("seeded", out _),
                "Existing zones must not be wiped when the slot file is missing — " +
                "fall back to the legacy state, don't blow away what we already had.");
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        private static void SetField(object obj, string name, object value)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) { f.SetValue(obj, value); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Field '{name}' not found on {obj.GetType().Name}.");
        }

        private static void InvokePrivate(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, null); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Method '{name}' not found on {obj.GetType().Name}.");
        }

        private static void InvokePrivateWith(object obj, string name, params object[] args)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, args); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Method '{name}' not found on {obj.GetType().Name}.");
        }

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
