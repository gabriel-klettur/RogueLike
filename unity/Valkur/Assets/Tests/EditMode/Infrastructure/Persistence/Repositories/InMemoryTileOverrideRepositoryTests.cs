using System.IO;
using System.Linq;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    /// <summary>Contract pinned for the in-memory repository (zero I/O).</summary>
    [TestFixture]
    public class InMemoryTileOverrideRepositoryTests : TileOverrideRepositoryContractTests
    {
        protected override ITileOverrideRepository CreateRepo() => new InMemoryTileOverrideRepository();
    }
}
