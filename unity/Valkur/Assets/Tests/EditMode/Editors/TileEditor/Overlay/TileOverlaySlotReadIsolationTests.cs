using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// READ-side per-slot isolation for <see cref="TileOverlayPersistence.ApplyAllOverrides(WorldGridBuilder, ZoneManager, WorldId)"/>.
    ///
    /// <c>TileOverlayPerSlotRoutingTests</c> and <c>MapEditorSlotIsolationTests</c>
    /// already pin that WRITE / DELETE / RENAME never cross a slot boundary, and
    /// <c>TileOverlayPersistenceWorldRoutingTests</c> pins that a <c>TileOverlayPersistence</c>
    /// instance always SAVES into the <see cref="WorldId"/> it was constructed with.
    /// None of them exercise the 3-arg <c>ApplyAllOverrides</c> overload that actually
    /// LOADS a world's overrides back onto the grid — grep confirms it has zero
    /// test call-sites anywhere in the suite before this file. This closes that:
    /// two slots save DIFFERENT content for the SAME zone name, and loading one
    /// slot must repaint only that slot's tile, never the other's.
    /// </summary>
    [TestFixture]
    public class TileOverlaySlotReadIsolationTests
    {
        private const string ZONE = "zone_test_slot_read_isolation";
        private static readonly WorldId SLOT_A = new WorldId(System.Guid.NewGuid(), "slot_read_a");
        private static readonly WorldId SLOT_B = new WorldId(System.Guid.NewGuid(), "slot_read_b");

        private static readonly string[] Candidates =
        {
            "wall", "floor", "floor_1", "floor_2", "floor_3", "floor_4", "floor_5", "dungeon_tunnel"
        };

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zoneGo;
        private ZoneManager _zones;
        private readonly List<Tile> _spawnedTiles = new List<Tile>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zoneGo = new GameObject("ZoneManager");
            _zones = _zoneGo.AddComponent<ZoneManager>();
            _zones.AddZone(ZONE, new Vector2Int(0, 0), editableInTileEditor: true);
        }

        [TearDown]
        public void TearDown()
        {
            TileOverlayPersistence.DeleteOverride(ZONE, SLOT_A);
            TileOverlayPersistence.DeleteOverride(ZONE, SLOT_B);

            foreach (var t in _spawnedTiles)
                if (t != null) Object.DestroyImmediate(t);
            _spawnedTiles.Clear();

            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            if (_zoneGo != null) Object.DestroyImmediate(_zoneGo);
            TileRegistry.Instance.Load(null);
            LogAssert.ignoreFailingMessages = false;
        }

        // OverlayLoader resolves tile names via Resources.Load, so a real,
        // pre-existing Resources/Tiles sprite is needed to prove the REPAINT
        // (not just the JSON) came from the right slot.
        private static string[] DiscoverTwoDistinctResourceTileNames()
        {
            var found = new List<string>();
            foreach (var name in Candidates)
            {
                if (Resources.Load<Sprite>("Tiles/" + name) != null)
                {
                    found.Add(name);
                    if (found.Count == 2) break;
                }
            }
            return found.ToArray();
        }

        private Tile MakeRegisteredTile(string resourceTileName)
        {
            var sprite = Resources.Load<Sprite>("Tiles/" + resourceTileName);
            Assert.IsNotNull(sprite, $"Expected to load sprite 'Tiles/{resourceTileName}'.");
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.name = resourceTileName;
            TileRegistry.Instance.Register(resourceTileName, tile);
            _spawnedTiles.Add(tile);
            return tile;
        }

        [Test]
        public void ApplyAllOverrides_ScopedToSlotA_RepaintsSlotAsTile_NotSlotBsTile()
        {
            var names = DiscoverTwoDistinctResourceTileNames();
            if (names.Length < 2)
                Assert.Inconclusive("Need two distinct resolvable Resources/Tiles sprites for this test.");
            string nameA = names[0];
            string nameB = names[1];

            var tileA = MakeRegisteredTile(nameA);
            var tileB = MakeRegisteredTile(nameB);

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            var cell = new Vector3Int(3, 3, 0);

            // Slot A saves nameA for this zone.
            ground.SetTile(cell, tileA);
            var pA = new TileOverlayPersistence(_zones, _grid, repository: null, worldId: SLOT_A);
            pA.MarkCellDirty(cell);
            Assert.IsTrue(pA.SaveZone(ZONE), "Slot A save must succeed.");

            // Slot B saves a DIFFERENT tile for the SAME zone name.
            ground.SetTile(cell, tileB);
            var pB = new TileOverlayPersistence(_zones, _grid, repository: null, worldId: SLOT_B);
            pB.MarkCellDirty(cell);
            Assert.IsTrue(pB.SaveZone(ZONE), "Slot B save must succeed.");

            // Simulate a fresh load: clear the live tilemap.
            ground.SetTile(cell, null);
            Assert.IsNull(ground.GetTile(cell), "Sanity: cell must be empty before re-apply.");

            // Load ONLY slot A's overrides.
            int applied = TileOverlayPersistence.ApplyAllOverrides(_grid, _zones, SLOT_A);
            Assert.AreEqual(1, applied, "Exactly the one zone saved under slot A must apply.");

            var restored = ground.GetTile(cell);
            Assert.IsNotNull(restored, "Slot A's tile must be repainted.");
            string restoredName = TileRegistry.Instance.GetName(restored);
            Assert.AreEqual(nameA, restoredName,
                "ApplyAllOverrides(worldId: SLOT_A) must repaint slot A's tile.");
            Assert.AreNotEqual(nameB, restoredName,
                "Slot B's content must NOT leak into a load scoped to slot A, even though " +
                "both slots saved an override for the identical zone name.");
        }

        [Test]
        public void ApplyAllOverrides_SlotWithNoFile_AppliesZero_DoesNotFallBackToAnotherSlot()
        {
            // Only SLOT_A ever saves for this zone. Collision is always emitted
            // (even empty) so the save succeeds without needing a resolvable tile.
            var pA = new TileOverlayPersistence(_zones, _grid, repository: null, worldId: SLOT_A);
            pA.MarkCellDirty(new Vector3Int(1, 1, 0));
            Assert.IsTrue(pA.SaveZone(ZONE), "Slot A save must succeed.");

            // SLOT_B never saved anything for this zone — its directory may not
            // even exist on disk yet.
            int applied = TileOverlayPersistence.ApplyAllOverrides(_grid, _zones, SLOT_B);

            Assert.AreEqual(0, applied,
                "A slot with no override directory on disk must apply ZERO zones — it must " +
                "not silently fall back to WorldId.Base or to another slot's files.");
        }
    }
}
