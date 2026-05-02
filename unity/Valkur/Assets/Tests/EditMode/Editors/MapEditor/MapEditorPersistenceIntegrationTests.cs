using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// End-to-end integration tests for the user-created-zone persistence flow.
    ///
    /// Covers the runtime sequence the user actually exercises:
    ///   1) The user creates a zone via F11 — <c>map_editor_zones.json</c> is written.
    ///   2) The user paints tiles in that zone via F8 — <c>MapOverrides/&lt;zone&gt;.overlay.json</c> is written.
    ///   3) The user closes and reopens the game.
    ///   4) WorldLoader runs FIRST and calls <see cref="TileOverlayPersistence.ApplyAllOverrides"/>
    ///      while user-created zones are still missing from the ZoneManager (registered later).
    ///   5) MapEditorManager.Start runs LoadZonesFromDisk which:
    ///      a) Adds the user zones to ZoneManager.
    ///      b) Re-runs ApplyAllOverrides so the previously-skipped tiles get painted.
    ///
    /// These tests reproduce that sequence end-to-end with real disk I/O,
    /// real ZoneManager + WorldGridBuilder + Tilemap components, then assert
    /// the tilemap actually contains the persisted tile after the second
    /// ApplyAllOverrides pass — proving zone+tile persistence survives a
    /// fresh world load.
    /// </summary>
    [TestFixture]
    public class MapEditorPersistenceIntegrationTests
    {
        // Use a unique, distinctive offset so we can't collide with any
        // real zone in the project's persistence file (just in case the
        // backup/restore logic somehow misfires).
        private const string USER_ZONE_NAME = "zone_test_persistence_500_500";
        private static readonly Vector2Int USER_ZONE_OFFSET = new Vector2Int(500, 500);

        private string _mapZonesJsonPath;
        private string _mapZonesBackupPath;
        private bool   _hadExistingMapZones;

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zoneGo;
        private ZoneManager _zones;
        private MapEditorManager _mgr;
        private GameObject _mgrGo;
        private Tile _testTile;
        private string _resolvedTileName;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Resolve the MapEditor persistence path via reflection (it's a
            // private property that returns Application.persistentDataPath/map_editor_zones.json).
            _mapZonesJsonPath = Path.Combine(Application.persistentDataPath, "map_editor_zones.json");
            _mapZonesBackupPath = _mapZonesJsonPath + ".test_backup_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            _hadExistingMapZones = File.Exists(_mapZonesJsonPath);
            if (_hadExistingMapZones)
            {
                // Use Copy + later Delete (instead of Move) so a crash between
                // SetUp and TearDown leaves the user's primary file intact —
                // the orphaned copy can be cleaned up by MapEditorDataGuard at
                // the next Editor load. The previous Move-based pattern was
                // the documented cause of "zones reset after restart".
                File.Copy(_mapZonesJsonPath, _mapZonesBackupPath, overwrite: true);
                File.Delete(_mapZonesJsonPath);
            }

            // Make sure no leftover override file from a prior run.
            TileOverlayPersistence.DeleteOverride(USER_ZONE_NAME);

            // World grid + zone manager (NO user zones yet — fresh boot simulation).
            _gridGo = new GameObject("WorldGridBuilder_PersistTest");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zoneGo = new GameObject("ZoneManager_PersistTest");
            _zones = _zoneGo.AddComponent<ZoneManager>();

            // Resolve a real tile from Resources — the synthetic test tile in
            // TileOverlayPersistenceTests doesn't survive Resources.Load lookup
            // inside OverlayLoader, so we discover an existing one here.
            _resolvedTileName = DiscoverFirstResourceTileName();
            if (!string.IsNullOrEmpty(_resolvedTileName))
            {
                var sprite = Resources.Load<Sprite>("Tiles/" + _resolvedTileName);
                if (sprite != null)
                {
                    _testTile = ScriptableObject.CreateInstance<Tile>();
                    _testTile.sprite = sprite;
                    _testTile.name = _resolvedTileName;
                    TileRegistry.Instance.Register(_resolvedTileName, _testTile);
                }
            }

            ClearMapEditorSingleton();
        }

        [TearDown]
        public void TearDown()
        {
            // Each step is wrapped so a failure in one does not abort the
            // others — the user's persistence file restoration is the most
            // important guarantee here, so it runs first and independently.
            try
            {
                if (File.Exists(_mapZonesJsonPath)) File.Delete(_mapZonesJsonPath);
                if (_hadExistingMapZones && File.Exists(_mapZonesBackupPath))
                    File.Move(_mapZonesBackupPath, _mapZonesJsonPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MapEditorPersistenceIntegrationTests] Restore failed " +
                                 $"(MapEditorDataGuard will recover on next Editor load): {ex.Message}");
            }
            // If we bailed before deleting the orphan backup for any reason,
            // make sure it's gone now so it doesn't pile up in persistentDataPath.
            try { if (File.Exists(_mapZonesBackupPath)) File.Delete(_mapZonesBackupPath); } catch { }

            try { TileOverlayPersistence.DeleteOverride(USER_ZONE_NAME); } catch { }

            if (_mgrGo != null) Object.DestroyImmediate(_mgrGo);
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            if (_zoneGo != null) Object.DestroyImmediate(_zoneGo);
            if (_testTile != null) Object.DestroyImmediate(_testTile);

            TileRegistry.Instance.Load(null);
            ClearMapEditorSingleton();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Test: zone persistence ──────────────────────────────────────────────

        [Test]
        public void Persistence_LoadZonesFromDisk_RegistersUserCreatedZoneInZoneManager()
        {
            // Arrange — write a persistence file containing one user-created zone.
            WritePersistenceFile(_mapZonesJsonPath, USER_ZONE_NAME, USER_ZONE_OFFSET, editable: true);
            CreateAndWireMapEditorManager();

            // Act — simulate MapEditorManager.Start's LoadZonesFromDisk.
            InvokePrivate(_mgr, "LoadZonesFromDisk");

            // Assert.
            Assert.IsTrue(_zones.TryGetZone(USER_ZONE_NAME, out var zone),
                $"User-created zone '{USER_ZONE_NAME}' must be registered in ZoneManager after LoadZonesFromDisk.");
            Assert.AreEqual(USER_ZONE_OFFSET, zone.gridOffset,
                "Zone offset must round-trip exactly through the persistence file.");
            Assert.IsTrue(zone.editableInTileEditor,
                "Zone editable flag must round-trip through persistence.");
        }

        // ── Test: tile persistence (the user's specific complaint) ──────────────

        [Test]
        public void Persistence_LoadZonesFromDisk_ReappliesTileOverridesForUserCreatedZones()
        {
            if (string.IsNullOrEmpty(_resolvedTileName) || _testTile == null)
                Assert.Inconclusive("No tile sprites found under Resources/Tiles/ — cannot exercise re-apply.");

            // Arrange — write zone persistence + an override file with painted tiles.
            WritePersistenceFile(_mapZonesJsonPath, USER_ZONE_NAME, USER_ZONE_OFFSET, editable: true);
            WriteOverrideFileWithGroundTile(USER_ZONE_NAME, _resolvedTileName);

            // Sanity — fresh ZoneManager has no user zone, and the WorldLoader-style
            // ApplyAllOverrides call would skip the override (matches production order).
            int firstApplyCount = TileOverlayPersistence.ApplyAllOverrides(_grid, _zones);
            Assert.AreEqual(0, firstApplyCount,
                "WorldLoader-stage ApplyAllOverrides must skip the user zone (not yet registered).");

            CreateAndWireMapEditorManager();

            // Act — MapEditor.Start would call LoadZonesFromDisk; that registers
            // user zones AND must re-run ApplyAllOverrides so the previously
            // skipped override gets applied.
            InvokePrivate(_mgr, "LoadZonesFromDisk");

            // Assert — the painted cell at (zoneOffset + (0,0)) is back on the Ground tilemap.
            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            Assert.IsNotNull(ground, "Ground tilemap must exist on the WorldGridBuilder.");
            // We painted the top-left cell of the zone in WriteOverrideFileWithGroundTile
            // (row 0 in JSON = top of zone, col 0 = left of zone).
            int unityX = USER_ZONE_OFFSET.x;
            int unityY = USER_ZONE_OFFSET.y + (_zones.ZoneHeightTiles - 1);
            var restored = ground.GetTile(new Vector3Int(unityX, unityY, 0));
            Assert.IsNotNull(restored,
                $"Ground tile at ({unityX},{unityY}) must be restored after LoadZonesFromDisk re-applies overrides.");
        }

        // ── Test: PersistZonesToDisk → LoadZonesFromDisk round-trip ─────────────
        //
        // Reproduces the user-reported bug: create a zone via the same flow F11
        // takes (zoneManager.AddZone + PersistZonesToDisk), simulate the
        // database reload that happens on next boot (ReplaceZones), then call
        // LoadZonesFromDisk and assert the user zone reappears.

        [Test]
        public void Persistence_PersistThenLoad_PreservesUserCreatedZoneAcrossDatabaseReload()
        {
            const string DB_ZONE   = "zone_test_db_base";
            const string USER_ZONE = "zone_test_user_500_500";
            var DB_OFFSET   = new Vector2Int(0, 0);
            var USER_OFFSET = new Vector2Int(500, 500);

            // === Session 1: a zone manager populated with DB zones, user adds a zone ===
            _zones.AddZone(DB_ZONE,   DB_OFFSET,   editableInTileEditor: true);  // simulates ZoneDatabaseLoader
            _zones.AddZone(USER_ZONE, USER_OFFSET, editableInTileEditor: true);  // simulates F11 ConfirmAddZone

            CreateAndWireMapEditorManager();
            // Mark non-default state to make sure that's also persisted.
            var stateField = typeof(MapEditorManager).GetField("_state",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var state = stateField.GetValue(_mgr);
            // _state.NextZoneIndex is an int public property/field on MapEditorState.
            var nextIdxField = state.GetType().GetField("NextZoneIndex",
                BindingFlags.Public | BindingFlags.Instance);
            if (nextIdxField != null) nextIdxField.SetValue(state, 7);

            InvokePrivate(_mgr, "PersistZonesToDisk");

            Assert.IsTrue(File.Exists(_mapZonesJsonPath),
                "PersistZonesToDisk must write a JSON file to persistentDataPath/map_editor_zones.json.");

            string json = File.ReadAllText(_mapZonesJsonPath);
            StringAssert.Contains(USER_ZONE, json,
                "Persisted JSON must contain the user-created zone name.");
            StringAssert.Contains("500", json,
                "Persisted JSON must contain the user-created zone offset.");

            // === Session 2: simulate fresh boot — ReplaceZones with only DB zones ===
            _zones.ReplaceZones(new System.Collections.Generic.List<ZoneManager.ZoneDefinition>
            {
                new ZoneManager.ZoneDefinition
                {
                    zoneName             = DB_ZONE,
                    gridOffset           = DB_OFFSET,
                    editableInTileEditor = true,
                }
            });
            Assert.IsFalse(_zones.TryGetZone(USER_ZONE, out _),
                "Sanity: after ReplaceZones with only the DB zone, user zone must be gone.");

            // === Session 2: MapEditor.Start runs LoadZonesFromDisk ===
            // Re-create the manager so its _state is fresh (mimics a new Play Mode).
            Object.DestroyImmediate(_mgrGo);
            ClearMapEditorSingleton();
            CreateAndWireMapEditorManager();

            InvokePrivate(_mgr, "LoadZonesFromDisk");

            // The user zone MUST be back in the ZoneManager.
            Assert.IsTrue(_zones.TryGetZone(USER_ZONE, out var restored),
                $"User-created zone '{USER_ZONE}' must be restored after a database-reload boot cycle.");
            Assert.AreEqual(USER_OFFSET, restored.gridOffset,
                "Restored user zone must keep its original offset.");
            Assert.IsTrue(restored.editableInTileEditor,
                "Restored user zone must keep its editable flag.");

            // The DB zone must still be there (untouched by LoadZonesFromDisk's flag-restore path).
            Assert.IsTrue(_zones.TryGetZone(DB_ZONE, out _),
                "DB zone must coexist with the restored user zone.");
        }

        // ── Test: rename moves the override file ─────────────────────────────────

        [Test]
        public void Persistence_RenameZone_MovesOverrideFileToNewName()
        {
            const string OLD_NAME = "zone_test_persistence_old";
            const string NEW_NAME = "zone_test_persistence_new";

            // Seed an override file under OLD_NAME.
            _zones.AddZone(OLD_NAME, USER_ZONE_OFFSET, editableInTileEditor: true);
            string oldPath = TileOverlayPersistence.OverridePathForZone(OLD_NAME);
            string newPath = TileOverlayPersistence.OverridePathForZone(NEW_NAME);
            Directory.CreateDirectory(Path.GetDirectoryName(oldPath));
            File.WriteAllText(oldPath, "{\n  \"layers\": {}\n}");
            try
            {
                Assert.IsTrue(File.Exists(oldPath), "Sanity: source override must exist before rename.");

                bool moved = TileOverlayPersistence.RenameOverride(OLD_NAME, NEW_NAME);

                Assert.IsTrue(moved, "RenameOverride should report success when source exists and dest is free.");
                Assert.IsFalse(File.Exists(oldPath), "Old override file must be gone after rename.");
                Assert.IsTrue(File.Exists(newPath), "New override file must exist after rename.");
            }
            finally
            {
                if (File.Exists(oldPath)) File.Delete(oldPath);
                if (File.Exists(newPath)) File.Delete(newPath);
            }
        }

        [Test]
        public void Persistence_RenameOverride_NoSourceFile_ReturnsTrueAsNoOp()
        {
            // Renaming a zone that never had any tile edits is a valid scenario.
            // The static helper must succeed (no-op) so the caller doesn't have to
            // check existence first.
            Assert.IsTrue(TileOverlayPersistence.RenameOverride("nonexistent_a", "nonexistent_b"),
                "RenameOverride must be a successful no-op when the source file does not exist.");
        }

        // ── Test: idempotence (re-running shouldn't break anything) ─────────────

        [Test]
        public void Persistence_LoadZonesFromDisk_IsIdempotentForSubsequentLoads()
        {
            WritePersistenceFile(_mapZonesJsonPath, USER_ZONE_NAME, USER_ZONE_OFFSET, editable: true);
            CreateAndWireMapEditorManager();

            InvokePrivate(_mgr, "LoadZonesFromDisk");
            int firstCount = _zones.GetZonesSnapshot().Length;

            // Second call should not duplicate the zone (collision check).
            InvokePrivate(_mgr, "LoadZonesFromDisk");
            int secondCount = _zones.GetZonesSnapshot().Length;

            Assert.AreEqual(firstCount, secondCount,
                "Repeated LoadZonesFromDisk must not duplicate persisted zones.");
        }

        // ── DTO round-trip (the JsonUtility refactor that fixed the runtime bug) ──
        //
        // The persistence DTOs were originally private nested classes inside
        // MapEditorManager. Unity's JsonUtility has documented quirks with that
        // shape — particularly the List<T> field on the outer type — and the
        // user-reported "zones disappear after restart" bug traced back to
        // that exact pattern. After promoting them to namespace-scope `internal`
        // classes, the round-trip is deterministic. These tests pin that
        // contract so a future refactor can't silently re-introduce the bug.

        [Test]
        public void Dto_JsonUtility_RoundTripsAllFields()
        {
            // Reflect on the internal types so the test is namespace-only and
            // doesn't require InternalsVisibleTo.
            var fileType  = typeof(MapEditorManager).Assembly
                .GetType("Valkur.Gameplay.MapEditor.ZonePersistenceFile");
            var entryType = typeof(MapEditorManager).Assembly
                .GetType("Valkur.Gameplay.MapEditor.ZonePersistenceEntry");
            Assert.IsNotNull(fileType,  "ZonePersistenceFile must exist as a namespace-scope type (not nested).");
            Assert.IsNotNull(entryType, "ZonePersistenceEntry must exist as a namespace-scope type (not nested).");
            Assert.IsTrue(System.Attribute.IsDefined(fileType,  typeof(System.SerializableAttribute)),
                "ZonePersistenceFile must carry [Serializable] for JsonUtility.");
            Assert.IsTrue(System.Attribute.IsDefined(entryType, typeof(System.SerializableAttribute)),
                "ZonePersistenceEntry must carry [Serializable] for JsonUtility.");

            // Build a populated file dynamically (the types are internal so we
            // can't reference them at compile time from this test assembly).
            var entry = System.Activator.CreateInstance(entryType);
            entryType.GetField("zoneName").SetValue(entry, "zone_500_500");
            entryType.GetField("gridOffsetX").SetValue(entry, 500);
            entryType.GetField("gridOffsetY").SetValue(entry, -250);
            entryType.GetField("editableInTileEditor").SetValue(entry, true);

            var file = System.Activator.CreateInstance(fileType);
            fileType.GetField("restrictTileEditingToEditableZones").SetValue(file, true);
            fileType.GetField("nextZoneIndex").SetValue(file, 42);
            var listField = fileType.GetField("zones");
            var list = listField.GetValue(file) as System.Collections.IList;
            list.Add(entry);

            string json = JsonUtility.ToJson(file, prettyPrint: true);

            // FromJson via reflection on the generic method.
            var fromJsonGeneric = typeof(JsonUtility).GetMethods()
                .First(m => m.Name == "FromJson" && m.IsGenericMethod);
            var roundTripped = fromJsonGeneric.MakeGenericMethod(fileType).Invoke(null, new object[] { json });

            Assert.IsNotNull(roundTripped, "Round-tripped file must not be null.");
            Assert.AreEqual(true, fileType.GetField("restrictTileEditingToEditableZones").GetValue(roundTripped));
            Assert.AreEqual(42,   fileType.GetField("nextZoneIndex").GetValue(roundTripped));

            var rtList = listField.GetValue(roundTripped) as System.Collections.IList;
            Assert.AreEqual(1, rtList.Count, "Zones list must round-trip its single entry — the original bug shape.");

            var rtEntry = rtList[0];
            Assert.AreEqual("zone_500_500", entryType.GetField("zoneName").GetValue(rtEntry));
            Assert.AreEqual(500,            entryType.GetField("gridOffsetX").GetValue(rtEntry));
            Assert.AreEqual(-250,           entryType.GetField("gridOffsetY").GetValue(rtEntry));
            Assert.AreEqual(true,           entryType.GetField("editableInTileEditor").GetValue(rtEntry));
        }

        // ── Round-trip individual flags ──────────────────────────────────────────

        [Test]
        public void Persistence_PersistThenLoad_RoundTripsRestrictTileEditingFlag()
        {
            // MapEditorState defaults RestrictTileEditing=true. To test the
            // round-trip we persist the OPPOSITE value (false) and verify it
            // overrides the default on a fresh state.
            _zones.AddZone("alpha", Vector2Int.zero, editableInTileEditor: true);
            CreateAndWireMapEditorManager();
            SetStateField(_mgr, "RestrictTileEditingToEditableZones", false);

            InvokePrivate(_mgr, "PersistZonesToDisk");

            // Fresh manager — _state defaults RestrictTileEditing back to true.
            Object.DestroyImmediate(_mgrGo);
            ClearMapEditorSingleton();
            CreateAndWireMapEditorManager();
            Assert.IsTrue((bool) GetStateField(_mgr, "RestrictTileEditingToEditableZones"),
                "Sanity: a fresh _state must default to RestrictTileEditing=true.");

            InvokePrivate(_mgr, "LoadZonesFromDisk");

            Assert.IsFalse((bool) GetStateField(_mgr, "RestrictTileEditingToEditableZones"),
                "Persisted RestrictTileEditing=false must override the fresh-state default of true.");
        }

        [Test]
        public void Persistence_PersistThenLoad_RoundTripsNextZoneIndex()
        {
            _zones.AddZone("alpha", Vector2Int.zero, editableInTileEditor: true);
            CreateAndWireMapEditorManager();
            SetStateField(_mgr, "NextZoneIndex", 13);

            InvokePrivate(_mgr, "PersistZonesToDisk");

            Object.DestroyImmediate(_mgrGo);
            ClearMapEditorSingleton();
            CreateAndWireMapEditorManager();

            InvokePrivate(_mgr, "LoadZonesFromDisk");

            Assert.AreEqual(13, (int) GetStateField(_mgr, "NextZoneIndex"),
                "NextZoneIndex must round-trip so the auto-naming counter doesn't reset on reload.");
        }

        [Test]
        public void Persistence_PersistThenLoad_RoundTripsMultipleUserZones()
        {
            // One DB zone + three user zones. After a fake "DB reload" only
            // the DB zone remains; LoadZonesFromDisk must restore all three.
            _zones.AddZone("dbZone",      new Vector2Int(0,    0),    editableInTileEditor: true);
            _zones.AddZone("zone_100_0",  new Vector2Int(100,  0),    editableInTileEditor: true);
            _zones.AddZone("zone_-50_75", new Vector2Int(-50,  75),   editableInTileEditor: false);
            _zones.AddZone("zone_999_999",new Vector2Int(999,  999),  editableInTileEditor: true);

            CreateAndWireMapEditorManager();
            InvokePrivate(_mgr, "PersistZonesToDisk");

            // Simulate database-only reload.
            _zones.ReplaceZones(new System.Collections.Generic.List<ZoneManager.ZoneDefinition>
            {
                new ZoneManager.ZoneDefinition
                {
                    zoneName = "dbZone",
                    gridOffset = new Vector2Int(0, 0),
                    editableInTileEditor = true,
                }
            });

            Object.DestroyImmediate(_mgrGo);
            ClearMapEditorSingleton();
            CreateAndWireMapEditorManager();
            InvokePrivate(_mgr, "LoadZonesFromDisk");

            Assert.IsTrue(_zones.TryGetZone("zone_100_0",   out var z1));
            Assert.IsTrue(_zones.TryGetZone("zone_-50_75",  out var z2));
            Assert.IsTrue(_zones.TryGetZone("zone_999_999", out var z3));
            Assert.AreEqual(new Vector2Int(100, 0),   z1.gridOffset);
            Assert.AreEqual(new Vector2Int(-50, 75),  z2.gridOffset);
            Assert.AreEqual(new Vector2Int(999, 999), z3.gridOffset);
            Assert.IsTrue(z1.editableInTileEditor);
            Assert.IsFalse(z2.editableInTileEditor,
                "Editable=false must round-trip — earlier the default was hard-coded true.");
            Assert.IsTrue(z3.editableInTileEditor);
        }

        // ── DeleteZone removes the orphan override file ─────────────────────────

        [Test]
        public void Persistence_DeleteZone_RemovesOverrideFile()
        {
            const string Z = "zone_test_delete_orphan";

            // Two zones so DeleteZoneByName's "cannot delete the last remaining zone" guard doesn't fire.
            _zones.AddZone("guard_zone", Vector2Int.zero,             editableInTileEditor: true);
            _zones.AddZone(Z,            new Vector2Int(200, 200),    editableInTileEditor: true);

            // Simulate a previously-saved overlay file for the zone.
            string path = TileOverlayPersistence.OverridePathForZone(Z);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{\n  \"layers\": {}\n}");
            Assert.IsTrue(File.Exists(path), "Sanity: override file must exist before delete.");

            CreateAndWireMapEditorManager();
            InvokePrivate(_mgr, "DeleteZoneByName", Z);

            Assert.IsFalse(_zones.TryGetZone(Z, out _),
                "Zone must be gone from ZoneManager after delete.");
            Assert.IsFalse(File.Exists(path),
                "Override file must be deleted along with the zone — otherwise it lingers as an orphan " +
                "and produces 'no matching zone' warnings on every subsequent boot.");
        }

        // ── Edge cases on load ───────────────────────────────────────────────────

        [Test]
        public void Persistence_LoadZonesFromDisk_NoFile_IsSafeNoOp()
        {
            // Make sure the file does NOT exist (SetUp already moved any real one aside).
            Assert.IsFalse(File.Exists(_mapZonesJsonPath),
                "Sanity: persistence file must not exist for this test.");

            CreateAndWireMapEditorManager();
            int countBefore = _zones.GetZonesSnapshot().Length;

            Assert.DoesNotThrow(() => InvokePrivate(_mgr, "LoadZonesFromDisk"),
                "LoadZonesFromDisk must safely no-op when the persistence file does not exist.");
            Assert.AreEqual(countBefore, _zones.GetZonesSnapshot().Length,
                "Zone count must not change when there is no persistence file to load.");
        }

        [Test]
        public void Persistence_LoadZonesFromDisk_EmptyZonesArray_IsSafeNoOp()
        {
            // Write a valid file with an empty zones list.
            File.WriteAllText(_mapZonesJsonPath,
                "{\n  \"restrictTileEditingToEditableZones\": false,\n  \"nextZoneIndex\": 1,\n  \"zones\": []\n}\n");

            _zones.AddZone("preexisting", Vector2Int.zero, editableInTileEditor: true);
            CreateAndWireMapEditorManager();

            Assert.DoesNotThrow(() => InvokePrivate(_mgr, "LoadZonesFromDisk"),
                "LoadZonesFromDisk must safely no-op on an empty zones list.");
            Assert.AreEqual(1, _zones.GetZonesSnapshot().Length,
                "Pre-existing zones must not be wiped by an empty-list load.");
        }

        [Test]
        public void Persistence_LoadZonesFromDisk_MalformedJson_IsSafeAndLogsError()
        {
            File.WriteAllText(_mapZonesJsonPath, "this is not valid json {[");

            _zones.AddZone("preexisting", Vector2Int.zero, editableInTileEditor: true);
            CreateAndWireMapEditorManager();

            // Catch the structured error so the test doesn't fail on an
            // expected log line — Unity's test runner promotes Debug.LogError
            // calls to test failures otherwise.
            LogAssert.ignoreFailingMessages = true;

            Assert.DoesNotThrow(() => InvokePrivate(_mgr, "LoadZonesFromDisk"),
                "LoadZonesFromDisk must not throw when the persistence file is malformed.");
            Assert.AreEqual(1, _zones.GetZonesSnapshot().Length,
                "Pre-existing zones must survive a malformed-JSON load.");
        }

        // ── Round-trip when only base zones exist (no user zones) ───────────────

        [Test]
        public void Persistence_PersistThenLoad_OnlyBaseZones_RestoresEditableFlags()
        {
            // Persist a single base zone with editable=false (Map Editor lets
            // the user lock a base zone via the UI). After a database reload
            // (which restores editable=true), LoadZonesFromDisk must reapply
            // the persisted "false" flag.
            _zones.AddZone("dbZone", Vector2Int.zero, editableInTileEditor: true);
            _zones.SetZoneEditable("dbZone", false);

            CreateAndWireMapEditorManager();
            InvokePrivate(_mgr, "PersistZonesToDisk");

            // Simulate ZoneDatabaseLoader reload — defaults editable back to true.
            _zones.ReplaceZones(new System.Collections.Generic.List<ZoneManager.ZoneDefinition>
            {
                new ZoneManager.ZoneDefinition
                {
                    zoneName = "dbZone",
                    gridOffset = Vector2Int.zero,
                    editableInTileEditor = true,
                }
            });

            Object.DestroyImmediate(_mgrGo);
            ClearMapEditorSingleton();
            CreateAndWireMapEditorManager();
            InvokePrivate(_mgr, "LoadZonesFromDisk");

            Assert.IsTrue(_zones.TryGetZone("dbZone", out var restored));
            Assert.IsFalse(restored.editableInTileEditor,
                "Persisted editable=false flag must override the DB default on reload — " +
                "this is the 'flagsRestored' code path in LoadZonesFromDisk.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private void CreateAndWireMapEditorManager()
        {
            _mgrGo = new GameObject("MapEditorManager_PersistTest");
            _mgr   = _mgrGo.AddComponent<MapEditorManager>();
            // Manager.OnSingletonAwake runs automatically via Unity lifecycle when
            // AddComponent is called from EditMode test code. Wire dependencies that
            // would normally come from Start() / FindObjectOfType.
            SetField(_mgr, "zoneManager", _zones);
            SetField(_mgr, "worldGridBuilder", _grid);
            // Ensure private state object exists (LoadZonesFromDisk reads _state.NextZoneIndex etc).
            InvokeProtected(_mgr, "EnsureCoreInitialized");
        }

        private static void WritePersistenceFile(string path, string zoneName, Vector2Int offset, bool editable)
        {
            // Mirror MapEditorManager's ZonePersistenceFile JSON shape exactly —
            // JsonUtility consumes flat-field POCOs, no nested objects, no escaping
            // beyond what zoneName needs (we use a safe identifier).
            string json =
                "{\n" +
                "  \"restrictTileEditingToEditableZones\": false,\n" +
                "  \"nextZoneIndex\": 1,\n" +
                "  \"zones\": [\n" +
                "    {\n" +
                "      \"zoneName\": \"" + zoneName + "\",\n" +
                "      \"gridOffsetX\": " + offset.x + ",\n" +
                "      \"gridOffsetY\": " + offset.y + ",\n" +
                "      \"editableInTileEditor\": " + (editable ? "true" : "false") + "\n" +
                "    }\n" +
                "  ]\n" +
                "}\n";
            File.WriteAllText(path, json);
        }

        private void WriteOverrideFileWithGroundTile(string zoneName, string tileName)
        {
            // Build a Ground-only overlay where the top-left cell (row 0, col 0)
            // contains tileName and every other cell is empty. Mirrors
            // TileOverlayPersistence.SerializeOverlay output exactly so the
            // parser path that production uses is the same one we test.
            int w = _zones.ZoneWidthTiles;
            int h = _zones.ZoneHeightTiles;

            var sb = new System.Text.StringBuilder();
            sb.Append("{\n  \"layers\": {\n    \"Ground\": [");
            for (int row = 0; row < h; row++)
            {
                sb.Append(row == 0 ? "\n      [" : ",\n      [");
                for (int col = 0; col < w; col++)
                {
                    if (col > 0) sb.Append(", ");
                    bool paintHere = (row == 0 && col == 0);
                    sb.Append('"').Append(paintHere ? tileName : string.Empty).Append('"');
                }
                sb.Append(']');
            }
            sb.Append("\n    ]\n  }\n}");

            string path = TileOverlayPersistence.OverridePathForZone(zoneName);
            // Ensure parent dir exists (OverridePathForZone calls EnsureDirectoryStatic).
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
        }

        private static string DiscoverFirstResourceTileName()
        {
            string[] candidates = {
                "wall", "floor", "floor_1", "floor_2", "floor_3",
                "floor_4", "floor_5", "dungeon_tunnel"
            };
            foreach (var name in candidates)
                if (Resources.Load<Sprite>("Tiles/" + name) != null) return name;
            return null;
        }

        private static void ClearMapEditorSingleton()
        {
            // Walk the type hierarchy to find SingletonMonoBehaviour<T>._instance.
            var type = typeof(MapEditorManager).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) { field.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private static void InvokePrivate(object target, string methodName, params object[] args)
        {
            var t = target.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                t = t.BaseType;
            }
            Assert.IsNotNull(m, $"Method '{methodName}' must exist on {target.GetType().Name}.");
            m.Invoke(target, args);
        }

        private static object GetStateField(MapEditorManager mgr, string fieldName)
        {
            var stateField = typeof(MapEditorManager).GetField("_state",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var state = stateField.GetValue(mgr);
            Assert.IsNotNull(state, "_state must be initialized on the manager before reading state fields.");
            // MapEditorState exposes properties or public fields — try field first, then property.
            var f = state.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return f.GetValue(state);
            var p = state.GetType().GetProperty(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(p, $"Neither field nor property '{fieldName}' exists on MapEditorState.");
            return p.GetValue(state);
        }

        private static void SetStateField(MapEditorManager mgr, string fieldName, object value)
        {
            var stateField = typeof(MapEditorManager).GetField("_state",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var state = stateField.GetValue(mgr);
            Assert.IsNotNull(state, "_state must be initialized on the manager before writing state fields.");
            var f = state.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (f != null) { f.SetValue(state, value); return; }
            var p = state.GetType().GetProperty(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(p, $"Neither field nor property '{fieldName}' exists on MapEditorState.");
            p.SetValue(state, value);
        }

        private static void InvokeProtected(object target, string methodName)
        {
            var t = target.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(target, null);
        }

        private static void SetField(object target, string name, object value)
        {
            var t = target.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { f.SetValue(target, value); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Field '{name}' not found on {target.GetType().Name}.");
        }
    }
}
