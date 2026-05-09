using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Dungeon.Strategy;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Strategy
{
    /// <summary>
    /// Verifies the BSP strategy adapter preserves the legacy contract:
    /// rejects bad input, exposes the expected id, and produces consistent
    /// world-coord room rectangles via <see cref="BspDungeonResultConverter"/>.
    /// Full end-to-end painting parity is covered by the existing
    /// <c>DungeonGeneratorTests</c> + <c>DungeonLoaderTests</c> suites.
    /// </summary>
    public class BspDungeonStrategyParityTests
    {
        [Test]
        public void Id_IsBsp()
        {
            var strategy = new BspDungeonStrategy(null);
            Assert.AreEqual("bsp", strategy.Id);
            Assert.AreEqual(BspDungeonStrategy.StrategyId, strategy.Id);
        }

        [Test]
        public void TryGenerate_FailsCleanly_OnNullContext()
        {
            var strategy = new BspDungeonStrategy(null);

            bool ok = strategy.TryGenerate(null, out var result);

            Assert.IsFalse(ok);
            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.FailureReason);
        }

        [Test]
        public void TryGenerate_FailsCleanly_OnNullGridBuilder()
        {
            var strategy = new BspDungeonStrategy(null);
            var ctx = new DungeonGenerationContext { GridBuilder = null };

            bool ok = strategy.TryGenerate(ctx, out var result);

            Assert.IsFalse(ok);
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void ToWorldRect_FlipsYAxis_AndAppliesOffset()
        {
            // Generator produces row 0 = top; Unity tilemap row 0 = bottom.
            // A room at gen (x=2..5, y=1..3) inside a 10-tall grid, dungeon offset
            // (100, 200) must land at world (102..105, 200+(10-1-3)..200+(10-1-1))
            //                                     = (102..105, 206..208).
            var genRect = new RectInt(2, 1, 4, 3); // xMin=2, yMin=1, w=4, h=3 → covers y 1..3 inclusive
            var world = BspDungeonResultConverter.ToWorldRect(genRect, genHeight: 10, offX: 100, offY: 200);

            Assert.AreEqual(102, world.xMin);
            Assert.AreEqual(4, world.width);
            Assert.AreEqual(206, world.yMin); // 200 + (10-1-3)
            Assert.AreEqual(3, world.height);
            Assert.AreEqual(208, world.yMax - 1); // 200 + (10-1-1) inclusive
        }

        [Test]
        public void ToWorldRect_ZeroOffset_PreservesXButFlipsY()
        {
            var genRect = new RectInt(0, 0, 5, 5);
            var world = BspDungeonResultConverter.ToWorldRect(genRect, genHeight: 50, offX: 0, offY: 0);

            Assert.AreEqual(0, world.xMin);
            Assert.AreEqual(5, world.width);
            // gen row 0..4 (top of grid) → world rows 45..49 (top of Unity grid).
            Assert.AreEqual(45, world.yMin);
            Assert.AreEqual(49, world.yMax - 1);
        }

        [Test]
        public void Cleanup_IsSafe_WhenNothingGenerated()
        {
            var strategy = new BspDungeonStrategy(null);
            Assert.DoesNotThrow(() => strategy.Cleanup());
        }
    }
}
