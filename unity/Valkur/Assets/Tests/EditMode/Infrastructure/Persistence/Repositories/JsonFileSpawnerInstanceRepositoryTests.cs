using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    [TestFixture]
    public class JsonFileSpawnerInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private string _tempRoot;
        private JsonFileSpawnerInstanceRepository _repo;

        [SetUp] public void SetUp()
        {
            _tempRoot = TempRootHelper.Create("spawners");
            _repo = new JsonFileSpawnerInstanceRepository(_tempRoot);
        }

        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
        protected override void   OnTearDown()                          => TempRootHelper.Cleanup(_tempRoot);
    }
}
