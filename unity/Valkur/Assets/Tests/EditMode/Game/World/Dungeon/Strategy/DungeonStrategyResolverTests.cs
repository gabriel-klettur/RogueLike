using NUnit.Framework;
using Valkur.Gameplay.World.Dungeon.Strategy;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Strategy
{
    public class DungeonStrategyResolverTests
    {
        [SetUp]
        public void SetUp()
        {
            DungeonStrategyResolver.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DungeonStrategyResolver.ClearForTests();
        }

        [Test]
        public void Register_AddsStrategy_AndIsRegisteredReturnsTrue()
        {
            var fake = new FakeStrategy("fake");
            DungeonStrategyResolver.Register(fake);

            Assert.IsTrue(DungeonStrategyResolver.IsRegistered("fake"));
            Assert.AreEqual(1, DungeonStrategyResolver.RegisteredCount);
        }

        [Test]
        public void Resolve_ReturnsRegisteredStrategy_ForExactId()
        {
            var fake = new FakeStrategy("bsp");
            DungeonStrategyResolver.Register(fake);

            Assert.AreSame(fake, DungeonStrategyResolver.Resolve("bsp"));
        }

        [Test]
        public void Resolve_IsCaseInsensitive()
        {
            var fake = new FakeStrategy("Udemy");
            DungeonStrategyResolver.Register(fake);

            Assert.AreSame(fake, DungeonStrategyResolver.Resolve("udemy"));
            Assert.AreSame(fake, DungeonStrategyResolver.Resolve("UDEMY"));
        }

        [Test]
        public void Resolve_FallsBackToDefault_WhenIdUnknown()
        {
            var bsp = new FakeStrategy(DungeonStrategyResolver.DefaultStrategyId);
            DungeonStrategyResolver.Register(bsp);

            var resolved = DungeonStrategyResolver.Resolve("nonexistent");
            Assert.AreSame(bsp, resolved);
        }

        [Test]
        public void Resolve_ReturnsNull_WhenNothingRegistered()
        {
            Assert.IsNull(DungeonStrategyResolver.Resolve("anything"));
        }

        [Test]
        public void Register_ReplacesExistingStrategy_LastWriteWins()
        {
            var first = new FakeStrategy("bsp");
            var second = new FakeStrategy("bsp");

            DungeonStrategyResolver.Register(first);
            DungeonStrategyResolver.Register(second);

            Assert.AreSame(second, DungeonStrategyResolver.Resolve("bsp"));
            Assert.AreEqual(1, DungeonStrategyResolver.RegisteredCount);
        }

        [Test]
        public void Register_ThrowsOnNull()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => DungeonStrategyResolver.Register(null));
        }

        [Test]
        public void Register_ThrowsOnEmptyId()
        {
            Assert.Throws<System.ArgumentException>(
                () => DungeonStrategyResolver.Register(new FakeStrategy("")));
        }

        // Minimal IDungeonStrategy stub for resolver-only tests.
        private sealed class FakeStrategy : IDungeonStrategy
        {
            public FakeStrategy(string id) { Id = id; }
            public string Id { get; }
            public bool TryGenerate(DungeonGenerationContext ctx, out DungeonGenerationResult result)
            {
                result = new DungeonGenerationResult { Success = true };
                return true;
            }
            public void Cleanup() { }
        }
    }
}
