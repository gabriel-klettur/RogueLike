using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    [TestFixture]
    public class InMemoryItemDropRepositoryTests : ItemDropRepositoryContractTests
    {
        protected override IItemDropRepository CreateRepo() => new InMemoryItemDropRepository();
    }
}
