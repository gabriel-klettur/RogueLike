using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    [TestFixture]
    public class JsonFileItemDropRepositoryTests : ItemDropRepositoryContractTests
    {
        private string _tempRoot;

        protected override IItemDropRepository CreateRepo()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(),
                "valkur_item_drops_repo_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            return new JsonFileItemDropRepository(_tempRoot);
        }

        protected override void OnTearDown()
        {
            if (!string.IsNullOrEmpty(_tempRoot) && Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        [Test]
        public void PathFor_BaseWorld_UsesLegacyFlatLayout()
        {
            var jsonRepo = (JsonFileItemDropRepository)Repo;
            string path = jsonRepo.PathFor(WorldId.Base);
            StringAssert.Contains("Items", path);
            StringAssert.Contains("item_drops.json", path);
            StringAssert.DoesNotContain("Worlds", path,
                "Base world must keep the legacy flat path so existing builds stay byte-compatible.");
        }

        [Test]
        public void PathFor_NonBaseWorld_NestsUnderWorldsSlug()
        {
            var jsonRepo = (JsonFileItemDropRepository)Repo;
            var alt = new WorldId(System.Guid.NewGuid(), "the_void");
            string path = jsonRepo.PathFor(alt);
            StringAssert.Contains(Path.Combine("Worlds", "the_void", "Items"), path);
        }

        [Test]
        public void CustomSubdir_RoutesUnderConfiguredFolder()
        {
            // Subdir + filename are user-configurable so the same impl can serve
            // the per-run save folder in Phase B without forking the class.
            var custom = new JsonFileItemDropRepository(_tempRoot, "Saves/run-42", "world_drops.json");
            custom.WriteRawJson(WorldId.Base, "{\"drops\":[]}");
            string path = custom.PathFor(WorldId.Base).Replace('\\', '/');
            StringAssert.Contains("Saves/run-42/world_drops.json", path);
        }
    }
}
