using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;
using Valkur.Gameplay.World.Dungeon.Udemy.Runtime;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Runtime
{
    public class RoomPathfindingBridgeTests
    {
        private RoomPathfindingBridge _bridge;

        [SetUp] public void SetUp() => _bridge = new RoomPathfindingBridge();

        [Test]
        public void GetExtraPenalty_NoRoomsRegistered_ReturnsZero()
        {
            Assert.AreEqual(0, _bridge.GetExtraPenalty(new Vector2Int(5, 7)));
        }

        [Test]
        public void RegisterRoom_PreferredCellReturnsItsPenaltyAtWorldOffset()
        {
            var room = MakeRoom(id: "r1", lower: new Vector2Int(10, 20), upper: new Vector2Int(12, 22));

            // 3x3 matrix: default=40 except (1,1) which is preferred=1.
            var matrix = new int[3, 3];
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) matrix[x, y] = 40;
            matrix[1, 1] = 1;

            _bridge.RegisterRoom(room, matrix, defaultPenalty: 40);

            // Preferred cell at template (1,1) maps to world (11, 21).
            Assert.AreEqual(1, _bridge.GetExtraPenalty(new Vector2Int(11, 21)));
            // Default cell — no override.
            Assert.AreEqual(0, _bridge.GetExtraPenalty(new Vector2Int(10, 20)));
        }

        [Test]
        public void RegisterRoom_DefaultPenalty_IsNotIndexed()
        {
            var room = MakeRoom(id: "r1", lower: Vector2Int.zero, upper: new Vector2Int(2, 2));
            var matrix = new int[3, 3];
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) matrix[x, y] = 40;

            _bridge.RegisterRoom(room, matrix, defaultPenalty: 40);

            Assert.AreEqual(0, _bridge.RegisteredCellCount);
            Assert.AreEqual(0, _bridge.RegisteredRoomCount); // empty room contribution → not tracked
        }

        [Test]
        public void RegisterRoom_ZeroPenalty_IsNotIndexed_BecauseUnwalkableHandledByPhysics()
        {
            var room = MakeRoom(id: "r1", lower: Vector2Int.zero, upper: new Vector2Int(1, 1));
            var matrix = new int[2, 2] { { 0, 0 }, { 0, 0 } };
            _bridge.RegisterRoom(room, matrix, defaultPenalty: 40);

            Assert.AreEqual(0, _bridge.RegisteredCellCount);
        }

        [Test]
        public void UnregisterRoom_RemovesAllItsCells()
        {
            var room = MakeRoom(id: "r1", lower: Vector2Int.zero, upper: new Vector2Int(1, 1));
            var matrix = new int[2, 2] { { 1, 1 }, { 1, 1 } };
            _bridge.RegisterRoom(room, matrix, defaultPenalty: 40);
            Assert.AreEqual(4, _bridge.RegisteredCellCount);

            _bridge.UnregisterRoom(room);
            Assert.AreEqual(0, _bridge.RegisteredCellCount);
            Assert.AreEqual(0, _bridge.RegisteredRoomCount);
        }

        [Test]
        public void RegisterRoom_TwiceForSameRoomId_ReplacesRegistration()
        {
            var room = MakeRoom(id: "r1", lower: Vector2Int.zero, upper: new Vector2Int(0, 0));
            _bridge.RegisterRoom(room, new int[1, 1] { { 1 } }, defaultPenalty: 40);
            Assert.AreEqual(1, _bridge.RegisteredCellCount);

            // Second call replaces — must not double-count.
            _bridge.RegisterRoom(room, new int[1, 1] { { 1 } }, defaultPenalty: 40);
            Assert.AreEqual(1, _bridge.RegisteredCellCount);
        }

        [Test]
        public void Clear_DropsEverything()
        {
            var room = MakeRoom(id: "r1", lower: Vector2Int.zero, upper: new Vector2Int(0, 0));
            _bridge.RegisterRoom(room, new int[1, 1] { { 1 } }, defaultPenalty: 40);
            _bridge.Clear();
            Assert.AreEqual(0, _bridge.RegisteredCellCount);
            Assert.AreEqual(0, _bridge.RegisteredRoomCount);
        }

        [Test]
        public void RegisterRoom_NullArgs_NoOp()
        {
            Assert.DoesNotThrow(() => _bridge.RegisterRoom(null, new int[1, 1], 40));
            Assert.DoesNotThrow(() => _bridge.RegisterRoom(MakeRoom("r", Vector2Int.zero, Vector2Int.zero), null, 40));
        }

        private static Room MakeRoom(string id, Vector2Int lower, Vector2Int upper)
        {
            return new Room
            {
                id = id,
                lowerBounds = lower,
                upperBounds = upper,
            };
        }
    }
}
