using System.IO;
using System.Linq;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    // ── Concrete fixtures ─────────────────────────────────────────────────────────

    [TestFixture]
    public class InMemoryChunkDeltaRepositoryTests : ChunkDeltaRepositoryContractTests
    {
        protected override IChunkDeltaRepository CreateRepo() => new InMemoryChunkDeltaRepository();
    }
}
