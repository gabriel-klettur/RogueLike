using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.Infrastructure.Persistence.Repositories
{
    [TestFixture]
    public class InMemoryZoneDatabaseRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private InMemoryZoneDatabaseRepository _repo;
        [SetUp] public void SetUp() => _repo = new InMemoryZoneDatabaseRepository();
        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
    }

    [TestFixture]
    public class JsonFileZoneDatabaseRepositoryTests : FlatJsonInstanceRepositoryContractTests
    {
        private string _tempRoot;
        private JsonFileZoneDatabaseRepository _repo;

        [SetUp] public void SetUp()
        {
            _tempRoot = TempRootHelper.Create("zonedb");
            _repo = new JsonFileZoneDatabaseRepository(_tempRoot);
        }

        protected override bool   Exists(WorldId w)                    => _repo.Exists(w);
        protected override string ReadRawJson(WorldId w)               => _repo.ReadRawJson(w);
        protected override void   WriteRawJson(WorldId w, string json) => _repo.WriteRawJson(w, json);
        protected override void   OnTearDown()                          => TempRootHelper.Cleanup(_tempRoot);

        [Test]
        public void PathFor_BaseWorld_PointsAtMapsZonesDatabase()
        {
            string p = _repo.PathFor(WorldId.Base);
            StringAssert.Contains("Maps", p);
            StringAssert.Contains("zones_database.json", p);
            StringAssert.DoesNotContain("Worlds", p);
        }
    }
}
