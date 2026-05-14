using NUnit.Framework;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Core.Services
{
    /// <summary>
    /// Sync EditMode tests for the static <see cref="ServiceLocator"/> API.
    /// Migrated from <c>PlayMode/Core/ServiceLocatorPlayTests.cs</c>: the
    /// register/get/unregister/clear paths are pure dictionary operations with
    /// no <c>MonoBehaviour</c> lifecycle, no <c>Time</c>, and no scene —
    /// running them in PlayMode paid the Play-Mode bootstrap cost for zero
    /// added coverage. The three <c>GameDirector</c> tests that legitimately
    /// need <c>Time.timeScale</c> + Awake/OnDestroy lifecycle stay in PlayMode.
    /// </summary>
    [TestFixture]
    public class ServiceLocatorTests
    {
        [SetUp]
        public void SetUp() => ServiceLocator.Clear();

        [TearDown]
        public void TearDown() => ServiceLocator.Clear();

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
            Assert.IsNull(ServiceLocator.Get<IDummyService>());
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

        // ── Helpers ─────────────────────────────────────────────────────────────

        private interface IDummyService { }
        private sealed class DummyService : IDummyService { }
        private interface IDummyService2 { }
        private sealed class DummyService2 : IDummyService2 { }
    }
}
