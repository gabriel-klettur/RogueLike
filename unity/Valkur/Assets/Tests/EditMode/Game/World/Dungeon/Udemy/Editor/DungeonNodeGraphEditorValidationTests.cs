using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.Editors.DungeonNodeGraph;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Editor
{
    /// <summary>
    /// Mirrors RoomNodeSOValidationTests but against the editor's DTO-shaped
    /// validator (<see cref="DungeonNodeGraphEditor.IsChildRoomValid"/>).
    /// Both implementations of the 11 rules must stay in lockstep.
    /// </summary>
    public class DungeonNodeGraphEditorValidationTests
    {
        private RoomNodeTypeSO _entrance, _corridor, _room, _boss, _none;

        [SetUp]
        public void SetUp()
        {
            _entrance = MakeType("Entrance", entrance: true);
            _corridor = MakeType("Corridor", corridor: true);
            _room = MakeType("Room");
            _boss = MakeType("Boss", boss: true);
            _none = MakeType("None", none: true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_entrance);
            Object.DestroyImmediate(_corridor);
            Object.DestroyImmediate(_room);
            Object.DestroyImmediate(_boss);
            Object.DestroyImmediate(_none);
        }

        [Test]
        public void HappyPath_CorridorToRoom_IsValid()
        {
            var corridor = MakeNode(_corridor);
            var room = MakeNode(_room);
            var graph = new List<DungeonGraphNodeData> { corridor, room };
            Assert.IsTrue(DungeonNodeGraphEditor.IsChildRoomValid(graph, corridor, room, out _));
        }

        [Test]
        public void RejectsCorridorToCorridor()
        {
            var c1 = MakeNode(_corridor);
            var c2 = MakeNode(_corridor);
            Assert.IsFalse(DungeonNodeGraphEditor.IsChildRoomValid(
                new List<DungeonGraphNodeData> { c1, c2 }, c1, c2, out _));
        }

        [Test]
        public void RejectsRoomToRoom()
        {
            var r1 = MakeNode(_room);
            var r2 = MakeNode(_room);
            Assert.IsFalse(DungeonNodeGraphEditor.IsChildRoomValid(
                new List<DungeonGraphNodeData> { r1, r2 }, r1, r2, out _));
        }

        [Test]
        public void RejectsEntranceAsChild()
        {
            var corridor = MakeNode(_corridor);
            var entrance = MakeNode(_entrance);
            Assert.IsFalse(DungeonNodeGraphEditor.IsChildRoomValid(
                new List<DungeonGraphNodeData> { corridor, entrance }, corridor, entrance, out _));
        }

        [Test]
        public void RejectsNoneTypedChild()
        {
            var parent = MakeNode(_corridor);
            var noneChild = MakeNode(_none);
            Assert.IsFalse(DungeonNodeGraphEditor.IsChildRoomValid(
                new List<DungeonGraphNodeData> { parent, noneChild }, parent, noneChild, out _));
        }

        [Test]
        public void RejectsAlreadyParentedChild()
        {
            var p1 = MakeNode(_corridor);
            var p2 = MakeNode(_corridor);
            var room = MakeNode(_room);
            room.ParentIds.Add(p1.Id);
            Assert.IsFalse(DungeonNodeGraphEditor.IsChildRoomValid(
                new List<DungeonGraphNodeData> { p1, p2, room }, p2, room, out _));
        }

        [Test]
        public void RejectsSelfChild()
        {
            var node = MakeNode(_corridor);
            Assert.IsFalse(DungeonNodeGraphEditor.IsChildRoomValid(
                new List<DungeonGraphNodeData> { node }, node, node, out _));
        }

        [Test]
        public void RejectsSecondConnectedBoss()
        {
            var c1 = MakeNode(_corridor);
            var b1 = MakeNode(_boss);
            b1.ParentIds.Add(c1.Id);
            c1.ChildIds.Add(b1.Id);

            var c2 = MakeNode(_corridor);
            var b2 = MakeNode(_boss);

            var graph = new List<DungeonGraphNodeData> { c1, b1, c2, b2 };
            Assert.IsFalse(DungeonNodeGraphEditor.IsChildRoomValid(graph, c2, b2, out _));
        }

        private static RoomNodeTypeSO MakeType(string name,
            bool entrance = false, bool corridor = false,
            bool boss = false, bool none = false)
        {
            var t = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            t.TestSetTypeFlags(name, entrance, corridor, boss: boss, none: none);
            return t;
        }

        private static DungeonGraphNodeData MakeNode(RoomNodeTypeSO type)
        {
            return new DungeonGraphNodeData
            {
                NodeType = type,
                RoomNodeName = type != null ? type.RoomNodeTypeName : "Node",
            };
        }
    }
}
