using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;
using Valkur.Gameplay.Player;

namespace Valkur.Tests.EditMode.Gameplay.Player
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
        public void Drain_KeepsTheFraction_SoARateIsNeverFree()
        {
            var e = CreateEnergy(100);
            for (int i = 0; i < 60; i++) e.Drain(0.15f);   // one second at 9/s, 60 fps
            Assert.AreEqual(91f, e.Exact, 0.01f);
            Assert.AreEqual(91, e.Current);
            Cleanup(e);
        }

        [Test]
        public void DrainAndRegenerate_Clamp()
        {
            var e = CreateEnergy(50);
            Assert.AreEqual(50f, e.Drain(80f), 1e-4f);
            Assert.IsTrue(e.IsEmpty);
            e.Regenerate(500f);
            Assert.IsTrue(e.IsFull);
            Assert.AreEqual(50f, e.Exact, 1e-4f);
            Cleanup(e);
        }

        [Test]
        public void SetMax_KeepsAFullPoolFull_AndClampsAPartialOne()
        {
            var e = CreateEnergy(100);
            e.SetMax(104);
            Assert.AreEqual(104, e.Current, "a class seeded at 104 must not boot at 100/104");

            e.Spend(50);
            e.SetMax(30);
            Assert.AreEqual(30, e.Current, "a pool above a lowered cap is clamped to it");
            e.Spend(10);
            e.SetMax(80);
            Assert.AreEqual(20, e.Current, "raising the cap does not refill a partial pool");
            Cleanup(e);
        }

        [Test]
        public void Changed_IsRaised_WithTheFill()
        {
            var e = CreateEnergy(100);
            float last = -1f;
            e.Changed += v => last = v;
            e.Drain(25f);
            Assert.AreEqual(0.75f, last, 1e-4f);
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
