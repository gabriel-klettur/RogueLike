using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
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
