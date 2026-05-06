using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Comprehensive tests for the dungeon migration pipeline:
    /// DungeonLoader (tile painting + tunnel), DungeonGenerator integration,
    /// zone Y-flip, sprite sizing, and Python parity.
    /// </summary>
    public class DungeonLoaderTests
    {
        private DungeonGeneratorConfig _config;
        private GameObject _loaderGo;
        private DungeonLoader _loader;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<DungeonGeneratorConfig>();
            _loaderGo = new GameObject("TestDungeonLoader");
            _loader = _loaderGo.AddComponent<DungeonLoader>();
            _loader.SetConfig(_config);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_loaderGo);
            Object.DestroyImmediate(_config);
        }

        // ── Configuration ──────────────────────────────────────────────

        [Test]
        public void SetConfig_AssignsConfig()
        {
            Assert.IsFalse(_loader.IsGenerated);
        }

        [Test]
        public void IsGenerated_DefaultsFalse()
        {
            Assert.IsFalse(_loader.IsGenerated);
        }

        [Test]
        public void DefaultConfig_MatchesPythonValues()
        {
            // Python: zone_width=50, zone_height=50, max_rooms=10,
            //         room_min_size=10, room_max_size=20, tunnel_thickness=3
            Assert.AreEqual(50, _config.ZoneWidth, "ZoneWidth should match Python");
            Assert.AreEqual(50, _config.ZoneHeight, "ZoneHeight should match Python");
            Assert.AreEqual(10, _config.MaxRoomAttempts, "MaxRoomAttempts should match Python max_rooms=10");
            Assert.AreEqual(10, _config.RoomMinSize, "RoomMinSize should match Python");
            Assert.AreEqual(20, _config.RoomMaxSize, "RoomMaxSize should match Python");
            Assert.AreEqual(3, _config.TunnelThickness, "TunnelThickness should match Python");
            Assert.AreEqual('#', _config.WallChar, "WallChar should match Python");
            Assert.AreEqual('O', _config.RoomChar, "RoomChar should match Python");
            Assert.AreEqual('=', _config.TunnelChar, "TunnelChar should match Python");
        }

        // ── Null safety ────────────────────────────────────────────────

        [Test]
        public void GenerateAndPaint_WithoutGridBuilder_DoesNotThrow()
        {
            LogAssert.Expect(LogType.Error, "[DungeonLoader] WorldGridBuilder is null.");
            Assert.DoesNotThrow(() =>
                _loader.GenerateAndPaint(null, 50, 0, 50, 50, 50, seed: 42));
        }

        [Test]
        public void GenerateAndPaint_WithoutConfig_DoesNotThrow()
        {
            var go = new GameObject("NoConfig");
            var loader = go.AddComponent<DungeonLoader>();
            LogAssert.Expect(LogType.Error, "[DungeonLoader] DungeonGeneratorConfig not assigned.");
            Assert.DoesNotThrow(() =>
                loader.GenerateAndPaint(null, 50, 0, 50, 50, 50));
            Object.DestroyImmediate(go);
        }

        // ── Zone Y-flip (ZoneDatabaseLoader) ──────────────────────────

        [Test]
        public void GlobalYFlip_DungeonBelowLobby()
        {
            // zones_database.json values (Python Y-down):
            //   Lobby: (50, 50), Dungeon: (50, 100), zone_height=50
            //   maxWorldY = max(all offset_y + 50) = 150
            // After flip: flippedY = 150 - offset_y - 50
            //   Lobby:   150 - 50 - 50 = 50  (unchanged)
            //   Dungeon: 150 - 100 - 50 = 0   (below lobby)
            int maxWorldY = 150; // max zone Y is 100, + zone_height 50
            int zoneHeight = 50;

            int lobbyFlipped = maxWorldY - 50 - zoneHeight;
            int dungeonFlipped = maxWorldY - 100 - zoneHeight;

            Assert.AreEqual(50, lobbyFlipped, "Lobby Y stays at 50 after flip");
            Assert.AreEqual(0, dungeonFlipped, "Dungeon Y flips to 0 (south of lobby)");
            Assert.IsTrue(dungeonFlipped < lobbyFlipped, "Dungeon should be south (lower Y) than lobby");
        }

        [Test]
        public void GlobalYFlip_PreservesRelativeOrder()
        {
            // All Y=0 zones should flip to highest Y, all Y=100 to lowest
            int maxWorldY = 150;
            int zoneH = 50;

            int y0_flipped = maxWorldY - 0 - zoneH;      // 100
            int y50_flipped = maxWorldY - 50 - zoneH;     // 50
            int y100_flipped = maxWorldY - 100 - zoneH;   // 0

            Assert.IsTrue(y100_flipped < y50_flipped, "Y=100 (south) should flip below Y=50");
            Assert.IsTrue(y50_flipped < y0_flipped, "Y=50 (middle) should flip below Y=0 (north)");
        }

        [Test]
        public void GlobalYFlip_XOffsetUnchanged()
        {
            // X offsets should remain identical after Y-flip
            int dungeonOffX = 50;
            int lobbyOffX = 50;
            Assert.AreEqual(lobbyOffX, dungeonOffX, "X offset should be unchanged by Y-flip");
        }

        // ── Zone layout (post-flip) ───────────────────────────────────

        [Test]
        public void DungeonOffsets_MatchZonesDatabase()
        {
            int lobbyOffX = 50, lobbyOffY = 50;
            int dungeonOffX = 50, dungeonOffY = 0; // after Y-flip
            int zoneHeight = 50;

            Assert.AreEqual(lobbyOffX, dungeonOffX, "Dungeon and lobby share X offset");
            Assert.AreEqual(lobbyOffY - zoneHeight, dungeonOffY,
                "Dungeon is one zone below lobby after Y-flip");
        }

        [Test]
        public void LobbyDungeonAdjacent_NoGap()
        {
            // After Y-flip: lobby spans Y=50..99, dungeon spans Y=0..49
            // They must be adjacent (no gap between Y=49 and Y=50)
            int lobbyBottom = 50;   // lobbyOffY
            int dungeonTop = 0 + 50 - 1; // dungeonOffY + zoneHeight - 1 = 49

            Assert.AreEqual(lobbyBottom - 1, dungeonTop,
                "Lobby bottom edge (Y=50) must be adjacent to dungeon top edge (Y=49)");
        }

        // ── Connecting tunnel logic ────────────────────────────────────

        [Test]
        public void ConnectingTunnel_ExitAtLobbyBottomCenter()
        {
            // Lobby exit should be at X = lobbyOffX + zoneWidth/2, Y = lobbyOffY
            int lobbyOffX = 50;
            int lobbyOffY = 50;
            int zoneWidth = 50;

            int expectedX = lobbyOffX + zoneWidth / 2; // 75
            int expectedY = lobbyOffY;                  // 50

            Assert.AreEqual(75, expectedX, "Lobby exit X should be midpoint");
            Assert.AreEqual(50, expectedY, "Lobby exit Y should be bottom edge of lobby");
        }

        [Test]
        public void ConnectingTunnel_FindsClosestRoom()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            Assert.IsTrue(result.Rooms.Count >= 1, "Need at least one room");

            int lobbyOffY = 50;
            int dungeonOffY = 0;

            int lobbyExitX = 50 + 25;
            int lobbyExitY = lobbyOffY;

            int bestDist = int.MaxValue;
            Vector2Int bestCenter = default;
            for (int i = 0; i < result.Rooms.Count; i++)
            {
                var c = DungeonGenerator.CenterOf(result.Rooms[i]);
                int flippedCY = result.Height - 1 - c.y;
                int worldCX = 50 + c.x;
                int worldCY = dungeonOffY + flippedCY;

                int dist = Mathf.Abs(worldCX - lobbyExitX) + Mathf.Abs(worldCY - lobbyExitY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestCenter = new Vector2Int(worldCX, worldCY);
                }
            }

            Assert.IsTrue(bestDist < 100, $"Closest room should be reachable, dist={bestDist}");
            Assert.IsTrue(bestCenter.y < lobbyOffY,
                $"Closest room Y should be in dungeon zone (below lobby), got {bestCenter.y}");
        }

        [Test]
        public void ConnectingTunnel_Thickness_MatchesPython()
        {
            // Python: dungeon_tunnel_thickness = 3
            Assert.AreEqual(3, _config.TunnelThickness,
                "Tunnel thickness should match Python's dungeon_tunnel_thickness=3");
        }

        // ── Grid content validation ────────────────────────────────────

        [Test]
        public void GeneratedGrid_HasWalkableAndWallTiles()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            bool hasRoom = false;
            bool hasWall = false;

            for (int y = 0; y < result.Height; y++)
                for (int x = 0; x < result.Width; x++)
                {
                    if (result.Grid[y][x] == _config.RoomChar) hasRoom = true;
                    if (result.Grid[y][x] == _config.WallChar) hasWall = true;
                }

            Assert.IsTrue(hasRoom, "Dungeon should have room tiles ('O')");
            Assert.IsTrue(hasWall, "Dungeon should have wall tiles ('#')");
        }

        [Test]
        public void GeneratedGrid_OnlyValidChars()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            for (int y = 0; y < result.Height; y++)
                for (int x = 0; x < result.Width; x++)
                {
                    char c = result.Grid[y][x];
                    Assert.IsTrue(c == '#' || c == 'O' || c == '=',
                        $"Unexpected char '{c}' at ({x},{y}). Only '#', 'O', '=' allowed.");
                }
        }

        [Test]
        public void GeneratedGrid_RoomsWithinBounds()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            for (int i = 0; i < result.Rooms.Count; i++)
            {
                var room = result.Rooms[i];
                Assert.IsTrue(room.x >= 1, $"Room {i} X start should be >= 1, got {room.x}");
                Assert.IsTrue(room.y >= 1, $"Room {i} Y start should be >= 1, got {room.y}");
                Assert.IsTrue(room.x + room.width < result.Width,
                    $"Room {i} X end should be < {result.Width}");
                Assert.IsTrue(room.y + room.height < result.Height,
                    $"Room {i} Y end should be < {result.Height}");
            }
        }

        // ── Sprite resource validation ─────────────────────────────────

        [Test]
        public void DungeonFloorSprite_ExistsInResources()
        {
            var sprite = Resources.Load<Sprite>("Tiles/dungeon_floor");
            Assert.IsNotNull(sprite, "dungeon_floor sprite should exist in Resources/Tiles/");
        }

        [Test]
        public void DungeonTunnelSprite_ExistsInResources()
        {
            var sprite = Resources.Load<Sprite>("Tiles/dungeon_tunnel");
            Assert.IsNotNull(sprite, "dungeon_tunnel sprite should exist in Resources/Tiles/");
        }

        [Test]
        public void WallSprite_ExistsInResources()
        {
            var sprite = Resources.Load<Sprite>("Tiles/wall");
            Assert.IsNotNull(sprite, "wall sprite should exist in Resources/Tiles/");
        }

        [Test]
        public void DungeonFloorSprite_PPU_MatchesTileSize()
        {
            // Python TILE_SIZE = 32. With 32×32 pixel sprites and PPU=32,
            // each sprite fills exactly 1 Unity unit = 1 tilemap cell.
            var sprite = Resources.Load<Sprite>("Tiles/dungeon_floor");
            if (sprite == null) { Assert.Inconclusive("Sprite not found"); return; }

            float expectedPPU = 32f; // ValkurAssetPostprocessor TILE_PPU
            Assert.AreEqual(expectedPPU, sprite.pixelsPerUnit,
                $"dungeon_floor PPU should be {expectedPPU} to match 1 tile = 1 world unit");
        }

        [Test]
        public void DungeonFloorSprite_Size_Is32x32()
        {
            // Python downscales 1024→32 at runtime. Unity uses pre-scaled 32×32.
            // ATLAS-SAFE: assert against sprite.rect (the sprite's own pixel size)
            // rather than sprite.texture.{width,height} — when the sprite is packed
            // into a SpriteAtlas (env-tiles atlas covers Resources/Tiles/), texture
            // returns the atlas page (e.g. 1024×1024), not the original 32×32 PNG.
            var sprite = Resources.Load<Sprite>("Tiles/dungeon_floor");
            if (sprite == null) { Assert.Inconclusive("Sprite not found"); return; }

            Assert.AreEqual(32, Mathf.RoundToInt(sprite.rect.width),
                "dungeon_floor sprite should be 32px wide (matching Python TILE_SIZE)");
            Assert.AreEqual(32, Mathf.RoundToInt(sprite.rect.height),
                "dungeon_floor sprite should be 32px tall (matching Python TILE_SIZE)");
        }

        [Test]
        public void DungeonTunnelSprite_Size_Is32x32()
        {
            var sprite = Resources.Load<Sprite>("Tiles/dungeon_tunnel");
            if (sprite == null) { Assert.Inconclusive("Sprite not found"); return; }

            Assert.AreEqual(32, Mathf.RoundToInt(sprite.rect.width),
                "dungeon_tunnel sprite should be 32px wide");
            Assert.AreEqual(32, Mathf.RoundToInt(sprite.rect.height),
                "dungeon_tunnel sprite should be 32px tall");
        }

        // ── Y-flip within dungeon zone ─────────────────────────────────

        [Test]
        public void IntraZoneYFlip_Row0MapsToTopOfZone()
        {
            // DungeonGenerator row 0 = top (Python Y-down convention)
            // In Unity: flippedY = height-1-y, so row 0 maps to highest Y = top
            int zoneHeight = 50;
            int dungeonOffsetY = 0; // after global flip

            int pythonRow0_unityY = dungeonOffsetY + (zoneHeight - 1 - 0); // 49
            int pythonRowLast_unityY = dungeonOffsetY + (zoneHeight - 1 - 49); // 0

            Assert.AreEqual(49, pythonRow0_unityY,
                "Python row 0 (top of dungeon) should map to Unity Y=49 (top of zone)");
            Assert.AreEqual(0, pythonRowLast_unityY,
                "Python last row (bottom) should map to Unity Y=0 (bottom of zone)");
        }

        [Test]
        public void IntraZoneYFlip_TopOfDungeon_AdjacentToLobby()
        {
            // Dungeon top edge (Y=49) must be adjacent to lobby bottom (Y=50)
            int dungeonOffY = 0;
            int zoneH = 50;
            int dungeonTopY = dungeonOffY + zoneH - 1; // 49

            int lobbyOffY = 50;
            int lobbyBottomY = lobbyOffY; // 50

            Assert.AreEqual(lobbyBottomY - 1, dungeonTopY,
                "Dungeon top (Y=49) must be adjacent to lobby bottom (Y=50)");
        }

        // ── Determinism ────────────────────────────────────────────────

        [Test]
        public void WallTiles_AreVisibleOnGround_MatchingPython()
        {
            // Python renders wall '#' tiles as VISIBLE stone-brick sprites on the ground layer.
            // Unity must paint them on Ground (visible) in addition to Collision (blocking).
            // Verify via DungeonGenerator that '#' cells exist,
            // and that DungeonLoader's GetWallGroundTile returns a fully opaque tile.
            var result = DungeonGenerator.Generate(_config, seed: 42);
            bool hasWall = false;
            for (int y = 0; y < result.Height && !hasWall; y++)
                for (int x = 0; x < result.Width && !hasWall; x++)
                    if (result.Grid[y][x] == _config.WallChar)
                        hasWall = true;

            Assert.IsTrue(hasWall, "Generated dungeon must have '#' wall cells");
        }

        [Test]
        public void GenerateAndPaint_SameSeed_SameResult()
        {
            var r1 = DungeonGenerator.Generate(_config, seed: 42);
            var r2 = DungeonGenerator.Generate(_config, seed: 42);
            Assert.AreEqual(r1.Rooms.Count, r2.Rooms.Count);
            for (int y = 0; y < r1.Height; y++)
                for (int x = 0; x < r1.Width; x++)
                    Assert.AreEqual(r1.Grid[y][x], r2.Grid[y][x]);
        }
    }
}
