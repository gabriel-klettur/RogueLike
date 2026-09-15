using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
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
}
