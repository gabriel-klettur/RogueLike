using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Tests.EditMode.Game.WorldGen
{
    /// <summary>
    /// <see cref="SeedWorldLiveStreamer"/> against a real tilemap grid: zones near the player are
    /// painted, far ones are cleared, and an edited zone comes back from its file. Driven through
    /// <c>Sync</c> directly — Edit Mode runs no Update.
    /// </summary>
    public class SeedWorldLiveStreamerTests
    {
        private string _root;
        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _streamerGo;
        private SeedWorldLiveStreamer _streamer;
        private SeedWorldLiveWorld _world;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _root = Path.Combine(Path.GetTempPath(), "valkur_seedworld_stream_" + Guid.NewGuid().ToString("N"));

            _gridGo = new GameObject("SeedWorldStreamerGrid");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
            var request = SeedWorldBakeRequest.ForTest("seed_stream", _root);
            var settings = new WorldGenSettings { seed = 99, widthTiles = 250, heightTiles = 150, riverCount = 4, townCount = 1 };
            Assert.IsTrue(SeedWorldBaker.BakeLive(settings, request, palette, null, null).Succeeded);
            _world = SeedWorldLiveWorld.TryOpen(request, palette, out string error);
            Assert.IsNotNull(_world, error);

            _streamerGo = new GameObject("SeedWorldStreamer");
            _streamer = _streamerGo.AddComponent<SeedWorldLiveStreamer>();
            _streamer.AttachForTests(_grid, _world);
        }

        [TearDown]
        public void TearDown()
        {
            // Object.Destroy is an ERROR in Edit Mode.
            if (_streamerGo != null) UnityEngine.Object.DestroyImmediate(_streamerGo);
            if (_gridGo != null) UnityEngine.Object.DestroyImmediate(_gridGo);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            LogAssert.ignoreFailingMessages = false;
        }

        private Tilemap Ground => _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);

        private Vector2 CentreOfZone(int zx, int zy)
        {
            var o = _world.Plan.ZoneOffset(zx, zy);
            return new Vector2(o.x + 25.5f, o.y + 25.5f);
        }

        private bool ZoneHasGround(int zx, int zy)
        {
            var o = _world.Plan.ZoneOffset(zx, zy);
            return Ground.GetTile(new Vector3Int(o.x + 10, o.y + 10, 0)) != null
                && Ground.GetTile(new Vector3Int(o.x + 49, o.y + 49, 0)) != null;
        }

        [Test]
        public void ThePlayersZone_AndItsNeighbours_ArePainted()
        {
            _streamer.SyncAll(CentreOfZone(1, 1));

            for (int zy = 0; zy <= 2; zy++)
                for (int zx = 0; zx <= 2; zx++)
                    Assert.IsTrue(ZoneHasGround(zx, zy), $"zone ({zx},{zy}) should be painted");
            Assert.IsFalse(ZoneHasGround(4, 1), "a zone three away was painted for nothing");
            Assert.AreEqual(9, _streamer.LoadedZoneCount);
        }

        [Test]
        public void WalkingAway_ClearsTheFarZones_AndKeepsTheNearOnes()
        {
            _streamer.SyncAll(CentreOfZone(0, 1));
            Assert.IsTrue(ZoneHasGround(0, 1));

            _streamer.SyncAll(CentreOfZone(4, 1));

            Assert.IsFalse(ZoneHasGround(0, 1), "four zones behind the player must have been dropped");
            Assert.IsTrue(ZoneHasGround(4, 1));
            Assert.IsTrue(ZoneHasGround(3, 1));
            Assert.Greater(_streamer.ZonesUnloaded, 0);
        }

        /// <summary>
        /// An editor pans the camera away from the player. Both places get ground; the zones BETWEEN
        /// them do not — a bounding box over the two would paint a whole world's worth of zones for
        /// one long pan.
        /// </summary>
        [Test]
        public void ACameraPannedAway_GetsItsOwnZones_WithoutTheOnesInBetween()
        {
            var cameraCentre = CentreOfZone(4, 1);
            var view = new Rect(cameraCentre.x - 16f, cameraCentre.y - 8f, 32f, 16f);
            for (int i = 0; i < 64; i++) _streamer.Sync(CentreOfZone(0, 1), view);

            Assert.IsTrue(ZoneHasGround(0, 1), "the player's zone");
            Assert.IsTrue(ZoneHasGround(4, 1), "the zone the camera looks at");
            Assert.IsFalse(ZoneHasGround(2, 1), "the zones between player and camera are not in view and not near the player");
            Assert.IsFalse(_streamer.IsLoaded(new Vector2Int(2, 1)));
        }

        [Test]
        public void TheSameZone_IsPaintedTheSameAfterComingBack()
        {
            _streamer.SyncAll(CentreOfZone(0, 1));
            var o = _world.Plan.ZoneOffset(0, 1);
            var before = Ground.GetTilesBlock(new BoundsInt(o.x, o.y, 0, 50, 50, 1));

            _streamer.SyncAll(CentreOfZone(4, 1));
            _streamer.SyncAll(CentreOfZone(0, 1));
            var after = Ground.GetTilesBlock(new BoundsInt(o.x, o.y, 0, 50, 50, 1));

            CollectionAssert.AreEqual(before, after, "a revisited zone must be regenerated identically");
        }

        /// <summary>
        /// A generated zone that was never saved has no overlay file, so the Tile editor's auto-brush
        /// had no corner terrain there at all and drew hard edges against the generated ground. The
        /// streamer hands it the vertex terrain of every zone it paints.
        /// </summary>
        [Test]
        public void APaintedZone_GivesTheAutoBrushItsVertexTerrain()
        {
            var map = new TerrainMap();
            _streamer.UseTerrainMapForTests(map);

            _streamer.SyncAll(CentreOfZone(1, 1));

            var zone = _world.GenerateZone(1, 1);
            var o = _world.Plan.ZoneOffset(1, 1);
            int z = zone.Size;
            int checkedVertices = 0;
            for (int vy = 0; vy <= z; vy += 7)
                for (int vx = 0; vx <= z; vx += 7)
                {
                    Assert.AreEqual(zone.Terrains[vy * (z + 1) + vx], map.GetTerrain(new Vector2Int(o.x + vx, o.y + vy)),
                        $"vertex ({vx},{vy}) of zone (1,1)");
                    checkedVertices++;
                }
            Assert.Greater(checkedVertices, 0);
            Assert.AreEqual(_streamer.LoadedZoneCount, _streamer.ZonesWithTerrains);
        }

        [Test]
        public void DroppingAZone_ForgetsItsTerrain_ButKeepsTheEdgeAPaintedNeighbourShares()
        {
            var map = new TerrainMap();
            _streamer.UseTerrainMapForTests(map);
            _streamer.SyncAll(CentreOfZone(1, 1));

            // Four zones on: 0 and 1 are dropped, 2 stays painted (the unload ring keeps it).
            _streamer.SyncAll(CentreOfZone(4, 1));
            Assert.IsFalse(_streamer.IsLoaded(new Vector2Int(1, 1)), "Sanity: zone (1,1) was dropped.");
            Assert.IsTrue(_streamer.IsLoaded(new Vector2Int(2, 1)), "Sanity: zone (2,1) is still painted.");

            var o1 = _world.Plan.ZoneOffset(1, 1);
            int z = _world.ZoneSize;
            Assert.IsNull(map.GetTerrain(new Vector2Int(o1.x + 20, o1.y + 20)),
                "A dropped zone's interior must leave the auto-brush map, or it answers for the next map painted there.");
            Assert.IsNull(map.GetTerrain(new Vector2Int(_world.Plan.ZoneOffset(0, 1).x + 20, o1.y + 20)));

            var shared = new Vector2Int(o1.x + z, o1.y + 20);
            Assert.AreEqual(_world.GenerateZone(2, 1).Terrains[20 * (z + 1)], map.GetTerrain(shared),
                "The edge a dropped zone shares with a painted one is also that zone's, and its last column of cells reads it.");
        }

        [Test]
        public void AnEditedZone_IsPaintedFromItsFile()
        {
            var sample = _world.GenerateZone(1, 1);
            string tileName = sample.Ground[0];
            Assert.IsFalse(string.IsNullOrEmpty(tileName));

            // One row, one tile: the file's own content, not the generator's.
            File.WriteAllText(_world.OverlayPathFor(1, 1), "{\"layers\":{\"Ground\":[[\"" + tileName + "\"]]}}");

            _streamer.SyncAll(CentreOfZone(1, 1));
            var o = _world.Plan.ZoneOffset(1, 1);
            // Row 0 of a one-row overlay is the zone's bottom row.
            Assert.IsNotNull(Ground.GetTile(new Vector3Int(o.x, o.y, 0)));
            Assert.IsNull(Ground.GetTile(new Vector3Int(o.x + 30, o.y + 30, 0)),
                "an edited zone must not be topped up with generated ground");
            Assert.AreEqual(1, _streamer.ZonesFromDisk);
        }
    }
}
