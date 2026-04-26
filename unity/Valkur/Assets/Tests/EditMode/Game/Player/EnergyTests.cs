using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;
using Valkur.Gameplay.Player;

namespace Valkur.Tests.EditMode.Game.Player
{
    public class EnergyTests
    {
        private Energy CreateEnergy(int max = 100)
        {
            var go = new GameObject("TestEntity");
            var e = go.AddComponent<Energy>();
            e.Initialize(max);
            return e;
        }

        private void Cleanup(Energy e)
        {
            Object.DestroyImmediate(e.gameObject);
        }

        [Test]
        public void Initialize_SetsMaxAndCurrent()
        {
            var e = CreateEnergy(80);
            Assert.AreEqual(80, e.Max);
            Assert.AreEqual(80, e.Current);
            Assert.AreEqual(1f, e.Normalized, 0.001f);
            Cleanup(e);
        }

        [Test]
        public void Spend_ReducesEnergy()
        {
            var e = CreateEnergy(100);
            bool ok = e.Spend(30);
            Assert.IsTrue(ok);
            Assert.AreEqual(70, e.Current);
            Cleanup(e);
        }

        [Test]
        public void Spend_InsufficientEnergy_ReturnsFalse()
        {
            var e = CreateEnergy(10);
            bool ok = e.Spend(20);
            Assert.IsFalse(ok);
            Assert.AreEqual(10, e.Current);
            Cleanup(e);
        }

        [Test]
        public void Spend_ZeroAmount_ReturnsFalse()
        {
            var e = CreateEnergy(50);
            bool ok = e.Spend(0);
            Assert.IsFalse(ok);
            Assert.AreEqual(50, e.Current);
            Cleanup(e);
        }

        [Test]
        public void Restore_AddsEnergy_ClampedToMax()
        {
            var e = CreateEnergy(100);
            e.Spend(60);
            e.Restore(200);
            Assert.AreEqual(100, e.Current);
            Cleanup(e);
        }

        [Test]
        public void Restore_ZeroAmount_DoesNothing()
        {
            var e = CreateEnergy(100);
            e.Spend(20);
            e.Restore(0);
            Assert.AreEqual(80, e.Current);
            Cleanup(e);
        }

        [Test]
        public void Normalized_ReturnsCorrectRatio()
        {
            var e = CreateEnergy(200);
            e.Spend(50);
            Assert.AreEqual(150f / 200f, e.Normalized, 0.001f);
            Cleanup(e);
        }
    }
}
