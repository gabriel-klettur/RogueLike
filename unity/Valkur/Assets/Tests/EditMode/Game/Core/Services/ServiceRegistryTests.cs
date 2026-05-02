using NUnit.Framework;
using Valkur.Core;
using Valkur.Core.Services;

namespace Valkur.Tests.EditMode.Core.Services
{
    /// <summary>
    /// Pins the contract of <see cref="IServiceRegistry"/>:
    ///  • <see cref="GlobalServiceRegistry"/> is a transparent adapter over the
    ///    legacy static <see cref="ServiceLocator"/> — both views observe the
    ///    same dictionary.
    ///  • <see cref="ScopedServiceRegistry"/> is isolated — Register on one
    ///    scope must not leak to the global ServiceLocator nor to a sibling
    ///    scope.
    /// </summary>
    [TestFixture]
    public class ServiceRegistryTests
    {
        public interface IFooService { string Tag { get; } }
        public sealed class FooService : IFooService { public string Tag { get; set; } }

        [TearDown] public void TearDown() => ServiceLocator.Clear();

        [Test]
        public void GlobalAdapter_ReadsWhatServiceLocatorWrote()
        {
            ServiceLocator.Register<IFooService>(new FooService { Tag = "via-locator" });
            Assert.AreEqual("via-locator", GlobalServiceRegistry.Instance.Get<IFooService>().Tag);
        }

        [Test]
        public void GlobalAdapter_WritesAreVisibleViaServiceLocator()
        {
            GlobalServiceRegistry.Instance.Register<IFooService>(new FooService { Tag = "via-adapter" });
            Assert.AreEqual("via-adapter", ServiceLocator.Get<IFooService>().Tag);
        }

        [Test]
        public void GlobalAdapter_TryGetReturnsFalseWhenAbsent()
        {
            Assert.IsFalse(GlobalServiceRegistry.Instance.TryGet<IFooService>(out var s));
            Assert.IsNull(s);
        }

        [Test]
        public void ScopedRegistry_DoesNotLeakToGlobal()
        {
            var scope = new ScopedServiceRegistry();
            scope.Register<IFooService>(new FooService { Tag = "scoped" });
            Assert.IsNull(ServiceLocator.Get<IFooService>(),
                "ScopedServiceRegistry must not leak into the global ServiceLocator.");
            Assert.AreEqual("scoped", scope.Get<IFooService>().Tag);
        }

        [Test]
        public void ScopedRegistry_TwoScopesAreIndependent()
        {
            var a = new ScopedServiceRegistry();
            var b = new ScopedServiceRegistry();
            a.Register<IFooService>(new FooService { Tag = "A" });
            b.Register<IFooService>(new FooService { Tag = "B" });
            Assert.AreEqual("A", a.Get<IFooService>().Tag);
            Assert.AreEqual("B", b.Get<IFooService>().Tag);
        }

        [Test]
        public void ScopedRegistry_UnregisterRemovesEntry()
        {
            var scope = new ScopedServiceRegistry();
            scope.Register<IFooService>(new FooService { Tag = "x" });
            scope.Unregister<IFooService>();
            Assert.IsFalse(scope.TryGet<IFooService>(out _));
        }
    }
}
