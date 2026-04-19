using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode
{
    public class HealthTests
    {
        private Health CreateHealth(int maxHp = 100)
        {
            var go = new GameObject("TestEntity");
            var h = go.AddComponent<Health>();
            h.Initialize(maxHp);
            return h;
        }

        private void Cleanup(Health h)
        {
            Object.DestroyImmediate(h.gameObject);
        }

        // --- Initialization ---

        [Test]
        public void Initialize_SetsMaxAndCurrentHp()
        {
            var h = CreateHealth(200);
            Assert.AreEqual(200, h.MaxHp);
            Assert.AreEqual(200, h.CurrentHp);
            Assert.IsFalse(h.IsDead);
            Assert.AreEqual(1f, h.NormalizedHp, 0.001f);
            Cleanup(h);
        }

        [Test]
        public void Initialize_CalledExplicitly_SetsCurrentToMax()
        {
            var go = new GameObject("TestEntity");
            var h = go.AddComponent<Health>();
            h.Initialize(75);
            Assert.AreEqual(75, h.MaxHp);
            Assert.AreEqual(75, h.CurrentHp);
            Cleanup(h);
        }

        // --- TakeDamage ---

        [Test]
        public void TakeDamage_ReducesHp()
        {
            var h = CreateHealth(100);
            h.TakeDamage(30);
            Assert.AreEqual(70, h.CurrentHp);
            Cleanup(h);
        }

        [Test]
        public void TakeDamage_ClampsToZero()
        {
            var h = CreateHealth(50);
            h.TakeDamage(999);
            Assert.AreEqual(0, h.CurrentHp);
            Assert.IsTrue(h.IsDead);
            Cleanup(h);
        }

        [Test]
        public void TakeDamage_ZeroAmount_DoesNothing()
        {
            var h = CreateHealth(100);
            h.TakeDamage(0);
            Assert.AreEqual(100, h.CurrentHp);
            Cleanup(h);
        }

        [Test]
        public void TakeDamage_NegativeAmount_DoesNothing()
        {
            var h = CreateHealth(100);
            h.TakeDamage(-10);
            Assert.AreEqual(100, h.CurrentHp);
            Cleanup(h);
        }

        [Test]
        public void TakeDamage_WhenDead_DoesNothing()
        {
            var h = CreateHealth(10);
            h.TakeDamage(10);
            Assert.IsTrue(h.IsDead);
            h.TakeDamage(5);
            Assert.AreEqual(0, h.CurrentHp);
            Cleanup(h);
        }

        // --- Heal ---

        [Test]
        public void Heal_RestoresHp()
        {
            var h = CreateHealth(100);
            h.TakeDamage(40);
            h.Heal(20);
            Assert.AreEqual(80, h.CurrentHp);
            Cleanup(h);
        }

        [Test]
        public void Heal_ClampsToMax()
        {
            var h = CreateHealth(100);
            h.TakeDamage(10);
            h.Heal(999);
            Assert.AreEqual(100, h.CurrentHp);
            Cleanup(h);
        }

        [Test]
        public void Heal_WhenDead_DoesNothing()
        {
            var h = CreateHealth(10);
            h.TakeDamage(10);
            Assert.IsTrue(h.IsDead);
            h.Heal(5);
            Assert.AreEqual(0, h.CurrentHp);
            Cleanup(h);
        }

        [Test]
        public void Heal_ZeroAmount_DoesNothing()
        {
            var h = CreateHealth(100);
            h.TakeDamage(20);
            h.Heal(0);
            Assert.AreEqual(80, h.CurrentHp);
            Cleanup(h);
        }

        // --- NormalizedHp ---

        [Test]
        public void NormalizedHp_ReturnsCorrectRatio()
        {
            var h = CreateHealth(200);
            h.TakeDamage(50);
            Assert.AreEqual(0.75f, h.NormalizedHp, 0.001f);
            Cleanup(h);
        }

        // --- Events ---

        [Test]
        public void OnHpChanged_FiresOnDamage()
        {
            var h = CreateHealth(100);
            int firedCurrent = -1, firedMax = -1;
            h.OnHpChanged += (cur, max) => { firedCurrent = cur; firedMax = max; };
            h.TakeDamage(25);
            Assert.AreEqual(75, firedCurrent);
            Assert.AreEqual(100, firedMax);
            Cleanup(h);
        }

        [Test]
        public void OnHpChanged_FiresOnHeal()
        {
            var h = CreateHealth(100);
            h.TakeDamage(50);
            int firedCurrent = -1;
            h.OnHpChanged += (cur, max) => { firedCurrent = cur; };
            h.Heal(20);
            Assert.AreEqual(70, firedCurrent);
            Cleanup(h);
        }

        [Test]
        public void OnDamaged_FiresWithCorrectAmount()
        {
            var h = CreateHealth(100);
            int firedAmount = -1;
            h.OnDamaged += (amt) => { firedAmount = amt; };
            h.TakeDamage(33);
            Assert.AreEqual(33, firedAmount);
            Cleanup(h);
        }

        [Test]
        public void OnDeath_FiresWhenHpReachesZero()
        {
            var h = CreateHealth(10);
            bool deathFired = false;
            h.OnDeath += () => { deathFired = true; };
            h.TakeDamage(10);
            Assert.IsTrue(deathFired);
            Cleanup(h);
        }

        [Test]
        public void OnDeath_DoesNotFireOnNonLethalDamage()
        {
            var h = CreateHealth(100);
            bool deathFired = false;
            h.OnDeath += () => { deathFired = true; };
            h.TakeDamage(50);
            Assert.IsFalse(deathFired);
            Cleanup(h);
        }

        // --- Reinitialize ---

        [Test]
        public void Initialize_ResetsAfterDamage()
        {
            var h = CreateHealth(100);
            h.TakeDamage(80);
            h.Initialize(150);
            Assert.AreEqual(150, h.MaxHp);
            Assert.AreEqual(150, h.CurrentHp);
            Assert.IsFalse(h.IsDead);
            Cleanup(h);
        }
    }
}
