using System.IO;
using System.Linq;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    [TestFixture]
    public class JsonFileChunkDeltaRepositoryTests : ChunkDeltaRepositoryContractTests
    {
        private string _tempRoot;

        protected override IChunkDeltaRepository CreateRepo()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(),
                "valkur_chunk_delta_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            return new JsonFileChunkDeltaRepository(_tempRoot);
        }

        protected override void OnTearDown()
        {
            if (!string.IsNullOrEmpty(_tempRoot) && Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
