using NUnit.Framework;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;
using Valkur.Gameplay.World.Dungeon.Udemy.Spawning;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Spawning
{
    public class RoomRegistryTests
    {
        [SetUp] public void SetUp() => RoomRegistry.Clear();
        [TearDown] public void TearDown() => RoomRegistry.Clear();

        [Test]
        public void Register_ThenGet_RoundTrips()
        {
            var room = new Room { id = "abc" };
            RoomRegistry.Register(room);
            Assert.AreSame(room, RoomRegistry.Get("abc"));
            Assert.AreEqual(1, RoomRegistry.Count);
        }

        [Test]
        public void Register_NullOrEmptyId_NoOp()
        {
            RoomRegistry.Register(null);
            RoomRegistry.Register(new Room { id = string.Empty });
            Assert.AreEqual(0, RoomRegistry.Count);
        }

        [Test]
        public void Get_UnknownId_ReturnsNull()
        {
            Assert.IsNull(RoomRegistry.Get("missing"));
            Assert.IsNull(RoomRegistry.Get(null));
            Assert.IsNull(RoomRegistry.Get(string.Empty));
        }

        [Test]
        public void Unregister_RemovesEntry()
        {
            RoomRegistry.Register(new Room { id = "abc" });
            RoomRegistry.Unregister("abc");
            Assert.IsNull(RoomRegistry.Get("abc"));
        }

        [Test]
        public void Register_OverwritesExistingId()
        {
            var first = new Room { id = "abc" };
            var second = new Room { id = "abc" };
            RoomRegistry.Register(first);
            RoomRegistry.Register(second);
            Assert.AreSame(second, RoomRegistry.Get("abc"));
            Assert.AreEqual(1, RoomRegistry.Count);
        }
    }
}
