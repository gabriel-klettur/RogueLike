using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;

namespace Valkur.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for the Health + Mana combat system.
    /// Validates damage, healing, death, mana consumption, and regen over real frames.
    /// </summary>
    public class CombatSystemPlayTests
    {
        private Health CreateHealth(int maxHp = 100)
        {
            var go = new GameObject("Entity");
            var h = go.AddComponent<Health>();
            h.Initialize(maxHp);
            return h;
        }

        private Mana CreateMana(int maxMana = 100, float regen = 0f)
        {
            var go = new GameObject("Entity");
            var m = go.AddComponent<Mana>();
            m.Initialize(maxMana, regen);
            return m;
        }

        // ── Health Tests ──

        [UnityTest]
        public IEnumerator TakeDamage_ReducesHp_OverFrame()
        {
            var h = CreateHealth(100);
            yield return null;

            h.TakeDamage(30);
            Assert.AreEqual(70, h.CurrentHp);
            Assert.IsFalse(h.IsDead);

            Object.Destroy(h.gameObject);
        }

        [UnityTest]
        public IEnumerator TakeDamage_KillsAt0()
        {
            var h = CreateHealth(50);
            yield return null;

            bool deathFired = false;
            h.OnDeath += () => deathFired = true;

            h.TakeDamage(50);
            Assert.AreEqual(0, h.CurrentHp);
            Assert.IsTrue(h.IsDead);
            Assert.IsTrue(deathFired);

            Object.Destroy(h.gameObject);
        }

        [UnityTest]
        public IEnumerator TakeDamage_OverkillClampsToZero()
        {
            var h = CreateHealth(30);
            yield return null;

            h.TakeDamage(100);
            Assert.AreEqual(0, h.CurrentHp);
            Assert.IsTrue(h.IsDead);

            Object.Destroy(h.gameObject);
        }

        [UnityTest]
        public IEnumerator OnHpChanged_FiresWithCorrectValues()
        {
            var h = CreateHealth(100);
            yield return null;

            int receivedCurrent = -1, receivedMax = -1;
            h.OnHpChanged += (cur, max) => { receivedCurrent = cur; receivedMax = max; };

            h.TakeDamage(25);
            Assert.AreEqual(75, receivedCurrent);
            Assert.AreEqual(100, receivedMax);

            Object.Destroy(h.gameObject);
        }

        [UnityTest]
        public IEnumerator Heal_RestoresHp()
        {
            var h = CreateHealth(100);
            yield return null;

            h.TakeDamage(60);
            Assert.AreEqual(40, h.CurrentHp);

            h.Heal(30);
            Assert.AreEqual(70, h.CurrentHp);

            Object.Destroy(h.gameObject);
        }

        [UnityTest]
        public IEnumerator Heal_ClampsToMax()
        {
            var h = CreateHealth(100);
            yield return null;

            h.TakeDamage(10);
            h.Heal(999);
            Assert.AreEqual(100, h.CurrentHp);

            Object.Destroy(h.gameObject);
        }

        [UnityTest]
        public IEnumerator Invincible_PreventsDamage()
        {
            var h = CreateHealth(100);
            yield return null;

            h.SetInvincible(true);
            h.TakeDamage(999);
            Assert.AreEqual(100, h.CurrentHp);
            Assert.IsFalse(h.IsDead);

            h.SetInvincible(false);
            h.TakeDamage(10);
            Assert.AreEqual(90, h.CurrentHp);

            Object.Destroy(h.gameObject);
        }

        // ── Mana Tests ──

        [UnityTest]
        public IEnumerator TryConsume_Success_ReducesMana()
        {
            var m = CreateMana(100);
            yield return null;

            bool result = m.TryConsume(30);
            Assert.IsTrue(result);
            Assert.AreEqual(70, m.CurrentMana);

            Object.Destroy(m.gameObject);
        }

        [UnityTest]
        public IEnumerator TryConsume_InsufficientMana_ReturnsFalse()
        {
            var m = CreateMana(50);
            yield return null;

            m.TryConsume(40);
            Assert.AreEqual(10, m.CurrentMana);

            bool result = m.TryConsume(15);
            Assert.IsFalse(result);
            Assert.AreEqual(10, m.CurrentMana);

            Object.Destroy(m.gameObject);
        }

        [UnityTest]
        public IEnumerator OnManaChanged_FiresOnConsume()
        {
            var m = CreateMana(100);
            yield return null;

            int lastCurrent = -1, lastMax = -1;
            m.OnManaChanged += (cur, max) => { lastCurrent = cur; lastMax = max; };

            m.TryConsume(25);
            Assert.AreEqual(75, lastCurrent);
            Assert.AreEqual(100, lastMax);

            Object.Destroy(m.gameObject);
        }

        [UnityTest]
        public IEnumerator HasMana_ReportsCorrectly()
        {
            var m = CreateMana(50);
            yield return null;

            Assert.IsTrue(m.HasMana(50));
            Assert.IsTrue(m.HasMana(1));
            Assert.IsFalse(m.HasMana(51));

            m.TryConsume(30);
            Assert.IsTrue(m.HasMana(20));
            Assert.IsFalse(m.HasMana(21));

            Object.Destroy(m.gameObject);
        }

        [UnityTest]
        public IEnumerator Restore_AddsMana_ClampsToMax()
        {
            var m = CreateMana(100);
            yield return null;

            m.TryConsume(60);
            Assert.AreEqual(40, m.CurrentMana);

            m.Restore(30);
            Assert.AreEqual(70, m.CurrentMana);

            m.Restore(999);
            Assert.AreEqual(100, m.CurrentMana);

            Object.Destroy(m.gameObject);
        }

        [UnityTest]
        public IEnumerator ManaRegen_WorksOverTime()
        {
            var m = CreateMana(100, regen: 50f); // Fast regen for test
            yield return null;

            m.TryConsume(80);
            Assert.AreEqual(20, m.CurrentMana);

            // Wait long enough for regen delay + some regen time
            float waitTime = 2.5f;
            float elapsed = 0f;
            while (elapsed < waitTime)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            // Mana should have regenerated some amount
            Assert.Greater(m.CurrentMana, 20, "Mana should have regenerated");

            Object.Destroy(m.gameObject);
        }
    }
}
