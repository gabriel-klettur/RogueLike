using NUnit.Framework;
using Valkur.Core.Persistence;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data
{
    /// <summary>
    /// Pins the contract that <see cref="InventoryData"/> participates in
    /// the generic <c>MigrationChain&lt;T&gt;</c> via <see cref="IVersioned"/>.
    /// Catches accidental removal of the interface during refactors.
    /// </summary>
    [TestFixture]
    public class InventoryDataVersioningTests
    {
        [Test]
        public void InventoryData_ImplementsIVersioned()
        {
            IVersioned versioned = new InventoryData();
            Assert.IsNotNull(versioned);
        }

        [Test]
        public void SchemaVersion_GetterDelegatesToField()
        {
            var inv = new InventoryData { schemaVersion = "2.5" };
            Assert.AreEqual("2.5", ((IVersioned)inv).SchemaVersion);
        }

        [Test]
        public void SchemaVersion_SetterWritesField()
        {
            var inv = new InventoryData();
            ((IVersioned)inv).SchemaVersion = "3.0";
            Assert.AreEqual("3.0", inv.schemaVersion);
        }
    }
}
