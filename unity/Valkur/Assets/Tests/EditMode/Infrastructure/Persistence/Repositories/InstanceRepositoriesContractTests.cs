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

    // ── Helpers to scope a temp StreamingAssets root for JSON-file fixtures ──────

    internal static class TempRootHelper
    {
        public static string Create(string tag)
        {
            string p = Path.Combine(Path.GetTempPath(),
                $"valkur_repo_tests_{tag}_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(p);
            return p;
        }

        public static void Cleanup(string root)
        {
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // ── Lights ───────────────────────────────────────────────────────────────────

    [TestFixture]
    public class InMemoryLightInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private InMemoryLightInstanceRepository _repo;
        // Fresh repo per test so tests do not share state via the dictionary
        // (NUnit runs them alphabetically; without isolation, NullPayload_*
        // pollutes the empty-base slot for ReadRawJson_MissingWorld).
        [SetUp] public void SetUp() => _repo = new InMemoryLightInstanceRepository();
        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
    }

    [TestFixture]
    public class JsonFileLightInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private string _tempRoot;
        private JsonFileLightInstanceRepository _repo;

        [SetUp] public void SetUp()
        {
            _tempRoot = TempRootHelper.Create("lights");
            _repo = new JsonFileLightInstanceRepository(_tempRoot);
        }

        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
        protected override void   OnTearDown()                          => TempRootHelper.Cleanup(_tempRoot);
    }

    // ── Spawners ─────────────────────────────────────────────────────────────────

    [TestFixture]
    public class InMemorySpawnerInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private InMemorySpawnerInstanceRepository _repo;
        [SetUp] public void SetUp() => _repo = new InMemorySpawnerInstanceRepository();
        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
    }

    [TestFixture]
    public class JsonFileSpawnerInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private string _tempRoot;
        private JsonFileSpawnerInstanceRepository _repo;

        [SetUp] public void SetUp()
        {
            _tempRoot = TempRootHelper.Create("spawners");
            _repo = new JsonFileSpawnerInstanceRepository(_tempRoot);
        }

        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
        protected override void   OnTearDown()                          => TempRootHelper.Cleanup(_tempRoot);
    }

    // ── Particles ────────────────────────────────────────────────────────────────

    [TestFixture]
    public class InMemoryParticleInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private InMemoryParticleInstanceRepository _repo;
        [SetUp] public void SetUp() => _repo = new InMemoryParticleInstanceRepository();
        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
    }

    [TestFixture]
    public class JsonFileParticleInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private string _tempRoot;
        private JsonFileParticleInstanceRepository _repo;

        [SetUp] public void SetUp()
        {
            _tempRoot = TempRootHelper.Create("particles");
            _repo = new JsonFileParticleInstanceRepository(_tempRoot);
        }

        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
        protected override void   OnTearDown()                          => TempRootHelper.Cleanup(_tempRoot);
    }

    // ── Path-layout test pinned on one impl (others share base behaviour) ────────

    [TestFixture]
    public class FlatStreamingPathLayoutTests
    {
        private string _tempRoot;
        private JsonFileLightInstanceRepository _repo;

        [SetUp]    public void SetUp()    { _tempRoot = TempRootHelper.Create("layout"); _repo = new JsonFileLightInstanceRepository(_tempRoot); }
        [TearDown] public void TearDown() => TempRootHelper.Cleanup(_tempRoot);

        [Test] public void BaseWorld_UsesLegacyFlatLayout()
        {
            string p = _repo.PathFor(WorldId.Base);
            StringAssert.Contains("Lights", p);
            StringAssert.Contains("light_instances.json", p);
            StringAssert.DoesNotContain("Worlds", p);
        }

        [Test] public void NonBaseWorld_NestsUnderWorldsSlug()
        {
            var alt = new WorldId(System.Guid.NewGuid(), "the_abyss");
            string p = _repo.PathFor(alt);
            StringAssert.Contains(Path.Combine("Worlds", "the_abyss", "Lights"), p);
        }
    }
}
