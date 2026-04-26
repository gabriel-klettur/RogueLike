using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    public class DungeonGeneratorTests
    {
        private DungeonGeneratorConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<DungeonGeneratorConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void Generate_ReturnsCorrectDimensions()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            Assert.AreEqual(_config.ZoneHeight, result.Height);
            Assert.AreEqual(_config.ZoneWidth, result.Width);
            Assert.AreEqual(result.Height, result.Grid.Length);
            for (int y = 0; y < result.Height; y++)
                Assert.AreEqual(result.Width, result.Grid[y].Length);
        }

        [Test]
        public void Generate_ProducesAtLeastOneRoom()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            Assert.IsTrue(result.Rooms.Count >= 1, "Should generate at least one room");
        }

        [Test]
        public void Generate_RoomTilesAreFloorOrTunnel()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            foreach (var room in result.Rooms)
            {
                for (int y = room.y; y < room.y + room.height; y++)
                    for (int x = room.x; x < room.x + room.width; x++)
                    {
                        char c = result.Grid[y][x];
                        Assert.IsTrue(c == _config.RoomChar || c == _config.TunnelChar,
                            $"Room tile at ({x},{y}) should be '{_config.RoomChar}' or '{_config.TunnelChar}' but was '{c}'");
                    }
            }
        }

        [Test]
        public void Generate_BorderIsWalls()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            // Top and bottom rows should be walls (rooms start at y>=1)
            for (int x = 0; x < result.Width; x++)
            {
                Assert.AreEqual(_config.WallChar, result.Grid[0][x], "Top border should be wall");
                Assert.AreEqual(_config.WallChar, result.Grid[result.Height - 1][x], "Bottom border should be wall");
            }
        }

        [Test]
        public void Generate_SameSeedProducesSameResult()
        {
            var r1 = DungeonGenerator.Generate(_config, seed: 123);
            var r2 = DungeonGenerator.Generate(_config, seed: 123);
            Assert.AreEqual(r1.Rooms.Count, r2.Rooms.Count);
            for (int y = 0; y < r1.Height; y++)
                for (int x = 0; x < r1.Width; x++)
                    Assert.AreEqual(r1.Grid[y][x], r2.Grid[y][x],
                        $"Grids should match at ({x},{y})");
        }

        [Test]
        public void Generate_DifferentSeedsProduceDifferentResults()
        {
            var r1 = DungeonGenerator.Generate(_config, seed: 1);
            var r2 = DungeonGenerator.Generate(_config, seed: 999);
            // At minimum room count or positions should differ
            bool differs = r1.Rooms.Count != r2.Rooms.Count;
            if (!differs && r1.Rooms.Count > 0)
            {
                differs = r1.Rooms[0].x != r2.Rooms[0].x ||
                          r1.Rooms[0].y != r2.Rooms[0].y;
            }
            Assert.IsTrue(differs, "Different seeds should produce different dungeons");
        }

        [Test]
        public void Generate_RoomsDoNotOverlap()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            for (int i = 0; i < result.Rooms.Count; i++)
                for (int j = i + 1; j < result.Rooms.Count; j++)
                    Assert.IsFalse(DungeonGenerator.Intersect(result.Rooms[i], result.Rooms[j]),
                        $"Rooms {i} and {j} should not overlap");
        }

        [Test]
        public void Generate_HasTunnelTiles()
        {
            var result = DungeonGenerator.Generate(_config, seed: 42);
            if (result.Rooms.Count < 2) return;

            bool hasTunnel = false;
            for (int y = 0; y < result.Height && !hasTunnel; y++)
                for (int x = 0; x < result.Width && !hasTunnel; x++)
                    if (result.Grid[y][x] == _config.TunnelChar)
                        hasTunnel = true;

            Assert.IsTrue(hasTunnel, "Multi-room dungeon should have tunnel tiles");
        }

        [Test]
        public void Generate_AvoidZoneExcludesRooms()
        {
            var avoid = new RectInt(0, 0, 25, 25);
            var result = DungeonGenerator.Generate(_config, seed: 42, avoidZone: avoid);
            foreach (var room in result.Rooms)
                Assert.IsFalse(DungeonGenerator.Intersect(avoid, room),
                    "No room should overlap the avoid zone");
        }

        [Test]
        public void Intersect_OverlappingRects_ReturnsTrue()
        {
            var a = new RectInt(0, 0, 10, 10);
            var b = new RectInt(5, 5, 10, 10);
            Assert.IsTrue(DungeonGenerator.Intersect(a, b));
        }

        [Test]
        public void Intersect_NonOverlapping_ReturnsFalse()
        {
            var a = new RectInt(0, 0, 5, 5);
            var b = new RectInt(10, 10, 5, 5);
            Assert.IsFalse(DungeonGenerator.Intersect(a, b));
        }

        [Test]
        public void CenterOf_ReturnsCorrectCenter()
        {
            var r = new RectInt(10, 20, 6, 8);
            var c = DungeonGenerator.CenterOf(r);
            Assert.AreEqual(13, c.x); // 10 + 6/2
            Assert.AreEqual(24, c.y); // 20 + 8/2
        }

        [Test]
        public void HorizTunnel_CarvesInGrid()
        {
            char[][] grid = new char[10][];
            for (int y = 0; y < 10; y++)
            {
                grid[y] = new char[20];
                for (int x = 0; x < 20; x++)
                    grid[y][x] = '#';
            }

            DungeonGenerator.HorizTunnel(grid, 2, 8, 5, 3, '=');

            // Center row (y=5) and adjacent rows (y=4, y=6) should have tunnel
            for (int x = 2; x <= 8; x++)
            {
                Assert.AreEqual('=', grid[5][x], $"Center row at x={x}");
                Assert.AreEqual('=', grid[4][x], $"Top row at x={x}");
                Assert.AreEqual('=', grid[6][x], $"Bottom row at x={x}");
            }
        }

        [Test]
        public void VertTunnel_CarvesInGrid()
        {
            char[][] grid = new char[20][];
            for (int y = 0; y < 20; y++)
            {
                grid[y] = new char[10];
                for (int x = 0; x < 10; x++)
                    grid[y][x] = '#';
            }

            DungeonGenerator.VertTunnel(grid, 3, 12, 5, 3, '=');

            // Center col (x=5) and adjacent cols (x=4, x=6) should have tunnel
            for (int y = 3; y <= 12; y++)
            {
                Assert.AreEqual('=', grid[y][5], $"Center col at y={y}");
                Assert.AreEqual('=', grid[y][4], $"Left col at y={y}");
                Assert.AreEqual('=', grid[y][6], $"Right col at y={y}");
            }
        }
    }
}
