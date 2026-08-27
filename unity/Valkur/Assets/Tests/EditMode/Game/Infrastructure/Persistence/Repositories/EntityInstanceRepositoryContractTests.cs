using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.Infrastructure.Persistence.Repositories
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  IEntityInstanceRepository (StreamingAssets/Entities/entities_instances.json —
    //  monsters placed through the Entities runtime editor, F5) shares the same
    //  Read/Write/Exists raw-JSON contract every other flat-file instances
    //  repository does. Reuses FlatJsonInstanceRepositoryContractTests +
    //  TempRootHelper from InstanceRepositoriesContractTests.cs (same namespace,
    //  both public) rather than duplicating the fixture shape.
    // ─────────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class InMemoryEntityInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private InMemoryEntityInstanceRepository _repo;

        // Fresh repo per test so tests do not share state via the dictionary.
        [SetUp] public void SetUp() => _repo = new InMemoryEntityInstanceRepository();

        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
    }

    [TestFixture]
    public class JsonFileEntityInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private string _tempRoot;
        private JsonFileEntityInstanceRepository _repo;

        [SetUp] public void SetUp()
        {
            _tempRoot = TempRootHelper.Create("entities");
            _repo = new JsonFileEntityInstanceRepository(_tempRoot);
        }

        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
        protected override void   OnTearDown()                          => TempRootHelper.Cleanup(_tempRoot);
    }

    [TestFixture]
    public class EntityInstanceRepositoryPathLayoutTests
    {
        private string _tempRoot;
        private JsonFileEntityInstanceRepository _repo;

        [SetUp]    public void SetUp()    { _tempRoot = TempRootHelper.Create("entities_layout"); _repo = new JsonFileEntityInstanceRepository(_tempRoot); }
        [TearDown] public void TearDown() => TempRootHelper.Cleanup(_tempRoot);

        [Test] public void BaseWorld_UsesLegacyFlatLayout()
        {
            string p = _repo.PathFor(WorldId.Base);
            StringAssert.Contains("Entities", p);
            StringAssert.Contains("entities_instances.json", p);
            StringAssert.DoesNotContain("Worlds", p);
        }

        [Test] public void NonBaseWorld_NestsUnderWorldsSlug()
        {
            var alt = new WorldId(System.Guid.NewGuid(), "the_abyss");
            string p = _repo.PathFor(alt);
            StringAssert.Contains(Path.Combine("Worlds", "the_abyss", "Entities"), p);
        }
    }
}
