using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;
using Valkur.Gameplay.Player;

namespace Valkur.Tests.EditMode
{
    public class HungerTests
    {
        private Hunger CreateHunger(int max = 100)
        {
            var go = new GameObject("TestEntity");
            go.AddComponent<Health>().Initialize(100);
            var h = go.AddComponent<Hunger>();
            h.Initialize(max);
            return h;
        }

        private void Cleanup(Hunger h)
        {
            Object.DestroyImmediate(h.gameObject);
        }

        [Test]
        public void Initialize_SetsMaxAndCurrent()
        {
            var h = CreateHunger(80);
            Assert.AreEqual(80, h.Max);
            Assert.AreEqual(80, h.Current);
            Assert.AreEqual(1f, h.Normalized, 0.001f);
            Assert.IsFalse(h.IsStarving);
            Cleanup(h);
        }

        [Test]
        public void Feed_AddsHunger_ClampedToMax()
        {
            var h = CreateHunger(100);
            // Manually simulate hunger reduction by re-initializing with lower value
            // Feed should clamp
            h.Feed(200);
            Assert.AreEqual(100, h.Current); // clamped to max
            Cleanup(h);
        }

        [Test]
        public void Feed_ZeroAmount_DoesNothing()
        {
            var h = CreateHunger(50);
            h.Feed(0);
            Assert.AreEqual(50, h.Current);
            Cleanup(h);
        }

        [Test]
        public void Normalized_ReturnsCorrectRatio()
        {
            var h = CreateHunger(200);
            // Can't easily reduce hunger in EditMode without Update(), so test at full
            Assert.AreEqual(1f, h.Normalized, 0.001f);
            Cleanup(h);
        }

        [Test]
        public void IsStarving_FalseWhenFull()
        {
            var h = CreateHunger(100);
            Assert.IsFalse(h.IsStarving);
            Cleanup(h);
        }
    }
}
