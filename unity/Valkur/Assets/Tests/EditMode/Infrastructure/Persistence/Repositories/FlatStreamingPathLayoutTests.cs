using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
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
