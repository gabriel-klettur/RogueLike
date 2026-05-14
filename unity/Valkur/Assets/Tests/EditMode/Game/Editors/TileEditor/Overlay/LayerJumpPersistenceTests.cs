using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.Editors.TileEditor.Overlay
{
    /// <summary>
    /// Round-trip + legacy migration coverage for the <c>layerJumps</c> matrix
    /// that M1.8 adds to the overlay JSON schema. Mirrors the structure of
    /// <c>CollisionTagPersistenceTests</c> because the two share the same
    /// "additive parallel matrix" architecture.
    /// </summary>
    [TestFixture]
    public class LayerJumpPersistenceTests
    {
        private const string ZONE = "zone_test_layer_jumps";

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zoneGo;
        private ZoneManager _zones;
        private TileOverlayPersistence _persistence;
        private LayerJumpMap _jumpMap;
        private Tile _wallTile;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zoneGo = new GameObject("ZoneManager");
            _zones = _zoneGo.AddComponent<ZoneManager>();
            _zones.AddZone(ZONE, new Vector2Int(0, 0), editableInTileEditor: true);

            _persistence = new TileOverlayPersistence(_zones, _grid);
            _jumpMap = new LayerJumpMap();
            _persistence.LayerJumpMap = _jumpMap;

            // Persistence needs at least one painted cell to flush a zone, but we
            // paint into the Ground tilemap (not Collision) so collisionTags stays
            // empty AND any test that asserts "layerJumps emitted" doesn't get
            // false-positive matches from other matrices.
            _wallTile = ScriptableObject.CreateInstance<Tile>();
            _wallTile.name = "test_ground";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            _wallTile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            _wallTile.sprite.name = "test_ground";
            TileRegistry.Instance.Register("test_ground", _wallTile);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            TileOverlayPersistence.DeleteOverride(ZONE);
            Object.DestroyImmediate(_gridGo);
            Object.DestroyImmediate(_zoneGo);
            Object.DestroyImmediate(_wallTile);
            TileRegistry.Instance.Load(null);
        }

        private void PaintGroundDirty()
        {
            // Ensure the zone is marked dirty so SaveZone actually writes the JSON.
            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            ground.SetTile(new Vector3Int(0, 0, 0), _wallTile);
            _persistence.MarkCellDirty(new Vector3Int(0, 0, 0));
        }

        [Test]
        public void RoundTrip_PreservesEveryAuthoredJump()
        {
            _jumpMap.Set(new Vector2Int(2, 3), "0");
            _jumpMap.Set(new Vector2Int(4, 5), "4");
            _jumpMap.Set(new Vector2Int(7, 7), "8");
            PaintGroundDirty();

            Assert.IsTrue(_persistence.SaveZone(ZONE));

            string path = TileOverlayPersistence.OverridePathForZone(ZONE);
            Assert.IsTrue(File.Exists(path));

            string json = File.ReadAllText(path);
            StringAssert.Contains("\"layerJumps\"", json,
                "Save must emit the layerJumps matrix when the map has entries.");

            var roundTrip = new LayerJumpMap();
            int written = OverlayLoader.ApplyLayerJumpsFromPath(path, roundTrip, 0, 0);
            Assert.AreEqual(3, written);
            Assert.AreEqual("0", roundTrip.Get(new Vector2Int(2, 3)));
            Assert.AreEqual("4", roundTrip.Get(new Vector2Int(4, 5)));
            Assert.AreEqual("8", roundTrip.Get(new Vector2Int(7, 7)));
            Assert.AreEqual(string.Empty, roundTrip.Get(new Vector2Int(0, 0)));
        }

        /// <summary>
        /// THE migration guard. Pre-feature JSON has no <c>layerJumps</c> field;
        /// loading must leave the map empty so the runtime trigger system
        /// short-circuits on the first frame (Count == 0). Cero gameplay change.
        /// </summary>
        [Test]
        public void LegacyOverlayWithoutField_LoadsAsEmpty()
        {
            PaintGroundDirty();
            Assert.IsTrue(_persistence.SaveZone(ZONE));

            string path = TileOverlayPersistence.OverridePathForZone(ZONE);
            string json = File.ReadAllText(path);
            StringAssert.DoesNotContain("\"layerJumps\"", json,
                "Empty jump map must NOT emit the layerJumps field — keeps legacy " +
                "JSONs byte-identical.");

            var fresh = new LayerJumpMap();
            int written = OverlayLoader.ApplyLayerJumpsFromPath(path, fresh, 0, 0);
            Assert.AreEqual(0, written);
            Assert.AreEqual(0, fresh.Count);
        }

        [Test]
        public void SaveWithEmptyJumpMap_DoesNotEmitField()
        {
            PaintGroundDirty();
            _persistence.SaveZone(ZONE);

            string json = File.ReadAllText(TileOverlayPersistence.OverridePathForZone(ZONE));
            StringAssert.DoesNotContain("\"layerJumps\"", json);
        }

        [Test]
        public void ApplyFromPath_MissingFile_ReturnsZeroAndDoesNotThrow()
        {
            string nonexistent = Path.Combine(Application.persistentDataPath,
                "MapOverrides", "definitely_does_not_exist.overlay.json");
            int written = OverlayLoader.ApplyLayerJumpsFromPath(nonexistent, _jumpMap, 0, 0);
            Assert.AreEqual(0, written);
        }
    }
}
