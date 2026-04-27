using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// Tests for <see cref="TileOverlayPersistence"/> — the per-zone disk persistence
    /// layer that mirrors Python's overlay JSON schema.
    ///
    /// Covers: dirty-tracking events, per-zone targeting, save round-trip
    /// (write → file exists → re-apply restores tiles), and cross-zone isolation.
    /// All disk I/O happens under <c>Application.persistentDataPath/MapOverrides/</c>;
    /// every test uses a unique zone name and cleans up its file in TearDown.
    /// </summary>
    [TestFixture]
    public class TileOverlayPersistenceTests
    {
        private const string ZONE_A = "zone_test_persistence_A";
        private const string ZONE_B = "zone_test_persistence_B";

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zoneGo;
        private ZoneManager _zones;
        private TileOverlayPersistence _persistence;
        private Tile _floorTile;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zoneGo = new GameObject("ZoneManager");
            _zones = _zoneGo.AddComponent<ZoneManager>();
            // Zone A at (0,0)..(49,49), Zone B at (50,0)..(99,49)
            _zones.AddZone(ZONE_A, new Vector2Int(0, 0),  editableInTileEditor: true);
            _zones.AddZone(ZONE_B, new Vector2Int(50, 0), editableInTileEditor: true);

            _persistence = new TileOverlayPersistence(_zones, _grid);

            _floorTile = ScriptableObject.CreateInstance<Tile>();
            _floorTile.name = "test_floor";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            _floorTile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            _floorTile.sprite.name = "test_floor";

            // Ensure the registry knows the tile so save/load can resolve it by name.
            TileRegistry.Instance.Register("test_floor", _floorTile);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // Wipe override files this test produced (avoids cross-run pollution).
            TileOverlayPersistence.DeleteOverride(ZONE_A);
            TileOverlayPersistence.DeleteOverride(ZONE_B);

            UnityEngine.Object.DestroyImmediate(_gridGo);
            UnityEngine.Object.DestroyImmediate(_zoneGo);
            UnityEngine.Object.DestroyImmediate(_floorTile);
            TileRegistry.Instance.Load(null);
        }

        // ── Dirty tracking ───────────────────────────────────────────────

        [Test]
        public void HasUnsavedChanges_StartsFalse()
        {
            Assert.IsFalse(_persistence.HasUnsavedChanges);
            Assert.AreEqual(0, _persistence.DirtyZoneCount);
        }

        [Test]
        public void MarkCellDirty_AddsOwnerZoneToDirtySet_AndFiresEvent()
        {
            int eventCount = 0;
            _persistence.OnDirtyChanged += () => eventCount++;

            _persistence.MarkCellDirty(new Vector3Int(5, 5, 0)); // inside ZONE_A

            Assert.IsTrue(_persistence.HasUnsavedChanges);
            Assert.AreEqual(1, _persistence.DirtyZoneCount);
            CollectionAssert.Contains(new System.Collections.Generic.List<string>(_persistence.DirtyZones), ZONE_A);
            Assert.AreEqual(1, eventCount);
        }

        [Test]
        public void MarkCellDirty_OutsideAnyZone_DoesNotMarkDirty()
        {
            _persistence.MarkCellDirty(new Vector3Int(9999, 9999, 0));

            Assert.IsFalse(_persistence.HasUnsavedChanges);
        }

        [Test]
        public void MarkCellDirty_SecondCallSameZone_DoesNotFireEventAgain()
        {
            int eventCount = 0;
            _persistence.OnDirtyChanged += () => eventCount++;

            _persistence.MarkCellDirty(new Vector3Int(0, 0, 0));
            _persistence.MarkCellDirty(new Vector3Int(1, 1, 0));

            Assert.AreEqual(1, eventCount,
                "Marking a second cell in the same zone must not re-fire OnDirtyChanged.");
            Assert.AreEqual(1, _persistence.DirtyZoneCount);
        }

        [Test]
        public void MarkBatchDirty_MultipleZones_AddsAllOwners()
        {
            var edits = new System.Collections.Generic.List<TileEdit>
            {
                new TileEdit(new Vector3Int(1, 1, 0),    null, _floorTile),  // ZONE_A
                new TileEdit(new Vector3Int(60, 10, 0),  null, _floorTile),  // ZONE_B
            };

            _persistence.MarkBatchDirty(edits);

            Assert.AreEqual(2, _persistence.DirtyZoneCount);
        }

        [Test]
        public void ClearDirtyState_ResetsCount_AndFiresEvent()
        {
            _persistence.MarkCellDirty(Vector3Int.zero);

            int eventCount = 0;
            _persistence.OnDirtyChanged += () => eventCount++;

            _persistence.ClearDirtyState();

            Assert.IsFalse(_persistence.HasUnsavedChanges);
            Assert.AreEqual(1, eventCount);
        }

        // ── Override path / file management ──────────────────────────────

        [Test]
        public void OverridePathForZone_HasExpectedShape()
        {
            string path = TileOverlayPersistence.OverridePathForZone(ZONE_A);
            Assert.IsTrue(path.EndsWith(ZONE_A + ".overlay.json"),
                $"Expected path to end with '{ZONE_A}.overlay.json' but got '{path}'.");
            Assert.IsTrue(path.StartsWith(Application.persistentDataPath),
                "Override path must live under persistentDataPath.");
        }

        // ── Save round-trip ──────────────────────────────────────────────

        [Test]
        public void SaveZone_WritesFileToDisk_AndClearsDirty()
        {
            // Paint into the Ground layer of zone A.
            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            ground.SetTile(new Vector3Int(2, 3, 0), _floorTile);
            _persistence.MarkCellDirty(new Vector3Int(2, 3, 0));

            string savedZone = null;
            _persistence.OnZoneSaved += z => savedZone = z;

            bool ok = _persistence.SaveZone(ZONE_A);

            Assert.IsTrue(ok, "SaveZone must return true on success.");
            Assert.AreEqual(ZONE_A, savedZone);
            Assert.IsTrue(File.Exists(TileOverlayPersistence.OverridePathForZone(ZONE_A)));
            Assert.IsFalse(_persistence.HasUnsavedChanges,
                "Successful save must clear the dirty flag for that zone.");
        }

        [Test]
        public void SaveAllDirty_OnlySavesZonesThatWereMarked()
        {
            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            ground.SetTile(new Vector3Int(0, 0, 0), _floorTile);    // ZONE_A
            _persistence.MarkCellDirty(new Vector3Int(0, 0, 0));

            int saved = _persistence.SaveAllDirty();

            Assert.AreEqual(1, saved);
            Assert.IsTrue(File.Exists(TileOverlayPersistence.OverridePathForZone(ZONE_A)));
            Assert.IsFalse(File.Exists(TileOverlayPersistence.OverridePathForZone(ZONE_B)),
                "Zone B was never marked dirty — its file must not exist.");
        }

        [Test]
        public void SaveAllDirty_NoDirtyZones_ReturnsZero()
        {
            Assert.AreEqual(0, _persistence.SaveAllDirty());
        }

        // ── Apply / round-trip restore ───────────────────────────────────

        [Test]
        public void ApplyAllOverrides_RestoresPaintedTiles_AfterClear()
        {
            // ApplyAllOverrides re-paints via OverlayLoader, which resolves names through
            // Resources.Load<Sprite>("Tiles/" + name). The synthetic test tile we created
            // in SetUp is NOT in Resources/, so we discover a real one for this test.
            string realTileName = DiscoverFirstResourceTileName();
            if (string.IsNullOrEmpty(realTileName))
                Assert.Inconclusive("No tile sprites found under Resources/Tiles/ — cannot test re-apply round-trip.");

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            var realSprite = Resources.Load<Sprite>("Tiles/" + realTileName);
            // Pre-load the tile through the loader cache so SetTile uses the same instance.
            var realTile = ScriptableObject.CreateInstance<Tile>();
            realTile.sprite = realSprite;
            realTile.name = realTileName;
            TileRegistry.Instance.Register(realTileName, realTile);

            var painted = new[]
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(5, 5, 0),
                new Vector3Int(10, 12, 0),
            };
            foreach (var p in painted)
            {
                ground.SetTile(p, realTile);
                _persistence.MarkCellDirty(p);
            }
            Assert.IsTrue(_persistence.SaveZone(ZONE_A), "Initial save must succeed.");

            // Clear every painted cell to simulate a fresh world load.
            foreach (var p in painted)
                ground.SetTile(p, null);
            foreach (var p in painted)
                Assert.IsNull(ground.GetTile(p), "Sanity: cell must be empty before re-apply.");

            // Re-apply overrides → all painted cells must come back with the same tile name.
            // OverlayLoader emits Debug.Log on success (Log-level, never fails tests).
            int applied = TileOverlayPersistence.ApplyAllOverrides(_grid, _zones);

            Assert.GreaterOrEqual(applied, 1, "ApplyAllOverrides should restore at least one zone.");
            foreach (var p in painted)
            {
                var restored = ground.GetTile(p);
                Assert.IsNotNull(restored, $"Cell {p} must be restored after override re-apply.");
                Assert.AreEqual(realTileName, TileRegistry.Instance.GetName(restored));
            }

            UnityEngine.Object.DestroyImmediate(realTile);
        }

        private static string DiscoverFirstResourceTileName()
        {
            // Match the path-based resolution in OverlayLoader.ResolveSprite.
            string[] candidates = {
                "wall", "floor", "floor_1", "floor_2", "floor_3",
                "floor_4", "floor_5", "dungeon_tunnel"
            };
            foreach (var name in candidates)
                if (Resources.Load<Sprite>("Tiles/" + name) != null) return name;
            return null;
        }

        private static string DiscoverFirstResourceTileNameInFolder(string folder)
        {
            var sprites = Resources.LoadAll<Sprite>(folder);
            if (sprites == null) return null;

            for (int i = 0; i < sprites.Length; i++)
            {
                var sprite = sprites[i];
                if (sprite != null && !string.IsNullOrEmpty(sprite.name))
                    return sprite.name;
            }

            return null;
        }

        private string ReadGroundCellFromOverride(string zoneName, int localX, int localYFromBottom)
        {
            string path = TileOverlayPersistence.OverridePathForZone(zoneName);
            Assert.IsTrue(File.Exists(path), $"Override file for zone '{zoneName}' must exist.");

            string json = File.ReadAllText(path);
            var root = MiniJsonRuntime.Deserialize(json) as Dictionary<string, object>;
            Assert.IsNotNull(root, "Override JSON must deserialize.");

            var layers = root["layers"] as Dictionary<string, object>;
            Assert.IsNotNull(layers, "Override JSON must contain 'layers'.");

            if (!layers.TryGetValue("Ground", out var groundLayer))
                return string.Empty;

            var rows = groundLayer as List<object>;
            Assert.IsNotNull(rows, "Override JSON must contain the Ground layer.");

            int rowIndex = _zones.ZoneHeightTiles - 1 - localYFromBottom;
            var row = rows[rowIndex] as List<object>;
            Assert.IsNotNull(row, "Expected a valid Ground row in override JSON.");

            return row[localX] as string;
        }

        [Test]
        public void ApplyAllOverrides_RestoresSandOceanTileStoredInCategorySubfolder()
        {
            string sandOceanTileName = DiscoverFirstResourceTileNameInFolder("Tiles/sand_ocean");
            if (string.IsNullOrEmpty(sandOceanTileName))
                Assert.Inconclusive("No sprites found under Resources/Tiles/sand_ocean/.");

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            var sprite = Resources.Load<Sprite>("Tiles/sand_ocean/" + sandOceanTileName);
            Assert.IsNotNull(sprite, $"Expected to load sand_ocean sprite '{sandOceanTileName}'.");

            var sandOceanTile = ScriptableObject.CreateInstance<Tile>();
            sandOceanTile.sprite = sprite;
            sandOceanTile.name = sandOceanTileName;
            TileRegistry.Instance.Register(sandOceanTileName, sandOceanTile);

            var cell = new Vector3Int(4, 6, 0);
            ground.SetTile(cell, sandOceanTile);
            _persistence.MarkCellDirty(cell);
            Assert.IsTrue(_persistence.SaveZone(ZONE_A), "Initial save must succeed.");

            ground.SetTile(cell, null);
            Assert.IsNull(ground.GetTile(cell), "Sanity: cell must be empty before re-apply.");

            int applied = TileOverlayPersistence.ApplyAllOverrides(_grid, _zones);

            Assert.GreaterOrEqual(applied, 1);
            var restored = ground.GetTile(cell);
            Assert.IsNotNull(restored, "sand_ocean tiles must survive a full save/reload round-trip.");
            Assert.AreEqual(sandOceanTileName, TileRegistry.Instance.GetName(restored));

            UnityEngine.Object.DestroyImmediate(sandOceanTile);
        }

        [Test]
        public void SaveZone_ErasedTile_IsSerializedToItsOwningZoneOnly()
        {
            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            var zoneATile = new Vector3Int(2, 3, 0);
            var zoneBTile = new Vector3Int(52, 7, 0);

            ground.SetTile(zoneATile, _floorTile);
            ground.SetTile(zoneBTile, _floorTile);
            _persistence.MarkCellDirty(zoneATile);
            _persistence.MarkCellDirty(zoneBTile);
            Assert.AreEqual(2, _persistence.SaveAllDirty(), "Baseline save for both zones must succeed.");

            ground.SetTile(zoneATile, null);
            _persistence.MarkCellDirty(zoneATile);
            Assert.IsTrue(_persistence.SaveZone(ZONE_A), "Erased zone must save successfully.");

            Assert.AreEqual(string.Empty, ReadGroundCellFromOverride(ZONE_A, localX: 2, localYFromBottom: 3),
                "The erased tile must be persisted as empty in zone A.");
            Assert.AreEqual("test_floor", ReadGroundCellFromOverride(ZONE_B, localX: 2, localYFromBottom: 7),
                "Saving zone A must not rewrite zone B's persisted tile data.");
        }
    }
}
