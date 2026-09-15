using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;
using Valkur.Tests.Support;

namespace Valkur.Tests.EditMode.Gameplay.World.Buildings
{
    /// <summary>
    /// Pins the contract that every world-data loader (BuildingLoader,
    /// WorldLightLoader, SpawnerInstanceLoader, ZoneDatabaseLoader) accepts an
    /// injected repository through <c>SetRepository</c>, replacing the
    /// previously-hard-coded <see cref="System.IO.File"/> calls. Without
    /// this contract, tests would have to write into the user's actual
    /// StreamingAssets to exercise the loaders — exactly the failure mode
    /// that triggered the persistence-hardening pass.
    ///
    /// The integration test suites for each loader exercise the full
    /// parse/spawn flow; this fixture only proves that injection swaps
    /// the underlying source.
    /// </summary>
    [TestFixture]
    public class LoaderRepositoryInjectionTests
    {
        private static GameObject MakeGo(string name) => new GameObject(name);

        // ── BuildingLoader ───────────────────────────────────────────────────────

        [Test]
        public void BuildingLoader_SetRepository_StoresInjectedInstance()
        {
            var go     = MakeGo("BuildingLoaderTest");
            try
            {
                var loader = go.AddComponent<BuildingLoader>();
                var repo   = new InMemoryBuildingInstanceRepository();

                loader.SetRepository(repo);

                Assert.AreSame(repo, TestReflection.GetField<IBuildingInstanceRepository>(loader, "_repository"),
                    "SetRepository must store the injected handle so subsequent " +
                    "LoadBuildings calls bypass the JSON-file backend.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── WorldLightLoader ─────────────────────────────────────────────────────

        [Test]
        public void WorldLightLoader_SetRepository_StoresInjectedInstance()
        {
            var go     = MakeGo("WorldLightLoaderTest");
            try
            {
                var loader = go.AddComponent<WorldLightLoader>();
                var repo   = new InMemoryLightInstanceRepository();

                loader.SetRepository(repo);

                Assert.AreSame(repo, TestReflection.GetField<ILightInstanceRepository>(loader, "_repository"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── SpawnerInstanceLoader ────────────────────────────────────────────────

        [Test]
        public void SpawnerInstanceLoader_SetRepository_StoresInjectedInstance()
        {
            var go     = MakeGo("SpawnerInstanceLoaderTest");
            try
            {
                var loader = go.AddComponent<SpawnerInstanceLoader>();
                var repo   = new InMemorySpawnerInstanceRepository();

                loader.SetRepository(repo);

                Assert.AreSame(repo, TestReflection.GetField<ISpawnerInstanceRepository>(loader, "_repository"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── ZoneDatabaseLoader ───────────────────────────────────────────────────

        [Test]
        public void ZoneDatabaseLoader_SetRepository_StoresInjectedInstance()
        {
            var go     = MakeGo("ZoneDatabaseLoaderTest");
            try
            {
                var loader = go.AddComponent<ZoneDatabaseLoader>();
                var repo   = new InMemoryZoneDatabaseRepository();

                loader.SetRepository(repo);

                Assert.AreSame(repo, TestReflection.GetField<IZoneDatabaseRepository>(loader, "_repository"));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
