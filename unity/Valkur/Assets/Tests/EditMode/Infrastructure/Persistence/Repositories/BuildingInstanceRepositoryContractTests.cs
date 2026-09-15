using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;
namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Shared contract every <see cref="IBuildingInstanceRepository"/>
    /// implementation must satisfy. Two derived fixtures plug in the
    /// concrete repos.
    /// </summary>
    public abstract class BuildingInstanceRepositoryContractTests
    {
        protected IBuildingInstanceRepository Repo { get; private set; }

        protected abstract IBuildingInstanceRepository CreateRepo();
        protected virtual  void OnTearDown() { }

        [SetUp]
        public void SetUp() => Repo = CreateRepo();

        [TearDown]
        public void TearDown() => OnTearDown();

        // ── Behaviours ───────────────────────────────────────────────────────────

        [Test]
        public void ReadRawJson_MissingWorld_ReturnsNull()
        {
            Assert.IsNull(Repo.ReadRawJson(WorldId.Base));
        }

        [Test]
        public void Exists_MissingWorld_ReturnsFalse()
        {
            Assert.IsFalse(Repo.Exists(WorldId.Base));
        }

        [Test]
        public void Write_Then_Read_RoundTripsContent()
        {
            const string payload = "[ {\"id\": 1, \"template_id\": 5, \"zone\": \"Lobby\"} ]";
            Repo.WriteRawJson(WorldId.Base, payload);
            Assert.IsTrue(Repo.Exists(WorldId.Base));
            Assert.AreEqual(payload, Repo.ReadRawJson(WorldId.Base));
        }

        [Test]
        public void Write_OverwriteExisting_ReplacesContent()
        {
            Repo.WriteRawJson(WorldId.Base, "[]");
            Repo.WriteRawJson(WorldId.Base, "[{\"id\":1}]");
            Assert.AreEqual("[{\"id\":1}]", Repo.ReadRawJson(WorldId.Base));
        }

        [Test]
        public void Worlds_AreIsolated_NoCrossLeak()
        {
            var altWorld = new WorldId(System.Guid.NewGuid(), "alt");
            Repo.WriteRawJson(WorldId.Base, "base-payload");
            Repo.WriteRawJson(altWorld, "alt-payload");
            Assert.AreEqual("base-payload", Repo.ReadRawJson(WorldId.Base));
            Assert.AreEqual("alt-payload",  Repo.ReadRawJson(altWorld));
        }

        [Test]
        public void EmptyPayload_RoundTripsAsEmpty()
        {
            Repo.WriteRawJson(WorldId.Base, string.Empty);
            Assert.IsTrue(Repo.Exists(WorldId.Base));
            Assert.AreEqual(string.Empty, Repo.ReadRawJson(WorldId.Base));
        }

        [Test]
        public void NullPayload_StoredAsEmptyString()
        {
            // Defensive: callers that hand us a null mid-pipeline must not
            // explode. The contract is "null in -> empty round-trip".
            Repo.WriteRawJson(WorldId.Base, null);
            Assert.IsTrue(Repo.Exists(WorldId.Base));
            Assert.AreEqual(string.Empty, Repo.ReadRawJson(WorldId.Base));
        }
    }
}
