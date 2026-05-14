using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// EditMode tests for <see cref="Health"/>: damage / kill / overkill clamp /
    /// invincibility / heal / event firing.
    ///
    /// Migrated from <c>PlayMode/Gameplay/CombatSystemPlayTests.cs</c>. <c>Health</c>
    /// is a MonoBehaviour but its API surface is synchronous — <c>Initialize</c>
    /// seeds state explicitly so the original tests' <c>yield return null</c> was
    /// only there to let Awake run, which <c>AddComponent</c> already drives in
    /// EditMode. No <see cref="Time"/> dependency lives in this slice.
    /// </summary>
    [TestFixture]
    public class HealthTests
    {
        private Health CreateHealth(int maxHp = 100)
        {
            var go = new GameObject("Entity");
            var h = go.AddComponent<Health>();
            h.Initialize(maxHp);
            return h;
        }

        private static void Destroy(Health h)
        {
            if (h != null) Object.DestroyImmediate(h.gameObject);
        }

        [Test]
        public void TakeDamage_ReducesHp()
        {
            var h = CreateHealth(100);
            try
            {
                h.TakeDamage(30);
                Assert.AreEqual(70, h.CurrentHp);
                Assert.IsFalse(h.IsDead);
            }
            finally { Destroy(h); }
        }

        [Test]
        public void TakeDamage_KillsAtZero()
        {
            var h = CreateHealth(50);
            try
            {
                bool deathFired = false;
                h.OnDeath += () => deathFired = true;

                h.TakeDamage(50);
                Assert.AreEqual(0, h.CurrentHp);
                Assert.IsTrue(h.IsDead);
                Assert.IsTrue(deathFired);
            }
            finally { Destroy(h); }
        }

        [Test]
        public void TakeDamage_OverkillClampsToZero()
        {
            var h = CreateHealth(30);
            try
            {
                h.TakeDamage(100);
                Assert.AreEqual(0, h.CurrentHp);
                Assert.IsTrue(h.IsDead);
            }
            finally { Destroy(h); }
        }

        [Test]
        public void OnHpChanged_FiresWithCorrectValues()
        {
            var h = CreateHealth(100);
            try
            {
                int receivedCurrent = -1, receivedMax = -1;
                h.OnHpChanged += (cur, max) =>
                {
                    receivedCurrent = cur;
                    receivedMax = max;
                };

                h.TakeDamage(25);
                Assert.AreEqual(75, receivedCurrent);
                Assert.AreEqual(100, receivedMax);
            }
            finally { Destroy(h); }
        }

        [Test]
        public void Heal_RestoresHp()
        {
            var h = CreateHealth(100);
            try
            {
                h.TakeDamage(60);
                Assert.AreEqual(40, h.CurrentHp);

                h.Heal(30);
                Assert.AreEqual(70, h.CurrentHp);
            }
            finally { Destroy(h); }
        }

        [Test]
        public void Heal_ClampsToMax()
        {
            var h = CreateHealth(100);
            try
            {
                h.TakeDamage(10);
                h.Heal(999);
                Assert.AreEqual(100, h.CurrentHp);
            }
            finally { Destroy(h); }
        }

        [Test]
        public void Invincible_PreventsDamage()
        {
            var h = CreateHealth(100);
            try
            {
                h.SetInvincible(true);
                h.TakeDamage(999);
                Assert.AreEqual(100, h.CurrentHp);
                Assert.IsFalse(h.IsDead);

                h.SetInvincible(false);
                h.TakeDamage(10);
                Assert.AreEqual(90, h.CurrentHp);
            }
            finally { Destroy(h); }
        }
    }
}
