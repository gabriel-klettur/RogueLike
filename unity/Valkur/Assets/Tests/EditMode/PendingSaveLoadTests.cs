using NUnit.Framework;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode
{
    public class PendingSaveLoadTests
    {
        [SetUp]
        public void SetUp()
        {
            // Ensure clean state before each test
            PendingSaveLoad.Path = null;
        }

        [Test]
        public void HasPending_WhenPathNull_ReturnsFalse()
        {
            PendingSaveLoad.Path = null;
            Assert.IsFalse(PendingSaveLoad.HasPending);
        }

        [Test]
        public void HasPending_WhenPathEmpty_ReturnsFalse()
        {
            PendingSaveLoad.Path = "";
            Assert.IsFalse(PendingSaveLoad.HasPending);
        }

        [Test]
        public void HasPending_WhenPathSet_ReturnsTrue()
        {
            PendingSaveLoad.Path = "saves/slot_1.json";
            Assert.IsTrue(PendingSaveLoad.HasPending);
        }

        [Test]
        public void Consume_ReturnsPathAndClearsIt()
        {
            PendingSaveLoad.Path = "saves/slot_1.json";
            string consumed = PendingSaveLoad.Consume();

            Assert.AreEqual("saves/slot_1.json", consumed);
            Assert.IsNull(PendingSaveLoad.Path);
            Assert.IsFalse(PendingSaveLoad.HasPending);
        }

        [Test]
        public void Consume_WhenEmpty_ReturnsNull()
        {
            PendingSaveLoad.Path = null;
            string consumed = PendingSaveLoad.Consume();

            Assert.IsNull(consumed);
            Assert.IsFalse(PendingSaveLoad.HasPending);
        }

        [Test]
        public void Consume_CalledTwice_SecondReturnsNull()
        {
            PendingSaveLoad.Path = "saves/slot_2.json";
            PendingSaveLoad.Consume();
            string second = PendingSaveLoad.Consume();

            Assert.IsNull(second);
            Assert.IsFalse(PendingSaveLoad.HasPending);
        }
    }
}
