using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.Infrastructure.Persistence.Repositories
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

    [TestFixture]
    public class InMemoryItemDropRepositoryTests : ItemDropRepositoryContractTests
    {
        protected override IItemDropRepository CreateRepo() => new InMemoryItemDropRepository();
    }

    [TestFixture]
    public class JsonFileItemDropRepositoryTests : ItemDropRepositoryContractTests
    {
        private string _tempRoot;

        protected override IItemDropRepository CreateRepo()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(),
                "valkur_item_drops_repo_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            return new JsonFileItemDropRepository(_tempRoot);
        }

        protected override void OnTearDown()
        {
            if (!string.IsNullOrEmpty(_tempRoot) && Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        [Test]
        public void PathFor_BaseWorld_UsesLegacyFlatLayout()
        {
            var jsonRepo = (JsonFileItemDropRepository)Repo;
            string path = jsonRepo.PathFor(WorldId.Base);
            StringAssert.Contains("Items", path);
            StringAssert.Contains("item_drops.json", path);
            StringAssert.DoesNotContain("Worlds", path,
                "Base world must keep the legacy flat path so existing builds stay byte-compatible.");
        }

        [Test]
        public void PathFor_NonBaseWorld_NestsUnderWorldsSlug()
        {
            var jsonRepo = (JsonFileItemDropRepository)Repo;
            var alt = new WorldId(System.Guid.NewGuid(), "the_void");
            string path = jsonRepo.PathFor(alt);
            StringAssert.Contains(Path.Combine("Worlds", "the_void", "Items"), path);
        }

        [Test]
        public void CustomSubdir_RoutesUnderConfiguredFolder()
        {
            // Subdir + filename are user-configurable so the same impl can serve
            // the per-run save folder in Phase B without forking the class.
            var custom = new JsonFileItemDropRepository(_tempRoot, "Saves/run-42", "world_drops.json");
            custom.WriteRawJson(WorldId.Base, "{\"drops\":[]}");
            string path = custom.PathFor(WorldId.Base).Replace('\\', '/');
            StringAssert.Contains("Saves/run-42/world_drops.json", path);
        }
    }
}
