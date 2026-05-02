using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Core.Services;
// Alias avoids the namespace clash between the production
// 'Valkur.Core.WorldContext' (which contains the WorldContext class) and the
// test namespace below — without this, every reference to WC.Global
// resolves into the test namespace and fails.
using WC = Valkur.Core.WorldContext.WorldContext;

namespace Valkur.Tests.EditMode.Core.WorldContext
{
    /// <summary>
    /// Pins WorldContext semantics: <see cref="WC.Global"/> wraps the
    /// global registry and resolves to <see cref="WorldId.Base"/>;
    /// <see cref="WC.Scoped"/> creates a fresh isolated registry per
    /// call so two test fixtures never see each other's mocks.
    /// </summary>
    [TestFixture]
    public class WorldContextTests
    {
        public interface IDummy { }
        public sealed class Dummy : IDummy { }

        [Test]
        public void Global_HasBaseWorldAndGlobalRegistry()
        {
            Assert.AreEqual(WorldId.Base, WC.Global.WorldId);
            Assert.AreSame(GlobalServiceRegistry.Instance, WC.Global.Services);
        }

        [Test]
        public void Scoped_DefaultWorldIsBase()
        {
            var ctx = WC.Scoped();
            Assert.AreEqual(WorldId.Base, ctx.WorldId);
            Assert.IsInstanceOf<ScopedServiceRegistry>(ctx.Services);
        }

        [Test]
        public void Scoped_AcceptsCustomWorldId()
        {
            var w = new WorldId(System.Guid.NewGuid(), "dungeon");
            var ctx = WC.Scoped(w);
            Assert.AreEqual(w, ctx.WorldId);
        }

        [Test]
        public void Scoped_TwoCallsReturnIndependentRegistries()
        {
            var a = WC.Scoped();
            var b = WC.Scoped();
            a.Services.Register<IDummy>(new Dummy());
            Assert.IsFalse(b.Services.TryGet<IDummy>(out _),
                "Each Scoped() must produce an isolated registry.");
        }
    }
}
