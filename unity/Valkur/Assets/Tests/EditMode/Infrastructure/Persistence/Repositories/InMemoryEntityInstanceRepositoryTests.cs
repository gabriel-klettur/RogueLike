using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;
namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
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
}
