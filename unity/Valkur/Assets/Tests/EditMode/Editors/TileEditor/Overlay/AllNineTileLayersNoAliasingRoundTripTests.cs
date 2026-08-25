using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// <c>TileOverlayPersistence.BuildOverlayJson</c> (private) iterates all 9
    /// <see cref="TilemapLayerSetup.TilemapLayer"/> values and, for each one, calls
    /// <see cref="WorldGridBuilder.GetTilemap"/> to fetch the backing <c>Tilemap</c>.
    /// Every existing persistence suite paints at most Ground + Collision at once —
    /// an accidental alias where two different <c>TilemapLayer</c> enum values
    /// resolve to the SAME <c>Tilemap</c> component would only surface with real
    /// content in more than two layers simultaneously, which nothing exercises.
    ///
    /// This file fills that specific hole: paint all 9 layers with distinct,
    /// individually-registered tile names at the same cell, save through the real
    /// <see cref="TileOverlayPersistence"/> write path, and assert each of the 9
    /// JSON layer keys carries its OWN tile name — not a neighbour's.
    ///
    /// It also proves the "Collision is always emitted, even empty"
    /// guarantee (<c>TileOverlayPersistenceTests</c>) survives when the other 8
    /// layers carry real painted content — every existing test for that guarantee
    /// only ever populates Collision (± Ground).
    /// </summary>
    [TestFixture]
    public class AllNineTileLayersNoAliasingRoundTripTests
    {
        private const string ZONE = "zone_test_nine_layers";
        private static readonly Vector2Int OFFSET = new Vector2Int(200, 100);

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zoneGo;
        private ZoneManager _zones;
        private TileOverlayPersistence _persistence;
        private readonly List<Tile> _tiles = new List<Tile>();
        private readonly List<string> _names = new List<string>();

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

            foreach (TilemapLayerSetup.TilemapLayer layer in
                     System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer)))
            {
                string name = "nine_layer_" + layer;
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = name;
                var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
                tile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
                tile.sprite.name = name;
                TileRegistry.Instance.Register(name, tile);
                _tiles.Add(tile);
                _names.Add(name);
            }
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            TileOverlayPersistence.DeleteOverride(ZONE);
            Object.DestroyImmediate(_gridGo);
            Object.DestroyImmediate(_zoneGo);
            foreach (var t in _tiles)
                if (t != null) Object.DestroyImmediate(t);
            _tiles.Clear();
            _names.Clear();
            TileRegistry.Instance.Load(null);
        }

        [Test]
        public void SaveZone_NineLayersPaintedAtSameCell_EachLayerKeepsItsOwnTileName()
        {
            var cell = new Vector3Int(OFFSET.x + 5, OFFSET.y + 5, 0);
            var layers = (TilemapLayerSetup.TilemapLayer[])
                System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer));

            for (int i = 0; i < layers.Length; i++)
            {
                var tilemap = _grid.GetTilemap(layers[i]);
                Assert.IsNotNull(tilemap, $"WorldGridBuilder must expose a tilemap for {layers[i]}.");
                tilemap.SetTile(cell, _tiles[i]);
            }
            _persistence.MarkCellDirty(cell);
            Assert.IsTrue(_persistence.SaveZone(ZONE), "Save must succeed.");

            var root = OverlayLoader.ParseOverlay(TileOverlayPersistence.OverridePathForZone(ZONE));
            Assert.IsNotNull(root, "Overlay must parse.");
            var layersDict = root["layers"] as Dictionary<string, object>;
            Assert.IsNotNull(layersDict, "Overlay must contain a 'layers' dictionary.");

            int h = _zones.ZoneHeightTiles;
            int localX = cell.x - OFFSET.x;
            int row = h - 1 - (cell.y - OFFSET.y); // row 0 = top of zone convention

            for (int i = 0; i < layers.Length; i++)
            {
                string key = layers[i].ToString();
                Assert.IsTrue(layersDict.ContainsKey(key), $"Layer '{key}' missing from saved JSON.");

                var rows = layersDict[key] as List<object>;
                Assert.IsNotNull(rows, $"Layer '{key}' must serialize as a matrix of rows.");
                var rowList = rows[row] as List<object>;
                Assert.IsNotNull(rowList, $"Layer '{key}' row {row} must be a list.");
                string cellValue = rowList[localX] as string;

                Assert.AreEqual(_names[i], cellValue,
                    $"Layer '{key}' must carry its OWN tile name at the shared cell — if " +
                    "WorldGridBuilder.GetTilemap ever aliases two TilemapLayer values to the " +
                    "same Tilemap component, this assertion fails with one layer's name " +
                    "bleeding into another's.");
            }
        }

        [Test]
        public void SaveZone_EightOtherLayersPaintedWithRealContent_CollisionStillAlwaysEmittedEmpty()
        {
            var layers = (TilemapLayerSetup.TilemapLayer[])
                System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer));

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == TilemapLayerSetup.TilemapLayer.Collision) continue;

                var tilemap = _grid.GetTilemap(layers[i]);
                var cell = new Vector3Int(OFFSET.x + i, OFFSET.y + i, 0);
                tilemap.SetTile(cell, _tiles[i]);
                _persistence.MarkCellDirty(cell);
            }
            Assert.IsTrue(_persistence.SaveZone(ZONE), "Save must succeed.");

            string json = File.ReadAllText(TileOverlayPersistence.OverridePathForZone(ZONE));
            var root = MiniJsonRuntime.Deserialize(json) as Dictionary<string, object>;
            Assert.IsNotNull(root, "Overlay must parse.");
            var layersDict = root["layers"] as Dictionary<string, object>;
            Assert.IsNotNull(layersDict, "Overlay must contain a 'layers' dictionary.");

            Assert.IsTrue(layersDict.ContainsKey("Collision"),
                "Collision must still be emitted (empty) even when the other 8 layers " +
                "carry real painted content — the loader's clearLayerRegion only fires for " +
                "layers present in the JSON, so an omitted-because-empty Collision key would " +
                "let base-map colliders silently reappear on reload.");

            var rows = layersDict["Collision"] as List<object>;
            Assert.IsNotNull(rows, "Collision must serialize as a matrix of rows.");
            Assert.AreEqual(_zones.ZoneHeightTiles, rows.Count,
                "Empty Collision matrix must still have full zone-height rows.");
            foreach (var rowObj in rows)
            {
                var row = rowObj as List<object>;
                Assert.AreEqual(_zones.ZoneWidthTiles, row.Count,
                    "Every row of the empty Collision matrix must have full zone-width columns.");
                foreach (var c in row)
                    Assert.AreEqual(string.Empty, (c as string) ?? string.Empty,
                        "Every Collision cell must be empty — none of the other 8 layers' " +
                        "content must leak into it.");
            }
        }
    }
}
