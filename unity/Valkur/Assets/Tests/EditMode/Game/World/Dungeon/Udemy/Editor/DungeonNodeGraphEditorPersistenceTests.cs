using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.Editors.DungeonNodeGraph;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Editor
{
    public class DungeonNodeGraphEditorPersistenceTests
    {
        private RoomNodeTypeListSO _types;
        private RoomNodeTypeSO _entranceType;
        private RoomNodeTypeSO _corridorType;
        private RoomNodeTypeSO _roomType;

        [SetUp]
        public void SetUp()
        {
            _entranceType = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            _entranceType.TestSetTypeFlags("Entrance", entrance: true);
            _corridorType = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            _corridorType.TestSetTypeFlags("Corridor", corridor: true);
            _roomType = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            _roomType.TestSetTypeFlags("Room");

            _types = ScriptableObject.CreateInstance<RoomNodeTypeListSO>();
            _types.TestAdd(_entranceType);
            _types.TestAdd(_corridorType);
            _types.TestAdd(_roomType);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_types);
            Object.DestroyImmediate(_entranceType);
            Object.DestroyImmediate(_corridorType);
            Object.DestroyImmediate(_roomType);
        }

        [Test]
        public void ToDtoFromDto_RoundTripsAllFields()
        {
            var nodes = new List<DungeonGraphNodeData>
            {
                new DungeonGraphNodeData
                {
                    Id = "id-1",
                    RoomNodeName = "Entrance",
                    NodeType = _entranceType,
                    Position = new Vector2(10, 20),
                    ChildIds = new List<string> { "id-2" },
                },
                new DungeonGraphNodeData
                {
                    Id = "id-2",
                    RoomNodeName = "Corridor",
                    NodeType = _corridorType,
                    Position = new Vector2(30, 40),
                    ParentIds = new List<string> { "id-1" },
                },
            };

            var dto = DungeonNodeGraphEditor.ToDto("test-graph", nodes);
            var roundTrip = DungeonNodeGraphEditor.FromDto(dto, _types);

            Assert.AreEqual(2, roundTrip.Count);
            Assert.AreEqual("id-1", roundTrip[0].Id);
            Assert.AreEqual("Entrance", roundTrip[0].RoomNodeName);
            Assert.AreSame(_entranceType, roundTrip[0].NodeType);
            Assert.AreEqual(new Vector2(10, 20), roundTrip[0].Position);
            CollectionAssert.AreEqual(new[] { "id-2" }, roundTrip[0].ChildIds);

            Assert.AreSame(_corridorType, roundTrip[1].NodeType);
            CollectionAssert.AreEqual(new[] { "id-1" }, roundTrip[1].ParentIds);
        }

        [Test]
        public void SaveLoad_RoundTripsViaDisk()
        {
            string fileName = "_test_graph_" + System.Guid.NewGuid().ToString("N").Substring(0, 6);

            try
            {
                var nodes = new List<DungeonGraphNodeData>
                {
                    new DungeonGraphNodeData
                    {
                        Id = "id-1",
                        RoomNodeName = "Entrance",
                        NodeType = _entranceType,
                        Position = new Vector2(5, 5),
                    },
                };
                var dto = DungeonNodeGraphEditor.ToDto(fileName, nodes);

                Assert.IsTrue(DungeonNodeGraphEditor.SaveToFile(fileName, dto));
                var loaded = DungeonNodeGraphEditor.LoadFromFile(fileName);

                Assert.IsNotNull(loaded);
                Assert.AreEqual(fileName, loaded.graphName);
                Assert.AreEqual(1, loaded.nodes.Count);
                Assert.AreEqual("Entrance", loaded.nodes[0].nodeTypeName);
            }
            finally
            {
                DungeonNodeGraphEditor.DeleteFile(fileName);
            }
        }

        [Test]
        public void Sanitise_StripsPathSeparators()
        {
            // We can't test Sanitise directly (private), so go through Save and
            // confirm the file lands inside the graphs directory regardless of
            // how nasty the input was.
            string nasty = "../../escape\\attempt";
            string sanitised = "escapeattempt";

            try
            {
                var dto = new DungeonGraphDto { graphName = nasty };
                Assert.IsTrue(DungeonNodeGraphEditor.SaveToFile(nasty, dto));

                var path = Path.Combine(DungeonNodeGraphEditor.GraphsDirectory, sanitised + ".json");
                Assert.IsTrue(File.Exists(path), $"expected sanitised file at {path}");
            }
            finally
            {
                DungeonNodeGraphEditor.DeleteFile(nasty);
            }
        }

        [Test]
        public void LoadFromFile_UnknownFile_ReturnsNull()
        {
            Assert.IsNull(DungeonNodeGraphEditor.LoadFromFile("does_not_exist_" + System.Guid.NewGuid()));
        }
    }
}
