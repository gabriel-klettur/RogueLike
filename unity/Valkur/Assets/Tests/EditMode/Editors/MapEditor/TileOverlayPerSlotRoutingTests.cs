using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Per-slot tile-overlay routing contract for the Map Editor multi-map
    /// system. The implicit "default" slot keeps the legacy flat
    /// <c>persistentDataPath/MapOverrides/&lt;zone&gt;.overlay.json</c> layout
    /// (byte-compat with single-map saves), while every other slot nests its
    /// overlays under a per-slot subdirectory keyed by
    /// <see cref="MapEditorMapSlots.ResolveWorldId"/>. These tests pin both
    /// sides of that contract so the multi-map isolation never silently
    /// regresses to the old "all slots share one directory" behaviour.
    /// </summary>
    [TestFixture]
    public class TileOverlayPerSlotRoutingTests
    {
        private const string ZONE_NAME = "zone_perslot_test";
        private const string SLOT_FOREST = "forest_test_slot";
        private const string SLOT_DUNGEON = "dungeon_test_slot";

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zoneGo;
        private ZoneManager _zones;
        private Tile _floorTile;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _gridGo = new GameObject("WorldGridBuilder_PerSlot");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zoneGo = new GameObject("ZoneManager_PerSlot");
            _zones = _zoneGo.AddComponent<ZoneManager>();
            _zones.AddZone(ZONE_NAME, new Vector2Int(0, 0), editableInTileEditor: true);

            _floorTile = ScriptableObject.CreateInstance<Tile>();
            _floorTile.name = "test_perslot_floor";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            _floorTile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            _floorTile.sprite.name = "test_perslot_floor";
            TileRegistry.Instance.Register("test_perslot_floor", _floorTile);
        }

        [TearDown]
        public void TearDown()
        {
            // Wipe any files this test produced. Going through the per-world
            // helpers keeps the cleanup honest (and exercises Delete itself).
            TileOverlayPersistence.DeleteOverride(ZONE_NAME, WorldId.Base);
            TileOverlayPersistence.DeleteOverride(ZONE_NAME, MapEditorMapSlots.ResolveWorldId(SLOT_FOREST));
            TileOverlayPersistence.DeleteOverride(ZONE_NAME, MapEditorMapSlots.ResolveWorldId(SLOT_DUNGEON));

            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            if (_zoneGo != null) Object.DestroyImmediate(_zoneGo);
            if (_floorTile != null) Object.DestroyImmediate(_floorTile);
            TileRegistry.Instance.Load(null);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── ResolveWorldId contract ──────────────────────────────────────────

        [Test]
        public void ResolveWorldId_DefaultSlot_ReturnsBase()
        {
            var id = MapEditorMapSlots.ResolveWorldId(MapEditorMapSlots.DEFAULT_SLOT);
            Assert.IsTrue(id.IsBase, "Default slot must map to WorldId.Base for byte-compat with legacy saves.");
        }

        [Test]
        public void ResolveWorldId_EmptyOrNull_ReturnsBase()
        {
            Assert.IsTrue(MapEditorMapSlots.ResolveWorldId(null).IsBase);
            Assert.IsTrue(MapEditorMapSlots.ResolveWorldId(string.Empty).IsBase);
            Assert.IsTrue(MapEditorMapSlots.ResolveWorldId("   ").IsBase);
        }

        [Test]
        public void ResolveWorldId_NonDefaultSlot_HasMatchingSlugAndNonEmptyGuid()
        {
            var id = MapEditorMapSlots.ResolveWorldId(SLOT_FOREST);
            Assert.IsFalse(id.IsBase, "Non-default slot must NOT map to WorldId.Base.");
            Assert.AreEqual(SLOT_FOREST, id.Slug);
            Assert.AreNotEqual(System.Guid.Empty, id.Value, "Non-default slot must have a derived non-empty Guid.");
        }

        [Test]
        public void ResolveWorldId_IsDeterministic()
        {
            var first = MapEditorMapSlots.ResolveWorldId(SLOT_FOREST);
            var second = MapEditorMapSlots.ResolveWorldId(SLOT_FOREST);
            Assert.AreEqual(first, second,
                "Two resolutions of the same slot name must produce equal WorldIds " +
                "(stable across editor restarts).");
        }

        [Test]
        public void ResolveWorldId_CaseInsensitiveYieldsSameId()
        {
            var lower = MapEditorMapSlots.ResolveWorldId("forest_test_slot");
            var upper = MapEditorMapSlots.ResolveWorldId("FOREST_TEST_SLOT");
            Assert.AreEqual(lower, upper,
                "Slot resolution must be case-insensitive — otherwise a typo on " +
                "the active-slot pointer would fork the world id.");
        }

        [Test]
        public void ResolveWorldId_DifferentSlots_ProduceDifferentIds()
        {
            var forest = MapEditorMapSlots.ResolveWorldId(SLOT_FOREST);
            var dungeon = MapEditorMapSlots.ResolveWorldId(SLOT_DUNGEON);
            Assert.AreNotEqual(forest, dungeon,
                "Two distinct slot names MUST produce distinct WorldIds — " +
                "otherwise the multi-map isolation collapses.");
        }

        // ── Directory routing ────────────────────────────────────────────────

        [Test]
        public void OverrideDirectoryForWorld_Base_UsesFlatRoot()
        {
            string dir = TileOverlayPersistence.OverrideDirectoryForWorld(WorldId.Base);
            string expected = Path.Combine(Application.persistentDataPath, "MapOverrides");
            Assert.AreEqual(expected, dir,
                "WorldId.Base must keep the flat root for byte-compat with single-map saves.");
        }

        [Test]
        public void OverrideDirectoryForWorld_NonBase_NestedUnderSlug()
        {
            var id = MapEditorMapSlots.ResolveWorldId(SLOT_FOREST);
            string dir = TileOverlayPersistence.OverrideDirectoryForWorld(id);
            string expected = Path.Combine(Application.persistentDataPath, "MapOverrides", SLOT_FOREST);
            Assert.AreEqual(expected, dir,
                "Non-base slots must nest under their slug so each slot owns an " +
                "independent override layer on disk.");
        }

        // ── Write/read isolation ─────────────────────────────────────────────

        [Test]
        public void Write_ToSlotA_DoesNotLeakIntoSlotB()
        {
            var idForest = MapEditorMapSlots.ResolveWorldId(SLOT_FOREST);
            var idDungeon = MapEditorMapSlots.ResolveWorldId(SLOT_DUNGEON);

            // Paint a tile and save through a persistence instance bound to "forest".
            var groundTilemap = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            Assert.IsNotNull(groundTilemap, "Ground tilemap must exist for this test.");
            groundTilemap.SetTile(new Vector3Int(1, 1, 0), _floorTile);

            var pForest = new TileOverlayPersistence(_zones, _grid, repository: null, worldId: idForest);
            pForest.MarkCellDirty(new Vector3Int(1, 1, 0));
            int saved = pForest.SaveAllDirty();
            Assert.AreEqual(1, saved, "Forest save must produce exactly one zone overlay.");

            string forestPath = TileOverlayPersistence.OverridePathForZone(ZONE_NAME, idForest);
            string dungeonPath = TileOverlayPersistence.OverridePathForZone(ZONE_NAME, idDungeon);
            string basePath   = TileOverlayPersistence.OverridePathForZone(ZONE_NAME, WorldId.Base);

            Assert.IsTrue(File.Exists(forestPath), "Forest overlay must exist after save.");
            Assert.IsFalse(File.Exists(dungeonPath),
                "Saving in 'forest' MUST NOT create any file under the 'dungeon' slot — " +
                "this is the canonical multi-map isolation guarantee.");
            Assert.IsFalse(File.Exists(basePath),
                "Saving in a non-default slot MUST NOT bleed into the legacy flat root " +
                "used by the 'default' slot.");
        }

        [Test]
        public void Delete_PerSlot_DoesNotAffectOtherSlot()
        {
            var idForest = MapEditorMapSlots.ResolveWorldId(SLOT_FOREST);
            var idDungeon = MapEditorMapSlots.ResolveWorldId(SLOT_DUNGEON);

            // Seed both worlds with a small overlay file directly through the
            // path helper. Bypassing the full save round-trip here keeps the
            // test focused on Delete's routing.
            string forestPath = TileOverlayPersistence.OverridePathForZone(ZONE_NAME, idForest);
            string dungeonPath = TileOverlayPersistence.OverridePathForZone(ZONE_NAME, idDungeon);
            File.WriteAllText(forestPath, "{ \"layers\": {} }");
            File.WriteAllText(dungeonPath, "{ \"layers\": {} }");

            bool deleted = TileOverlayPersistence.DeleteOverride(ZONE_NAME, idForest);
            Assert.IsTrue(deleted, "DeleteOverride must report success when a file existed.");
            Assert.IsFalse(File.Exists(forestPath), "Forest file must be gone after delete.");
            Assert.IsTrue(File.Exists(dungeonPath),
                "Deleting in 'forest' MUST NOT touch the 'dungeon' slot's file.");
        }

        [Test]
        public void Rename_PerSlot_StaysInSameWorld()
        {
            const string OLD_NAME = "rename_old_zone";
            const string NEW_NAME = "rename_new_zone";

            var idForest = MapEditorMapSlots.ResolveWorldId(SLOT_FOREST);
            string oldForest = TileOverlayPersistence.OverridePathForZone(OLD_NAME, idForest);
            string newForest = TileOverlayPersistence.OverridePathForZone(NEW_NAME, idForest);
            string oldBase   = TileOverlayPersistence.OverridePathForZone(OLD_NAME, WorldId.Base);

            try
            {
                File.WriteAllText(oldForest, "{ \"layers\": {} }");

                bool renamed = TileOverlayPersistence.RenameOverride(OLD_NAME, NEW_NAME, idForest);
                Assert.IsTrue(renamed, "Rename must succeed when a source file exists.");
                Assert.IsFalse(File.Exists(oldForest), "Old forest file must be gone.");
                Assert.IsTrue(File.Exists(newForest), "New forest file must exist.");
                Assert.IsFalse(File.Exists(oldBase),
                    "Renaming inside 'forest' MUST NOT spill into the default world's root.");
            }
            finally
            {
                if (File.Exists(oldForest)) File.Delete(oldForest);
                if (File.Exists(newForest)) File.Delete(newForest);
                if (File.Exists(oldBase))   File.Delete(oldBase);
            }
        }

        // ── Backwards compatibility ─────────────────────────────────────────

        [Test]
        public void LegacyApi_NoWorldId_DefaultsToBase()
        {
            // Calling the legacy single-arg overloads must continue to behave
            // as if WorldId.Base was passed — every existing single-map save
            // depends on this.
            string legacyPath = TileOverlayPersistence.OverridePathForZone(ZONE_NAME);
            string explicitBasePath = TileOverlayPersistence.OverridePathForZone(ZONE_NAME, WorldId.Base);
            Assert.AreEqual(explicitBasePath, legacyPath,
                "The legacy no-WorldId overload must resolve to the same path as " +
                "the explicit WorldId.Base overload to preserve byte-compat.");
        }
    }
}
