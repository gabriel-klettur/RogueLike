using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Builder
{
    /// <summary>
    /// End-to-end algorithm tests with deterministic seeds. Each test wires up
    /// a tiny fixture (3-room linear graph) and verifies that the builder
    /// reaches a valid no-overlap layout. These tests guard the core
    /// double-retry + doorway-matching loop.
    /// </summary>
    public class DungeonBuilderDeterministicTests
    {
        private DungeonFixture _fx;

        [SetUp] public void SetUp() => _fx = DungeonFixture.MakeLinearEntranceCorridorChamber();
        [TearDown] public void TearDown() => _fx.Dispose();

        [Test]
        public void Generate_ValidLinearGraph_PlacesAllThreeRoomsWithNoOverlap()
        {
            var builder = DungeonBuilder.FromSeed(_fx.Config, _fx.NodeTypeList, seed: 42);
            var result = builder.GenerateDungeon(new DungeonBuildRequest
            {
                Level = _fx.Level,
                NodeTypeList = _fx.NodeTypeList,
                Config = _fx.Config,
                Seed = 42,
            });

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(3, result.RoomsByNodeId.Count);
            AssertNoOverlaps(result);
        }

        [Test]
        public void Generate_DifferentSeeds_StillSucceedForLinearGraph()
        {
            // Linear graphs are tolerant of seed choice; success rate must be 100%
            // because each child has only one viable doorway pairing.
            for (int seed = 1; seed <= 5; seed++)
            {
                var builder = DungeonBuilder.FromSeed(_fx.Config, _fx.NodeTypeList, seed);
                var result = builder.GenerateDungeon(new DungeonBuildRequest
                {
                    Level = _fx.Level,
                    NodeTypeList = _fx.NodeTypeList,
                    Config = _fx.Config,
                    Seed = seed,
                });
                Assert.IsTrue(result.Success, $"Seed {seed} failed: {result.FailureReason}");
            }
        }

        [Test]
        public void Generate_NullRequest_FailsCleanly()
        {
            var builder = new DungeonBuilder(_fx.Config, _fx.NodeTypeList);
            var result = builder.GenerateDungeon(null);
            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.FailureReason);
        }

        [Test]
        public void Generate_NullLevel_FailsCleanly()
        {
            var builder = new DungeonBuilder(_fx.Config, _fx.NodeTypeList);
            var result = builder.GenerateDungeon(new DungeonBuildRequest());
            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.FailureReason);
        }

        [Test]
        public void Generate_LevelWithNoGraphs_FailsCleanly()
        {
            _fx.Level.roomNodeGraphList.Clear();
            var builder = new DungeonBuilder(_fx.Config, _fx.NodeTypeList);
            var result = builder.GenerateDungeon(new DungeonBuildRequest
            {
                Level = _fx.Level,
                NodeTypeList = _fx.NodeTypeList,
                Config = _fx.Config,
            });
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Generate_LevelMissingCorridorTemplate_FailsAfterRetries()
        {
            // Drop the only corridor template — the builder cannot place the corridor child.
            _fx.Level.roomTemplateList.Remove(_fx.CorridorNSTemplate);

            // Tiny retry budget so the test stays fast.
            _fx.Config.maxDungeonBuildAttempts = 2;
            _fx.Config.maxDungeonRebuildAttemptsForRoomGraph = 5;

            var builder = DungeonBuilder.FromSeed(_fx.Config, _fx.NodeTypeList, seed: 1);
            var result = builder.GenerateDungeon(new DungeonBuildRequest
            {
                Level = _fx.Level,
                NodeTypeList = _fx.NodeTypeList,
                Config = _fx.Config,
            });
            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.FailureReason);
        }

        [Test]
        public void Generate_DungeonHas3RoomsAndAllArePositioned()
        {
            var builder = DungeonBuilder.FromSeed(_fx.Config, _fx.NodeTypeList, seed: 7);
            var result = builder.GenerateDungeon(new DungeonBuildRequest
            {
                Level = _fx.Level,
                NodeTypeList = _fx.NodeTypeList,
                Config = _fx.Config,
            });
            Assert.IsTrue(result.Success);
            foreach (var room in result.RoomsByNodeId.Values)
                Assert.IsTrue(room.isPositioned, $"Room {room.id} not positioned.");
        }

        private static void AssertNoOverlaps(DungeonBuildResult result)
        {
            var rooms = new List<Room>(result.RoomsByNodeId.Values);
            for (int i = 0; i < rooms.Count; i++)
            for (int j = i + 1; j < rooms.Count; j++)
            {
                Assert.IsFalse(
                    DoorwayMatcher.RoomsOverlap(
                        rooms[i].lowerBounds, rooms[i].upperBounds,
                        rooms[j].lowerBounds, rooms[j].upperBounds),
                    $"Rooms {rooms[i].id} and {rooms[j].id} overlap.");
            }
        }
    }
}
