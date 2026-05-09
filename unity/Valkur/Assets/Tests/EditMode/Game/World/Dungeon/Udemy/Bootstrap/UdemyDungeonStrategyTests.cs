using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Strategy;
using Valkur.Gameplay.World.Dungeon.Udemy.Bootstrap;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Bootstrap
{
    public class UdemyDungeonStrategyTests
    {
        [Test]
        public void Id_IsUdemy()
        {
            var strategy = new UdemyDungeonStrategy(null, null, null);
            Assert.AreEqual("udemy", strategy.Id);
            Assert.AreEqual(UdemyDungeonStrategy.StrategyId, strategy.Id);
        }

        [Test]
        public void TryGenerate_NullContext_FailsCleanly()
        {
            var strategy = new UdemyDungeonStrategy(null, null, null);
            bool ok = strategy.TryGenerate(null, out var result);
            Assert.IsFalse(ok);
            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.FailureReason);
        }

        [Test]
        public void TryGenerate_NullLevel_FailsCleanly()
        {
            var strategy = new UdemyDungeonStrategy(null, null, null);
            bool ok = strategy.TryGenerate(new DungeonGenerationContext { GridBuilder = null }, out var result);
            Assert.IsFalse(ok);
        }

        [Test]
        public void Cleanup_IsSafe_WhenNothingGenerated()
        {
            var strategy = new UdemyDungeonStrategy(null, null, null);
            Assert.DoesNotThrow(() => strategy.Cleanup());
        }

        [Test]
        public void Strategy_IsRegisterable_AndResolvableById()
        {
            DungeonStrategyResolver.ClearForTests();
            var level = ScriptableObject.CreateInstance<DungeonLevelSO>();
            var nodeTypes = ScriptableObject.CreateInstance<RoomNodeTypeListSO>();
            var config = ScriptableObject.CreateInstance<DungeonConfigSO>();
            try
            {
                var strategy = new UdemyDungeonStrategy(level, nodeTypes, config);
                DungeonStrategyResolver.Register(strategy);

                Assert.IsTrue(DungeonStrategyResolver.IsRegistered("udemy"));
                Assert.AreSame(strategy, DungeonStrategyResolver.Resolve("udemy"));
            }
            finally
            {
                DungeonStrategyResolver.ClearForTests();
                Object.DestroyImmediate(level);
                Object.DestroyImmediate(nodeTypes);
                Object.DestroyImmediate(config);
            }
        }
    }
}
