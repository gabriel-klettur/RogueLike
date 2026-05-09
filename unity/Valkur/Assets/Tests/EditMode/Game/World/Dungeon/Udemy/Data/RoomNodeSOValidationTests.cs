using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Data
{
    /// <summary>
    /// Covers each of the 11 rules in <see cref="RoomNodeSO.IsChildRoomValid"/>.
    /// Every test wires up a tiny three-node graph (parent + child + extra)
    /// and toggles only the field under test.
    /// </summary>
    public class RoomNodeSOValidationTests
    {
        private RoomNodeGraphSO _graph;
        private RoomNodeTypeSO _entranceType;
        private RoomNodeTypeSO _corridorType;
        private RoomNodeTypeSO _roomType;
        private RoomNodeTypeSO _bossType;
        private RoomNodeTypeSO _noneType;

        [SetUp]
        public void SetUp()
        {
            _graph = ScriptableObject.CreateInstance<RoomNodeGraphSO>();

            _entranceType = MakeType("Entrance", entrance: true);
            _corridorType = MakeType("Corridor", corridor: true);
            _roomType = MakeType("Room");
            _bossType = MakeType("Boss", boss: true);
            _noneType = MakeType("None", none: true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_graph);
            Object.DestroyImmediate(_entranceType);
            Object.DestroyImmediate(_corridorType);
            Object.DestroyImmediate(_roomType);
            Object.DestroyImmediate(_bossType);
            Object.DestroyImmediate(_noneType);
        }

        // Rule 1 — only one connected boss room is permitted in the whole graph.
        [Test]
        public void Rule1_RejectsSecondConnectedBoss()
        {
            var corridor = MakeNode(_corridorType);
            var boss1 = MakeNode(_bossType);
            var corridor2 = MakeNode(_corridorType);
            var boss2 = MakeNode(_bossType);

            // Connect corridor → boss1 (first boss connection)
            corridor.AddChildRoomNodeIDToRoomNode(boss1.id);
            boss1.AddParentRoomNodeIDToRoomNode(corridor.id);

            // Try to connect corridor2 → boss2 (second boss → must reject)
            Assert.IsFalse(corridor2.IsChildRoomValid(boss2.id));
        }

        // Rule 2 — a None-typed node can never be a child.
        [Test]
        public void Rule2_RejectsNoneTypedChild()
        {
            var parent = MakeNode(_corridorType);
            var noneChild = MakeNode(_noneType);
            Assert.IsFalse(parent.IsChildRoomValid(noneChild.id));
        }

        // Rule 3 — duplicate child id rejected.
        [Test]
        public void Rule3_RejectsDuplicateChild()
        {
            var parent = MakeNode(_corridorType);
            var child = MakeNode(_roomType);
            parent.AddChildRoomNodeIDToRoomNode(child.id);
            Assert.IsFalse(parent.IsChildRoomValid(child.id));
        }

        // Rule 4 — no self-loops.
        [Test]
        public void Rule4_RejectsSelfChild()
        {
            var node = MakeNode(_corridorType);
            Assert.IsFalse(node.IsChildRoomValid(node.id));
        }

        // Rule 5 — a node already in the parent list cannot become a child.
        [Test]
        public void Rule5_RejectsAncestorAsChild()
        {
            var parent = MakeNode(_corridorType);
            var child = MakeNode(_roomType);
            // Pretend child is already an ancestor of parent.
            parent.parentRoomNodeIDList.Add(child.id);
            Assert.IsFalse(parent.IsChildRoomValid(child.id));
        }

        // Rule 6 — child must not already have a parent.
        [Test]
        public void Rule6_RejectsAlreadyParentedChild()
        {
            var parentA = MakeNode(_corridorType);
            var parentB = MakeNode(_corridorType);
            var child = MakeNode(_roomType);
            child.parentRoomNodeIDList.Add(parentA.id);
            Assert.IsFalse(parentB.IsChildRoomValid(child.id));
        }

        // Rule 7 — corridor cannot connect directly to corridor.
        [Test]
        public void Rule7_RejectsCorridorToCorridor()
        {
            var corridor1 = MakeNode(_corridorType);
            var corridor2 = MakeNode(_corridorType);
            Assert.IsFalse(corridor1.IsChildRoomValid(corridor2.id));
        }

        // Rule 8 — non-corridor cannot connect directly to non-corridor.
        [Test]
        public void Rule8_RejectsRoomToRoom()
        {
            var room1 = MakeNode(_roomType);
            var room2 = MakeNode(_roomType);
            Assert.IsFalse(room1.IsChildRoomValid(room2.id));
        }

        // Rule 9 — at most MaxChildCorridors corridor children per node.
        [Test]
        public void Rule9_RejectsTooManyCorridorChildren()
        {
            var parent = MakeNode(_roomType);
            for (int i = 0; i < DungeonSettings.MaxChildCorridors; i++)
            {
                var corridor = MakeNode(_corridorType);
                parent.AddChildRoomNodeIDToRoomNode(corridor.id);
            }
            var extra = MakeNode(_corridorType);
            Assert.IsFalse(parent.IsChildRoomValid(extra.id));
        }

        // Rule 10 — entrance can never be a child.
        [Test]
        public void Rule10_RejectsEntranceAsChild()
        {
            var parent = MakeNode(_corridorType);
            var entrance = MakeNode(_entranceType);
            Assert.IsFalse(parent.IsChildRoomValid(entrance.id));
        }

        // Rule 11 — a corridor that already has a (non-corridor) child rejects more children.
        [Test]
        public void Rule11_RejectsSecondRoomChildOfCorridor()
        {
            var corridor = MakeNode(_corridorType);
            var room1 = MakeNode(_roomType);
            var room2 = MakeNode(_roomType);
            corridor.AddChildRoomNodeIDToRoomNode(room1.id);
            Assert.IsFalse(corridor.IsChildRoomValid(room2.id));
        }

        // Happy path — corridor accepts a room child when nothing else is wrong.
        [Test]
        public void HappyPath_CorridorToRoom_IsValid()
        {
            var corridor = MakeNode(_corridorType);
            var room = MakeNode(_roomType);
            Assert.IsTrue(corridor.IsChildRoomValid(room.id));
        }

        private RoomNodeTypeSO MakeType(string name,
            bool entrance = false, bool corridor = false,
            bool corridorNS = false, bool corridorEW = false,
            bool boss = false, bool none = false)
        {
            var t = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            t.TestSetTypeFlags(name, entrance, corridor, corridorNS, corridorEW, boss, none);
            return t;
        }

        private RoomNodeSO MakeNode(RoomNodeTypeSO type)
        {
            var node = ScriptableObject.CreateInstance<RoomNodeSO>();
            node.Initialise(new Rect(0, 0, 100, 60), _graph, type);
            _graph.AddRoomNode(node);
            return node;
        }
    }
}
