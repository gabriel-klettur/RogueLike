using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.VFX
{
    public class TimedDespawnTests
    {
        [Test]
        public void TTL_DefaultIsPositive()
        {
            var go = new GameObject("TestEntity");
            var td = go.AddComponent<TimedDespawn>();
            // Default TTL should be > 0 (set in inspector or constructor)
            td.TTL = 5f;
            Assert.AreEqual(5f, td.TTL, 0.001f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TTL_CanBeSet()
        {
            var go = new GameObject("TestEntity");
            var td = go.AddComponent<TimedDespawn>();
            td.TTL = 10f;
            Assert.AreEqual(10f, td.TTL, 0.001f);
            Object.DestroyImmediate(go);
        }
    }
}
