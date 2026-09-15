using System.IO;
using System.Linq;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    /// <summary>Contract pinned for the JSON-file repository against a scratch
    /// directory so the test never touches the user's persistentDataPath.</summary>
    [TestFixture]
    public class JsonFileTileOverrideRepositoryTests : TileOverrideRepositoryContractTests
    {
        private string _tempDir;

        protected override ITileOverrideRepository CreateRepo()
        {
            _tempDir = Path.Combine(Path.GetTempPath(),
                "valkur_tileoverride_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            return new JsonFileTileOverrideRepository(_tempDir);
        }

        protected override void OnTearDown()
        {
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
    }
}
