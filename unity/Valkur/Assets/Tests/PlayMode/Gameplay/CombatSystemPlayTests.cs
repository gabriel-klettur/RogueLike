using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;

namespace Valkur.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// PlayMode slice of the combat system: only tests that genuinely require
    /// real frame-time advancement.
    ///
    /// The synchronous Health/Mana tests (damage, kill, overkill, invincible,
    /// heal, consume, restore, event firing) now live in:
    ///   - <c>EditMode/Game/Combat/HealthTests.cs</c>
    ///   - <c>EditMode/Game/Combat/ManaTests.cs</c>
    /// </summary>
    public class CombatSystemPlayTests
    {
        [UnityTest]
        public IEnumerator ManaRegen_WorksOverTime()
        {
            // Real Time.deltaTime advancement is the entire point — must stay in PlayMode.
            var go = new GameObject("Entity");
            var m = go.AddComponent<Mana>();
            m.Initialize(max: 100, regen: 50f); // Fast regen for test.
            yield return null;

            m.TryConsume(80);
            Assert.AreEqual(20, m.CurrentMana);

            float waitTime = 2.5f;
            float elapsed = 0f;
            while (elapsed < waitTime)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            Assert.Greater(m.CurrentMana, 20,
                "Mana must have regenerated after the delay window elapsed.");

            Object.Destroy(go);
        }
    }
}
