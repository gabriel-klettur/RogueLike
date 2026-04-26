using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;

namespace Valkur.Tests.PlayMode.Core
{
    /// <summary>
    /// PlayMode tests for ServiceLocator and GameDirector registration patterns.
    /// Validates service registration, retrieval, and cleanup.
    /// </summary>
    public class ServiceLocatorPlayTests
    {
        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Clear();
        }

        // ── Basic Registration ──

        [Test]
        public void Register_ThenGet_ReturnsSameInstance()
        {
            var service = new DummyService();
            ServiceLocator.Register<IDummyService>(service);

            var retrieved = ServiceLocator.Get<IDummyService>();
            Assert.AreSame(service, retrieved);
        }

        [Test]
        public void Get_Unregistered_ReturnsNull()
        {
            var result = ServiceLocator.Get<IDummyService>();
            Assert.IsNull(result);
        }

        [Test]
        public void TryGet_Registered_ReturnsTrue()
        {
            ServiceLocator.Register<IDummyService>(new DummyService());
            bool found = ServiceLocator.TryGet<IDummyService>(out var service);
            Assert.IsTrue(found);
            Assert.IsNotNull(service);
        }

        [Test]
        public void TryGet_Unregistered_ReturnsFalse()
        {
            bool found = ServiceLocator.TryGet<IDummyService>(out var service);
            Assert.IsFalse(found);
            Assert.IsNull(service);
        }

        [Test]
        public void Unregister_RemovesService()
        {
            ServiceLocator.Register<IDummyService>(new DummyService());
            ServiceLocator.Unregister<IDummyService>();
            Assert.IsNull(ServiceLocator.Get<IDummyService>());
        }

        [Test]
        public void Clear_RemovesAllServices()
        {
            ServiceLocator.Register<IDummyService>(new DummyService());
            ServiceLocator.Register<IDummyService2>(new DummyService2());
            ServiceLocator.Clear();
            Assert.IsNull(ServiceLocator.Get<IDummyService>());
            Assert.IsNull(ServiceLocator.Get<IDummyService2>());
        }

        // ── GameDirector Integration ──

        [UnityTest]
        public IEnumerator GameDirector_RegistersSelfInServiceLocator()
        {
            var go = new GameObject("GameDirector");
            go.AddComponent<GameDirector>();

            yield return null;

            var director = ServiceLocator.Get<GameDirector>();
            Assert.IsNotNull(director, "GameDirector should register itself in ServiceLocator");
            Assert.AreEqual(GameDirector.Instance, director);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameDirector_UnregistersOnDestroy()
        {
            var go = new GameObject("GameDirector");
            go.AddComponent<GameDirector>();

            yield return null;
            Assert.IsNotNull(ServiceLocator.Get<GameDirector>());

            Object.Destroy(go);
            yield return null;

            Assert.IsNull(ServiceLocator.Get<GameDirector>(),
                "GameDirector should unregister from ServiceLocator on destroy");
        }

        [UnityTest]
        public IEnumerator GameDirector_Pause_SetsTimeScale()
        {
            var go = new GameObject("GameDirector");
            var director = go.AddComponent<GameDirector>();

            yield return null;

            director.SetPaused(true);
            Assert.IsTrue(director.IsPaused);
            Assert.AreEqual(0f, Time.timeScale);

            director.SetPaused(false);
            Assert.IsFalse(director.IsPaused);
            Assert.AreEqual(1f, Time.timeScale);

            Object.Destroy(go);
        }

        // ── Helpers ──

        private interface IDummyService { }
        private class DummyService : IDummyService { }
        private interface IDummyService2 { }
        private class DummyService2 : IDummyService2 { }
    }
}
