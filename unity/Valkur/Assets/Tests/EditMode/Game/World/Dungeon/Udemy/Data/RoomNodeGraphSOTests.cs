using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Data
{
    public class RoomNodeGraphSOTests
    {
        private RoomNodeGraphSO _graph;
        private RoomNodeTypeSO _type;

        [SetUp]
        public void SetUp()
        {
            _graph = ScriptableObject.CreateInstance<RoomNodeGraphSO>();
            _type = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            _type.TestSetTypeFlags("Room");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_graph);
            Object.DestroyImmediate(_type);
        }

        [Test]
        public void GetRoomNode_ById_ReturnsAddedNode()
        {
            var node = MakeNode();
            _graph.AddRoomNode(node);

            Assert.AreSame(node, _graph.GetRoomNode(node.id));
        }

        [Test]
        public void GetRoomNode_UnknownId_ReturnsNull()
        {
            Assert.IsNull(_graph.GetRoomNode("not-a-real-guid"));
        }

        [Test]
        public void GetChildRoomNodes_YieldsLiveChildren_AfterDictionaryRebuild()
        {
            var parent = MakeNode();
            var c1 = MakeNode();
            var c2 = MakeNode();
            _graph.AddRoomNode(parent);
            _graph.AddRoomNode(c1);
            _graph.AddRoomNode(c2);

            parent.childRoomNodeIDList.Add(c1.id);
            parent.childRoomNodeIDList.Add(c2.id);

            var seen = new HashSet<string>();
            foreach (var child in _graph.GetChildRoomNodes(parent))
                seen.Add(child.id);

            Assert.IsTrue(seen.Contains(c1.id));
            Assert.IsTrue(seen.Contains(c2.id));
            Assert.AreEqual(2, seen.Count);
        }

        [Test]
        public void RemoveRoomNode_DropsFromLookup()
        {
            var node = MakeNode();
            _graph.AddRoomNode(node);
            Assert.IsNotNull(_graph.GetRoomNode(node.id));

            _graph.RemoveRoomNode(node);
            _graph.InvalidateDictionary();

            Assert.IsNull(_graph.GetRoomNode(node.id));
        }

        private RoomNodeSO MakeNode()
        {
            var n = ScriptableObject.CreateInstance<RoomNodeSO>();
            n.Initialise(new Rect(0, 0, 100, 60), _graph, _type);
            return n;
        }
    }
}
