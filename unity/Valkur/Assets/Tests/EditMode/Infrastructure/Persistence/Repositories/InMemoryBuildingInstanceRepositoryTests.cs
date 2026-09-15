using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    /// <summary>Contract pinned for the in-memory repository (zero I/O).</summary>
    [TestFixture]
    public class InMemoryBuildingInstanceRepositoryTests : BuildingInstanceRepositoryContractTests
    {
        protected override IBuildingInstanceRepository CreateRepo() => new InMemoryBuildingInstanceRepository();
    }
}
