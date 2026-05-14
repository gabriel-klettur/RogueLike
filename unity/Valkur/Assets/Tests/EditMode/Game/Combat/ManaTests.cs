using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// EditMode tests for <see cref="Mana"/>: consume / insufficient-fail / event
    /// firing / has-mana query / restore-with-clamp. The time-based regen test
    /// lives in <c>PlayMode/Gameplay/CombatSystemPlayTests.cs</c> because it
    /// requires real <see cref="Time.deltaTime"/> advancement.
    ///
    /// Migrated from <c>PlayMode/Gameplay/CombatSystemPlayTests.cs</c>.
    /// </summary>
    [TestFixture]
    public class ManaTests
    {
        private Mana CreateMana(int maxMana = 100, float regen = 0f)
        {
            var go = new GameObject("Entity");
            var m = go.AddComponent<Mana>();
            m.Initialize(maxMana, regen);
            return m;
        }

        private static void Destroy(Mana m)
        {
            if (m != null) Object.DestroyImmediate(m.gameObject);
        }

        [Test]
        public void TryConsume_Success_ReducesMana()
        {
            var m = CreateMana(100);
            try
            {
                bool result = m.TryConsume(30);
                Assert.IsTrue(result);
                Assert.AreEqual(70, m.CurrentMana);
            }
            finally { Destroy(m); }
        }

        [Test]
        public void TryConsume_InsufficientMana_ReturnsFalse()
        {
            var m = CreateMana(50);
            try
            {
                m.TryConsume(40);
                Assert.AreEqual(10, m.CurrentMana);

                bool result = m.TryConsume(15);
                Assert.IsFalse(result);
                Assert.AreEqual(10, m.CurrentMana,
                    "A failed TryConsume must not deduct partial mana.");
            }
            finally { Destroy(m); }
        }

        [Test]
        public void OnManaChanged_FiresOnConsume()
        {
            var m = CreateMana(100);
            try
            {
                int lastCurrent = -1, lastMax = -1;
                m.OnManaChanged += (cur, max) =>
                {
                    lastCurrent = cur;
                    lastMax = max;
                };

                m.TryConsume(25);
                Assert.AreEqual(75, lastCurrent);
                Assert.AreEqual(100, lastMax);
            }
            finally { Destroy(m); }
        }

        [Test]
        public void HasMana_ReportsCorrectly()
        {
            var m = CreateMana(50);
            try
            {
                Assert.IsTrue(m.HasMana(50));
                Assert.IsTrue(m.HasMana(1));
                Assert.IsFalse(m.HasMana(51));

                m.TryConsume(30);
                Assert.IsTrue(m.HasMana(20));
                Assert.IsFalse(m.HasMana(21));
            }
            finally { Destroy(m); }
        }

        [Test]
        public void Restore_AddsMana_ClampsToMax()
        {
            var m = CreateMana(100);
            try
            {
                m.TryConsume(60);
                Assert.AreEqual(40, m.CurrentMana);

                m.Restore(30);
                Assert.AreEqual(70, m.CurrentMana);

                m.Restore(999);
                Assert.AreEqual(100, m.CurrentMana);
            }
            finally { Destroy(m); }
        }
    }
}
