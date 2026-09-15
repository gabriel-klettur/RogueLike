using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    /// <summary>Contract pinned for the JSON-file repository against a scratch
    /// directory so the test never touches the user's StreamingAssets.</summary>
    [TestFixture]
    public class JsonFileBuildingInstanceRepositoryTests : BuildingInstanceRepositoryContractTests
    {
        private string _tempRoot;

        protected override IBuildingInstanceRepository CreateRepo()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(),
                "valkur_buildings_repo_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            return new JsonFileBuildingInstanceRepository(_tempRoot);
        }

        protected override void OnTearDown()
        {
            if (!string.IsNullOrEmpty(_tempRoot) && Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        // JSON-specific assertions

        [Test]
        public void PathFor_BaseWorld_UsesLegacyFlatLayout()
        {
            var jsonRepo = (JsonFileBuildingInstanceRepository)Repo;
            string path = jsonRepo.PathFor(WorldId.Base);
            StringAssert.Contains("Buildings", path);
            StringAssert.Contains("buildings_instances.json", path);
            StringAssert.DoesNotContain("Worlds", path,
                "Base world must keep the legacy flat path so existing builds " +
                "stay byte-compatible.");
        }

        [Test]
        public void PathFor_NonBaseWorld_NestsUnderWorldsSlug()
        {
            var jsonRepo = (JsonFileBuildingInstanceRepository)Repo;
            var alt = new WorldId(System.Guid.NewGuid(), "the_abyss");
            string path = jsonRepo.PathFor(alt);
            StringAssert.Contains(Path.Combine("Worlds", "the_abyss", "Buildings"), path);
        }
    }
}
