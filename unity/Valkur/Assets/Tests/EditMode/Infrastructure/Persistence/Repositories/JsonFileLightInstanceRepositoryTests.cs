using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    [TestFixture]
    public class JsonFileLightInstanceRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private string _tempRoot;
        private JsonFileLightInstanceRepository _repo;

        [SetUp] public void SetUp()
        {
            _tempRoot = TempRootHelper.Create("lights");
            _repo = new JsonFileLightInstanceRepository(_tempRoot);
        }

        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
        protected override void   OnTearDown()                          => TempRootHelper.Cleanup(_tempRoot);
    }
}
