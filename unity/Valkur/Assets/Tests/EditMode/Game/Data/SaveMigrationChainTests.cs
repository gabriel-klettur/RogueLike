using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Game.Data
{
    public class SaveMigrationChainTests
    {
        [SetUp]
        public void ResetChain() => SaveMigrationChain.Clear();

        [Test]
        public void Register_SameStepTwice_IsIdempotent()
        {
            SaveMigrationChain.Register("1.0", "1.1", d => { });
            SaveMigrationChain.Register("1.0", "1.1", d => { });
            Assert.AreEqual(1, SaveMigrationChain.AllSteps.Count);
        }

        [Test]
        public void MigrateTo_RunsStepsUntilTarget()
        {
            int calls = 0;
            SaveMigrationChain.Register("1.0", "1.1", d => { calls++; });
            SaveMigrationChain.Register("1.1", "1.2", d => { calls++; });

            var data = new GameSaveData { schemaVersion = "1.0" };
            int applied = SaveMigrationChain.MigrateTo(data, "1.2");
            Assert.AreEqual(2, applied);
            Assert.AreEqual(2, calls);
            Assert.AreEqual("1.2", data.schemaVersion);
        }

        [Test]
        public void MigrateTo_WhenAtTarget_AppliesNothing()
        {
            SaveMigrationChain.Register("1.0", "1.1", d => { });
            var data = new GameSaveData { schemaVersion = "1.1" };
            int applied = SaveMigrationChain.MigrateTo(data, "1.1");
            Assert.AreEqual(0, applied);
            Assert.AreEqual("1.1", data.schemaVersion);
        }

        [Test]
        public void MigrateTo_UnknownOrigin_DoesNotCrash()
        {
            var data = new GameSaveData { schemaVersion = "99.99" };
            LogAssert.Expect(LogType.Warning,
                "[SaveMigrationChain] no migration path from '99.99' (target '1.1').");
            int applied = SaveMigrationChain.MigrateTo(data, "1.1");
            Assert.AreEqual(0, applied);
            Assert.AreEqual("99.99", data.schemaVersion);
        }

        [Test]
        public void SaveSchemaMigrator_Migrate_BumpsTo1_1()
        {
            var data = new GameSaveData { schemaVersion = "1.0" };
            SaveSchemaMigrator.Migrate(data);
            Assert.AreEqual("1.1", data.schemaVersion);
        }

        [Test]
        public void SaveSchemaMigrator_Migrate_NullReturnsNull()
        {
            Assert.IsNull(SaveSchemaMigrator.Migrate(null));
        }
    }
}
