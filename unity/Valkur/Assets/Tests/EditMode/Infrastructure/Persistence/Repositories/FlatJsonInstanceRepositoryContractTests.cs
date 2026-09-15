using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;
namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  Light, spawner, and particle instance repositories all share the same
    //  Read/Write/Exists raw-JSON contract. Each domain gets its own pair of
    //  fixtures (in-memory + JSON-file) sharing the test list below.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Contract pinned for any IBuildingInstanceRepository-shaped repo.
    /// Reused by every flat-file instances repository.</summary>
    public abstract class FlatJsonInstanceRepositoryContractTests
    {
        protected abstract bool   Exists(WorldId w);
        protected abstract string ReadRawJson(WorldId w);
        protected abstract void   WriteRawJson(WorldId w, string json);
        protected virtual  void   OnTearDown() { }

        [TearDown] public void TearDown() => OnTearDown();

        [Test] public void ReadRawJson_MissingWorld_ReturnsNull()
            => Assert.IsNull(ReadRawJson(WorldId.Base));

        [Test] public void Exists_MissingWorld_ReturnsFalse()
            => Assert.IsFalse(Exists(WorldId.Base));

        [Test] public void Write_Then_Read_RoundTripsContent()
        {
            const string payload = "[ {\"id\": 1} ]";
            WriteRawJson(WorldId.Base, payload);
            Assert.IsTrue(Exists(WorldId.Base));
            Assert.AreEqual(payload, ReadRawJson(WorldId.Base));
        }

        [Test] public void Worlds_AreIsolated_NoCrossLeak()
        {
            var altWorld = new WorldId(System.Guid.NewGuid(), "alt");
            WriteRawJson(WorldId.Base, "base-payload");
            WriteRawJson(altWorld, "alt-payload");
            Assert.AreEqual("base-payload", ReadRawJson(WorldId.Base));
            Assert.AreEqual("alt-payload",  ReadRawJson(altWorld));
        }

        [Test] public void NullPayload_StoredAsEmptyString()
        {
            WriteRawJson(WorldId.Base, null);
            Assert.AreEqual(string.Empty, ReadRawJson(WorldId.Base));
        }
    }
}
