using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Data
{
    public class RoomTemplateSOTests
    {
        [Test]
        public void TestRegenerateGuid_ProducesNonEmptyAndUnique()
        {
            var a = ScriptableObject.CreateInstance<RoomTemplateSO>();
            var b = ScriptableObject.CreateInstance<RoomTemplateSO>();

            try
            {
                a.TestRegenerateGuid();
                b.TestRegenerateGuid();

                Assert.IsFalse(string.IsNullOrEmpty(a.guid));
                Assert.IsFalse(string.IsNullOrEmpty(b.guid));
                Assert.AreNotEqual(a.guid, b.guid);
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void GetDoorwayList_ReturnsBackingList()
        {
            var t = ScriptableObject.CreateInstance<RoomTemplateSO>();
            try
            {
                Assert.IsNotNull(t.GetDoorwayList());
                Assert.AreSame(t.doorwayList, t.GetDoorwayList());
            }
            finally
            {
                Object.DestroyImmediate(t);
            }
        }
    }
}
