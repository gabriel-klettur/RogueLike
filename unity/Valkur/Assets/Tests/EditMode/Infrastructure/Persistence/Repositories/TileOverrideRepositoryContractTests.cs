using System.IO;
using System.Linq;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Shared contract every <see cref="ITileOverrideRepository"/>
    /// implementation must satisfy. Two derived fixtures plug in the
    /// concrete repos. A future repository (e.g. RemoteTileOverrideRepository
    /// in Phase 4) only needs to subclass and override <see cref="CreateRepo"/>;
    /// the test list is inherited automatically.
    /// </summary>
    public abstract class TileOverrideRepositoryContractTests
    {
        protected ITileOverrideRepository Repo { get; private set; }

        protected abstract ITileOverrideRepository CreateRepo();
        protected virtual  void OnTearDown() { }

        [SetUp]
        public void SetUp() => Repo = CreateRepo();

        [TearDown]
        public void TearDown() => OnTearDown();

        // ── Behaviours ───────────────────────────────────────────────────────────

        [Test]
        public void Read_MissingZone_ReturnsNull()
        {
            Assert.IsNull(Repo.Read(WorldId.Base, "no_such_zone"));
        }

        [Test]
        public void Exists_MissingZone_ReturnsFalse()
        {
            Assert.IsFalse(Repo.Exists(WorldId.Base, "no_such_zone"));
        }

        [Test]
        public void Write_Then_Read_RoundTripsContent()
        {
            const string payload = "{\"layers\":{\"Ground\":[]}}";
            Repo.Write(WorldId.Base, "alpha", payload);
            Assert.IsTrue(Repo.Exists(WorldId.Base, "alpha"));
            Assert.AreEqual(payload, Repo.Read(WorldId.Base, "alpha"));
        }

        [Test]
        public void Write_OverwriteExisting_ReplacesContent()
        {
            Repo.Write(WorldId.Base, "alpha", "{}");
            Repo.Write(WorldId.Base, "alpha", "{\"v\":2}");
            Assert.AreEqual("{\"v\":2}", Repo.Read(WorldId.Base, "alpha"));
        }

        [Test]
        public void Delete_ReturnsTrueWhenFileExisted()
        {
            Repo.Write(WorldId.Base, "alpha", "{}");
            Assert.IsTrue(Repo.Delete(WorldId.Base, "alpha"));
            Assert.IsFalse(Repo.Exists(WorldId.Base, "alpha"));
        }

        [Test]
        public void Delete_NoFile_ReturnsFalseWithoutThrowing()
        {
            Assert.IsFalse(Repo.Delete(WorldId.Base, "ghost"));
        }

        [Test]
        public void Rename_MovesContent()
        {
            Repo.Write(WorldId.Base, "old", "{\"k\":1}");
            Assert.IsTrue(Repo.Rename(WorldId.Base, "old", "new"));
            Assert.IsFalse(Repo.Exists(WorldId.Base, "old"));
            Assert.AreEqual("{\"k\":1}", Repo.Read(WorldId.Base, "new"));
        }

        [Test]
        public void Rename_NoSource_IsSuccessfulNoOp()
        {
            // Used by the map-editor rename flow when the zone never had any
            // painted tiles — caller must not have to check existence first.
            Assert.IsTrue(Repo.Rename(WorldId.Base, "ghost", "new"));
        }

        [Test]
        public void Rename_DestinationExists_ReturnsFalseAndPreservesBoth()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Repo.Write(WorldId.Base, "src", "src-data");
            Repo.Write(WorldId.Base, "dst", "dst-data");
            Assert.IsFalse(Repo.Rename(WorldId.Base, "src", "dst"),
                "Rename must not silently overwrite an existing destination.");
            Assert.AreEqual("src-data", Repo.Read(WorldId.Base, "src"));
            Assert.AreEqual("dst-data", Repo.Read(WorldId.Base, "dst"));
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void List_OnlyReturnsZonesInTheRequestedWorld()
        {
            var altWorld = new WorldId(System.Guid.NewGuid(), "alt");
            Repo.Write(WorldId.Base, "in_base", "{}");
            Repo.Write(altWorld, "in_alt", "{}");

            var inBase = Repo.ListAvailableZones(WorldId.Base).ToList();
            var inAlt  = Repo.ListAvailableZones(altWorld).ToList();

            CollectionAssert.Contains(inBase, "in_base");
            CollectionAssert.DoesNotContain(inBase, "in_alt");
            CollectionAssert.Contains(inAlt, "in_alt");
            CollectionAssert.DoesNotContain(inAlt, "in_base");
        }

        [Test]
        public void Worlds_AreIsolated_NoCrossLeak()
        {
            var altWorld = new WorldId(System.Guid.NewGuid(), "alt");
            Repo.Write(WorldId.Base, "shared_name", "base-payload");
            Repo.Write(altWorld, "shared_name", "alt-payload");
            Assert.AreEqual("base-payload", Repo.Read(WorldId.Base, "shared_name"));
            Assert.AreEqual("alt-payload",  Repo.Read(altWorld, "shared_name"));
        }
    }

    /// <summary>Contract pinned for the in-memory repository (zero I/O).</summary>
    [TestFixture]
    public class InMemoryTileOverrideRepositoryTests : TileOverrideRepositoryContractTests
    {
        protected override ITileOverrideRepository CreateRepo() => new InMemoryTileOverrideRepository();
    }

    /// <summary>Contract pinned for the JSON-file repository against a scratch
    /// directory so the test never touches the user's persistentDataPath.</summary>
    [TestFixture]
    public class JsonFileTileOverrideRepositoryTests : TileOverrideRepositoryContractTests
    {
        private string _tempDir;

        protected override ITileOverrideRepository CreateRepo()
        {
            _tempDir = Path.Combine(Path.GetTempPath(),
                "valkur_tileoverride_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            return new JsonFileTileOverrideRepository(_tempDir);
        }

        protected override void OnTearDown()
        {
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
    }
}
