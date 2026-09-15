using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;
namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Shared contract every <see cref="IItemDropRepository"/> implementation
    /// must satisfy — mirrors <see cref="BuildingInstanceRepositoryContractTests"/>
    /// so the file/in-memory parity is verified the same way as for buildings.
    /// </summary>
    public abstract class ItemDropRepositoryContractTests
    {
        protected IItemDropRepository Repo { get; private set; }

        protected abstract IItemDropRepository CreateRepo();
        protected virtual  void OnTearDown() { }

        [SetUp]    public void SetUp()    => Repo = CreateRepo();
        [TearDown] public void TearDown() => OnTearDown();

        [Test]
        public void ReadRawJson_MissingWorld_ReturnsNull()
            => Assert.IsNull(Repo.ReadRawJson(WorldId.Base));

        [Test]
        public void Exists_MissingWorld_ReturnsFalse()
            => Assert.IsFalse(Repo.Exists(WorldId.Base));

        [Test]
        public void Write_Then_Read_RoundTripsContent()
        {
            const string payload = "{\"schemaVersion\":1,\"drops\":[]}";
            Repo.WriteRawJson(WorldId.Base, payload);
            Assert.IsTrue(Repo.Exists(WorldId.Base));
            Assert.AreEqual(payload, Repo.ReadRawJson(WorldId.Base));
        }

        [Test]
        public void Write_OverwriteExisting_ReplacesContent()
        {
            Repo.WriteRawJson(WorldId.Base, "{\"drops\":[]}");
            Repo.WriteRawJson(WorldId.Base, "{\"drops\":[{\"dropId\":\"abc\"}]}");
            Assert.AreEqual("{\"drops\":[{\"dropId\":\"abc\"}]}", Repo.ReadRawJson(WorldId.Base));
        }

        [Test]
        public void Worlds_AreIsolated_NoCrossLeak()
        {
            var altWorld = new WorldId(System.Guid.NewGuid(), "alt");
            Repo.WriteRawJson(WorldId.Base, "base-drops");
            Repo.WriteRawJson(altWorld, "alt-drops");
            Assert.AreEqual("base-drops", Repo.ReadRawJson(WorldId.Base));
            Assert.AreEqual("alt-drops",  Repo.ReadRawJson(altWorld));
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
            Repo.WriteRawJson(WorldId.Base, null);
            Assert.IsTrue(Repo.Exists(WorldId.Base));
            Assert.AreEqual(string.Empty, Repo.ReadRawJson(WorldId.Base));
        }
    }
}
