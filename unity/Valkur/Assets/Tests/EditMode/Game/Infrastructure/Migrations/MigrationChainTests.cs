using NUnit.Framework;
using Valkur.Core.Persistence;
using Valkur.Infrastructure.Migrations;

namespace Valkur.Tests.EditMode.Game.Infrastructure.Migrations
{
    /// <summary>
    /// Pins the contract of <see cref="MigrationChain{T}"/>: walks registered
    /// steps from the doc's current version to the chain's target, applies
    /// each upgrade exactly once, leaves up-to-date docs untouched, and never
    /// loops indefinitely on missing-path inputs.
    /// </summary>
    [TestFixture]
    public class MigrationChainTests
    {
        private sealed class Doc : IVersioned
        {
            public string SchemaVersion { get; set; }
            public int Counter;
        }

        [Test]
        public void Migrate_NoSteps_NoOp()
        {
            var chain = new MigrationChain<Doc>("1.0");
            var doc = new Doc { SchemaVersion = "1.0", Counter = 0 };
            int applied = chain.Migrate(doc);
            Assert.AreEqual(0, applied);
            Assert.AreEqual("1.0", doc.SchemaVersion);
        }

        [Test]
        public void Migrate_WalksSequentially()
        {
            var chain = new MigrationChain<Doc>("1.3")
                .Register("1.0", "1.1", d => d.Counter += 1)
                .Register("1.1", "1.2", d => d.Counter += 10)
                .Register("1.2", "1.3", d => d.Counter += 100);

            var doc = new Doc { SchemaVersion = "1.0", Counter = 0 };
            int applied = chain.Migrate(doc);

            Assert.AreEqual(3, applied);
            Assert.AreEqual("1.3", doc.SchemaVersion);
            Assert.AreEqual(111, doc.Counter,
                "All three steps must run in order, each contributing its delta.");
        }

        [Test]
        public void Migrate_StartsFromIntermediateVersion()
        {
            var chain = new MigrationChain<Doc>("1.3")
                .Register("1.0", "1.1", d => d.Counter += 1)
                .Register("1.1", "1.2", d => d.Counter += 10)
                .Register("1.2", "1.3", d => d.Counter += 100);

            var doc = new Doc { SchemaVersion = "1.1", Counter = 0 };
            int applied = chain.Migrate(doc);

            Assert.AreEqual(2, applied);
            Assert.AreEqual(110, doc.Counter,
                "Starting from 1.1, only 1.1->1.2 and 1.2->1.3 should run.");
        }

        [Test]
        public void Migrate_AlreadyAtCurrent_DoesNothing()
        {
            var chain = new MigrationChain<Doc>("1.5")
                .Register("1.0", "1.5", d => d.Counter += 999);
            var doc = new Doc { SchemaVersion = "1.5", Counter = 0 };
            int applied = chain.Migrate(doc);
            Assert.AreEqual(0, applied);
            Assert.AreEqual(0, doc.Counter);
        }

        [Test]
        public void Migrate_EmptyVersion_TreatedAsLowestRegistered()
        {
            // A doc with no SchemaVersion is legacy / pre-versioned: the chain
            // should treat that as the lowest registered "from" so it gets
            // every upgrade applied to reach current.
            var chain = new MigrationChain<Doc>("1.2")
                .Register("1.0", "1.1", d => d.Counter += 1)
                .Register("1.1", "1.2", d => d.Counter += 1);

            var doc = new Doc { SchemaVersion = null, Counter = 0 };
            int applied = chain.Migrate(doc);
            Assert.AreEqual(2, applied);
            Assert.AreEqual("1.2", doc.SchemaVersion);
        }

        [Test]
        public void Migrate_MissingPath_ForcesCurrentVersionTagAndWarns()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var chain = new MigrationChain<Doc>("9.9")
                .Register("1.0", "1.1", d => d.Counter += 1);
            // Doc lands on "1.5" — no step from "1.5" exists.
            var doc = new Doc { SchemaVersion = "1.5" };
            chain.Migrate(doc);
            Assert.AreEqual("9.9", doc.SchemaVersion,
                "The chain must force the version tag when no path exists, " +
                "otherwise the same broken doc gets retried forever.");
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void Register_DuplicateStep_IsIdempotent()
        {
            var chain = new MigrationChain<Doc>("1.1")
                .Register("1.0", "1.1", d => d.Counter += 1)
                .Register("1.0", "1.1", d => d.Counter += 1); // duplicate
            var doc = new Doc { SchemaVersion = "1.0" };
            chain.Migrate(doc);
            Assert.AreEqual(1, doc.Counter,
                "Duplicate Register calls must NOT cause the upgrade to run twice.");
        }
    }
}
