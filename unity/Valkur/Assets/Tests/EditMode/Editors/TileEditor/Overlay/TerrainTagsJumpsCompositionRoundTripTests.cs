using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// Composition round-trip for the overlay schema's three OPTIONAL parallel
    /// matrices — <c>terrains</c>, <c>collisionTags</c>, <c>layerJumps</c> — through
    /// the REAL production pair: <see cref="TileOverlayPersistence"/> (write) and
    /// <see cref="OverlayLoader"/>'s <c>Apply*</c> methods (read).
    ///
    /// Why this file exists despite <c>CollisionTagPersistenceTests</c> and
    /// <c>LayerJumpPersistenceTests</c> already covering their own matrices:
    /// <c>TerrainMap</c> never got the same treatment. <c>TerrainOverlayLoaderTests</c>
    /// only exercises the LOAD half — it hand-writes the overlay JSON literal or
    /// calls <see cref="TerrainMap.BuildMatrix"/> directly, but never routes through
    /// <see cref="TileOverlayPersistence.TerrainMap"/> + <see cref="TileOverlayPersistence.SaveZone"/>,
    /// the actual write path F8's auto-tile tool uses. The gap matters because
    /// <see cref="OverlayLoader.ApplyTerrains"/> re-implements the row-flip
    /// (row 0 = top of zone) INLINE rather than calling <see cref="TerrainMap.LoadMatrix"/>
    /// (which has zero production call-sites — verified: it is only ever invoked by
    /// its own isolated unit tests). Two independently-written row-flip
    /// implementations for the same on-disk field can drift without anyone noticing
    /// unless the exact write→read pair used in production is exercised together.
    ///
    /// This file also proves the three matrices survive together, at a non-zero
    /// zone offset (offset math bugs love to hide behind a (0,0) test — see
    /// <c>SPAWNER_COORDINATE_SPACE_DRIFT</c>), with an asymmetric top-vs-bottom
    /// pattern so a row-flip regression produces a swapped value instead of a
    /// coincidental pass.
    /// </summary>
    [TestFixture]
    public class TerrainTagsJumpsCompositionRoundTripTests
    {
        private const string ZONE = "zone_test_four_matrices";

        // Matches the real world's 50-tile zone spacing (zones_database.json:
        // zone_width_tiles/zone_height_tiles = 50) rather than (0,0), so a bug
        // that only manifests when world-space != zone-local-space can't hide.
        private static readonly Vector2Int OFFSET = new Vector2Int(150, 50);

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zoneGo;
        private ZoneManager _zones;
        private TileOverlayPersistence _persistence;
        private TerrainMap _terrainMap;
        private CollisionTagMap _tagMap;
        private LayerJumpMap _jumpMap;
        private Tile _groundTile;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zoneGo = new GameObject("ZoneManager");
            _zones = _zoneGo.AddComponent<ZoneManager>();
            _zones.AddZone(ZONE, OFFSET, editableInTileEditor: true);

            _persistence = new TileOverlayPersistence(_zones, _grid);
            _terrainMap = new TerrainMap();
            _tagMap = new CollisionTagMap();
            _jumpMap = new LayerJumpMap();
            _persistence.TerrainMap = _terrainMap;
            _persistence.CollisionTagMap = _tagMap;
            _persistence.LayerJumpMap = _jumpMap;

            _groundTile = ScriptableObject.CreateInstance<Tile>();
            _groundTile.name = "four_matrix_ground";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            _groundTile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            _groundTile.sprite.name = "four_matrix_ground";
            TileRegistry.Instance.Register("four_matrix_ground", _groundTile);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            TileOverlayPersistence.DeleteOverride(ZONE);
            Object.DestroyImmediate(_gridGo);
            Object.DestroyImmediate(_zoneGo);
            Object.DestroyImmediate(_groundTile);
            TileRegistry.Instance.Load(null);
        }

        // ── Terrain write path (the specific, previously-untested half) ─────

        [Test]
        public void RoundTrip_Terrain_PreservesEveryAuthoredCell_ThroughRealSaveZone()
        {
            _terrainMap.SetTerrain(new Vector2Int(OFFSET.x + 2, OFFSET.y + 3), "grass");
            _terrainMap.SetTerrain(new Vector2Int(OFFSET.x + 4, OFFSET.y + 5), "dirt");
            _terrainMap.SetTerrain(new Vector2Int(OFFSET.x + 7, OFFSET.y + 7), "sand");

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            ground.SetTile(new Vector3Int(OFFSET.x + 2, OFFSET.y + 3, 0), _groundTile);
            _persistence.MarkCellDirty(new Vector3Int(OFFSET.x + 2, OFFSET.y + 3, 0));
            Assert.IsTrue(_persistence.SaveZone(ZONE), "Save must succeed.");

            string path = TileOverlayPersistence.OverridePathForZone(ZONE);
            Assert.IsTrue(File.Exists(path), "Save must produce the overlay file.");
            string json = File.ReadAllText(path);
            StringAssert.Contains("\"terrains\"", json,
                "Save must emit the terrains matrix when the terrain map has entries — " +
                "this field was previously only ever hand-written by tests, never produced " +
                "by the real TileOverlayPersistence write path.");

            var roundTrip = new TerrainMap();
            int written = OverlayLoader.ApplyTerrainsFromPath(path, roundTrip, OFFSET.x, OFFSET.y);
            Assert.AreEqual(3, written, "All three authored terrain cells must load.");

            Assert.AreEqual("grass", roundTrip.GetTerrain(new Vector2Int(OFFSET.x + 2, OFFSET.y + 3)));
            Assert.AreEqual("dirt",  roundTrip.GetTerrain(new Vector2Int(OFFSET.x + 4, OFFSET.y + 5)));
            Assert.AreEqual("sand",  roundTrip.GetTerrain(new Vector2Int(OFFSET.x + 7, OFFSET.y + 7)));
            Assert.IsNull(roundTrip.GetTerrain(new Vector2Int(OFFSET.x, OFFSET.y)),
                "An untouched cell must not gain a spurious terrain entry.");
        }

        [Test]
        public void SaveWithEmptyTerrainMap_DoesNotEmitField()
        {
            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            ground.SetTile(new Vector3Int(OFFSET.x, OFFSET.y, 0), _groundTile);
            _persistence.MarkCellDirty(new Vector3Int(OFFSET.x, OFFSET.y, 0));
            Assert.IsTrue(_persistence.SaveZone(ZONE));

            string json = File.ReadAllText(TileOverlayPersistence.OverridePathForZone(ZONE));
            StringAssert.DoesNotContain("\"terrains\"", json,
                "Empty terrain map must not emit the field — keeps legacy-shaped JSON diff-clean.");
        }

        // ── All three metadata matrices + tiles, together, asymmetric, offset ──

        [Test]
        public void RoundTrip_AllFourMatricesTogether_PreservesOrientationAndWorldCoordinates()
        {
            int w = _zones.ZoneWidthTiles;
            int h = _zones.ZoneHeightTiles;
            // Row 0 of every matrix = the TOP of the zone = the highest Unity Y
            // (origin.y + h - 1). Painting distinct values at the top and bottom
            // row turns a row-flip regression into a swapped-value failure
            // instead of a silent pass.
            var topRow = new Vector2Int(OFFSET.x, OFFSET.y + h - 1);
            var bottomRow = new Vector2Int(OFFSET.x, OFFSET.y);
            var untouched = new Vector2Int(OFFSET.x + w / 2, OFFSET.y + h / 2);

            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            ground.SetTile(new Vector3Int(topRow.x, topRow.y, 0), _groundTile);
            ground.SetTile(new Vector3Int(bottomRow.x, bottomRow.y, 0), _groundTile);

            _terrainMap.SetTerrain(topRow, "grass_top");
            _terrainMap.SetTerrain(bottomRow, "dirt_bottom");
            _tagMap.Set(topRow, "2");
            _tagMap.Set(bottomRow, "6");
            _jumpMap.Set(topRow, "1");
            _jumpMap.Set(bottomRow, "8");

            _persistence.MarkCellDirty(new Vector3Int(topRow.x, topRow.y, 0));
            Assert.IsTrue(_persistence.SaveZone(ZONE), "Combined save must succeed.");

            string path = TileOverlayPersistence.OverridePathForZone(ZONE);
            var root = OverlayLoader.ParseOverlay(path);
            Assert.IsNotNull(root, "Combined overlay must parse.");

            var freshTerrain = new TerrainMap();
            var freshTags = new CollisionTagMap();
            var freshJumps = new LayerJumpMap();
            OverlayLoader.ApplyTerrains(root, freshTerrain, OFFSET.x, OFFSET.y);
            OverlayLoader.ApplyCollisionTags(root, freshTags, OFFSET.x, OFFSET.y);
            OverlayLoader.ApplyLayerJumps(root, freshJumps, OFFSET.x, OFFSET.y);

            Assert.AreEqual("grass_top",   freshTerrain.GetTerrain(topRow), "Terrain top row.");
            Assert.AreEqual("dirt_bottom", freshTerrain.GetTerrain(bottomRow), "Terrain bottom row.");
            Assert.AreEqual("2", freshTags.Get(topRow), "CollisionTags top row.");
            Assert.AreEqual("6", freshTags.Get(bottomRow), "CollisionTags bottom row.");
            Assert.AreEqual("1", freshJumps.Get(topRow), "LayerJumps top row.");
            Assert.AreEqual("8", freshJumps.Get(bottomRow), "LayerJumps bottom row.");

            // An untouched middle cell must resolve to each matrix's own default
            // (null / Wildcard / empty) rather than picking up a neighbour's value.
            Assert.IsNull(freshTerrain.GetTerrain(untouched));
            Assert.AreEqual(CollisionTagMap.Wildcard, freshTags.Get(untouched));
            Assert.AreEqual(string.Empty, freshJumps.Get(untouched));

            // Cross-matrix isolation: the SAME cell's three authored values must
            // stay distinct after the round trip — a shared row/col indexing bug
            // between the three Apply* readers could otherwise make one matrix
            // silently echo another's content at the same coordinate.
            Assert.AreNotEqual(freshTags.Get(topRow), freshJumps.Get(topRow));
        }
    }
}
