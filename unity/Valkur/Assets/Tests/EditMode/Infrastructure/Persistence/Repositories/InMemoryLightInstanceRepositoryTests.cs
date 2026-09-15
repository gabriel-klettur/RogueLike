using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
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
}
