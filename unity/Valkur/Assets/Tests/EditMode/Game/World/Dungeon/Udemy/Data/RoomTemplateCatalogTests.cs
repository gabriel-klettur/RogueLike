using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Data
{
    public class RoomTemplateCatalogTests
    {
        private RoomTemplateCatalog _catalog;
        private RoomNodeTypeSO _typeRoom;
        private RoomNodeTypeSO _typeBoss;

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<RoomTemplateCatalog>();
            _typeRoom = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            _typeRoom.TestSetTypeFlags("Room");
            _typeBoss = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            _typeBoss.TestSetTypeFlags("Boss", boss: true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_typeRoom);
            Object.DestroyImmediate(_typeBoss);
        }

        [Test]
        public void AddTemplate_AddsAndIsRetrievableByGuid()
        {
            var t = MakeTemplate(_typeRoom);
            Assert.IsTrue(_catalog.AddTemplate(t));
            Assert.AreSame(t, _catalog.GetByGuid(t.guid));
        }

        [Test]
        public void AddTemplate_RejectsDuplicateGuid()
        {
            var t = MakeTemplate(_typeRoom);
            Assert.IsTrue(_catalog.AddTemplate(t));
            Assert.IsFalse(_catalog.AddTemplate(t));
        }

        [Test]
        public void GetByGuid_UnknownGuid_ReturnsNull()
        {
            Assert.IsNull(_catalog.GetByGuid("nope"));
            Assert.IsNull(_catalog.GetByGuid(""));
            Assert.IsNull(_catalog.GetByGuid(null));
        }

        [Test]
        public void UpsertTemplate_ReplacesExistingEntry_LastWriteWins()
        {
            var t1 = MakeTemplate(_typeRoom);
            _catalog.UpsertTemplate(t1);

            var t2 = MakeTemplate(_typeRoom);
            t2.guid = t1.guid; // simulate edit-in-place: same GUID, different SO
            _catalog.UpsertTemplate(t2);

            Assert.AreSame(t2, _catalog.GetByGuid(t1.guid));
            Assert.AreEqual(1, _catalog.Templates.Count);
        }

        [Test]
        public void FindByNodeType_ReturnsOnlyMatching()
        {
            var room1 = MakeTemplate(_typeRoom);
            var room2 = MakeTemplate(_typeRoom);
            var boss = MakeTemplate(_typeBoss);

            _catalog.AddTemplate(room1);
            _catalog.AddTemplate(room2);
            _catalog.AddTemplate(boss);

            var matches = _catalog.FindByNodeType(_typeRoom);
            Assert.AreEqual(2, matches.Count);
            Assert.IsTrue(matches.Contains(room1));
            Assert.IsTrue(matches.Contains(room2));
            Assert.IsFalse(matches.Contains(boss));
        }

        [Test]
        public void FindByNodeType_NullType_ReturnsEmpty()
        {
            Assert.AreEqual(0, _catalog.FindByNodeType(null).Count);
        }

        private RoomTemplateSO MakeTemplate(RoomNodeTypeSO type)
        {
            var t = ScriptableObject.CreateInstance<RoomTemplateSO>();
            t.roomNodeType = type;
            t.TestRegenerateGuid();
            return t;
        }
    }
}
