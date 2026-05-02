using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Worlds;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Phase 1.5 end-to-end smoke test for the secondary "test_world" demo
    /// dimension. Proves that:
    ///
    ///   1. The TestWorld.asset + TestWorldConfig.asset assets exist on disk
    ///      and are well-formed (slug + chunk size + display name).
    ///   2. The companion StreamingAssets/Worlds/test_world/Maps/ JSON files
    ///      exist (zones_database.json + test_zone.overlay.json) and the
    ///      overlay is fully covered by the same tile name.
    ///   3. After registering the WorldManager and activating the test world,
    ///      ZoneDatabaseLoader populates the ZoneManager with exactly one
    ///      zone, and OverlayLoader paints the Ground tilemap with that
    ///      tile across every cell of the 50x50 zone footprint.
    ///
    /// If any of these breaks, the chain that takes a designer-authored
    /// WorldDescriptor + a per-world StreamingAssets directory and turns it
    /// into a visible scene has regressed somewhere between the loaders and
    /// the WorldManager wiring.
    /// </summary>
    [TestFixture]
    public class TestWorldEndToEndTests
    {
        private const string ConfigAssetPath = "Assets/_Project/Data/Worlds/TestWorldConfig.asset";
        private const string DescAssetPath   = "Assets/_Project/Data/Worlds/TestWorld.asset";
        private const string ExpectedTile    = "dungeon_floor";
        private const int    ZoneSide        = 50;

        private GameObject _gridGo, _zonesGo;
        private WorldGridBuilder _grid;
        private ZoneManager _zones;
        private ZoneDatabaseLoader _dbLoader;
        private WorldLoader _worldLoader;
        private Tile _testTile;
        private bool _registeredTile;

        // ── Asset / data integrity ──────────────────────────────────────────────

        [Test]
        public void TestWorld_Asset_Exists_And_IsWellFormed()
        {
            var descriptor = AssetDatabase.LoadAssetAtPath<WorldDescriptor>(DescAssetPath);
            Assert.IsNotNull(descriptor,
                $"TestWorld descriptor must exist at {DescAssetPath}. " +
                "Run 'Valkur/World/Create or Refresh Test World Assets' to (re)build it.");

            Assert.AreEqual("test_world", descriptor.Slug);
            Assert.AreEqual(new Vector2Int(25, 25), descriptor.DefaultSpawnTile,
                "Spawn tile must land in the centre of the zone so the player " +
                "lands on a painted cell rather than at the (0,0) corner.");
            Assert.IsNotNull(descriptor.Config,
                "TestWorld must reference a WorldConfig asset.");
            Assert.AreEqual("test_world", descriptor.Config.DimensionSlug);
        }

        [Test]
        public void TestWorld_StreamingAssets_AreShippedAndConsistent()
        {
            string mapsDir = Path.Combine(Application.streamingAssetsPath,
                "Worlds", "test_world", "Maps");
            Assert.IsTrue(Directory.Exists(mapsDir),
                $"StreamingAssets/Worlds/test_world/Maps must exist; got {mapsDir}.");

            string dbPath = Path.Combine(mapsDir, "zones_database.json");
            string overlayPath = Path.Combine(mapsDir, "test_zone.overlay.json");
            Assert.IsTrue(File.Exists(dbPath),    $"zones_database.json missing at {dbPath}");
            Assert.IsTrue(File.Exists(overlayPath), $"test_zone.overlay.json missing at {overlayPath}");

            string overlay = File.ReadAllText(overlayPath);
            int tileHits = 0, idx = 0;
            while ((idx = overlay.IndexOf("\"" + ExpectedTile + "\"", idx)) >= 0)
            { tileHits++; idx += ExpectedTile.Length + 2; }
            // 50x50 = 2500 cells, every one should be the test tile.
            Assert.AreEqual(ZoneSide * ZoneSide, tileHits,
                $"Overlay must contain {ZoneSide * ZoneSide} occurrences of \"{ExpectedTile}\"; got {tileHits}.");
        }

        // ── Runtime end-to-end ──────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ServiceLocator.Unregister<IWorldManager>();

            // Real grid + zone manager so OverlayLoader writes into a real Tilemap.
            _gridGo = new GameObject("E2EGrid");
            _grid   = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zonesGo = new GameObject("E2EZones");
            _zones   = _zonesGo.AddComponent<ZoneManager>();

            _dbLoader = _zonesGo.AddComponent<ZoneDatabaseLoader>();
            SetField(_dbLoader, "_zoneManager", _zones);
            SetField(_dbLoader, "_autoLoad",     false);

            _worldLoader = _zonesGo.AddComponent<WorldLoader>();
            SetField(_worldLoader, "_databaseLoader", _dbLoader);
            SetField(_worldLoader, "_gridBuilder",    _grid);
            SetField(_worldLoader, "_autoLoad",       false);

            // Register the dungeon_floor tile in the runtime TileRegistry so
            // OverlayLoader can resolve it from the JSON cell strings.
            var sprite = Resources.Load<Sprite>("Tiles/" + ExpectedTile);
            if (sprite != null)
            {
                _testTile = ScriptableObject.CreateInstance<Tile>();
                _testTile.sprite = sprite;
                _testTile.name = ExpectedTile;
                Valkur.Gameplay.TileEditor.TileRegistry.Instance.Register(ExpectedTile, _testTile);
                _registeredTile = true;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo  != null) Object.DestroyImmediate(_gridGo);
            if (_zonesGo != null) Object.DestroyImmediate(_zonesGo);
            if (_testTile != null) Object.DestroyImmediate(_testTile);
            if (_registeredTile)
                Valkur.Gameplay.TileEditor.TileRegistry.Instance.Load(null);
            ServiceLocator.Unregister<IWorldManager>();
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void Loading_TestWorld_PaintsExpectedTileAcrossEntireZone()
        {
            if (!_registeredTile)
                Assert.Inconclusive($"Resources/Tiles/{ExpectedTile}.png not available — cannot exercise paint.");

            // 1) Wire WorldManager and activate the test world.
            var descriptor = AssetDatabase.LoadAssetAtPath<WorldDescriptor>(DescAssetPath);
            Assert.IsNotNull(descriptor, "Test world descriptor must be present.");

            var manager = new WorldManager();
            ServiceLocator.Register<IWorldManager>(manager);
            manager.LoadAndActivateAsync(descriptor).GetAwaiter().GetResult();
            Assert.AreEqual("test_world", manager.Active.WorldId.Slug,
                "Active world must be the test world after activation.");

            // 2) Run the database + world loaders. ZoneDatabaseLoader.LoadDatabase
            //    consults ServiceLocator.Get<IWorldManager>().Active.WorldId, so
            //    the per-world StreamingAssets directory is the one being read.
            _dbLoader.LoadDatabase();
            Assert.IsTrue(_zones.TryGetZone("test_zone", out _),
                "ZoneManager must contain the single 'test_zone' from the test " +
                "world's zones_database.json.");

            _worldLoader.LoadFullWorld();
            Assert.AreEqual(1, _worldLoader.OverlaysLoaded,
                "Exactly one overlay must be loaded (the test world has one zone).");

            // 3) Verify the entire 50x50 footprint has the expected tile painted
            //    on the Ground tilemap. This proves the chain runs end-to-end.
            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            Assert.IsNotNull(ground, "Ground tilemap must exist.");

            int painted = 0;
            int wrong   = 0;
            for (int x = 0; x < ZoneSide; x++)
            for (int y = 0; y < ZoneSide; y++)
            {
                var t = ground.GetTile(new Vector3Int(x, y, 0));
                if (t == null) continue;
                if (t.name == ExpectedTile) painted++;
                else                        wrong++;
            }

            Assert.AreEqual(ZoneSide * ZoneSide, painted,
                $"Every one of the {ZoneSide * ZoneSide} cells must be painted " +
                $"with '{ExpectedTile}'. Painted: {painted}, wrong tile: {wrong}.");
            Assert.AreEqual(0, wrong, "No cell should hold a tile other than the expected one.");
        }

        // ── Reflection helpers ──────────────────────────────────────────────────

        private static void SetField(object obj, string name, object value)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { f.SetValue(obj, value); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Field '{name}' not found on {obj.GetType().Name}.");
        }
    }
}
