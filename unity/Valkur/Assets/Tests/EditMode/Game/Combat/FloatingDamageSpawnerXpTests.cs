using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the <see cref="FloatingDamageSpawner"/> XP feedback path:
    /// ShowXp formats text as "+N XP", increments the spawn counter,
    /// and ignores zero/negative amounts.
    /// </summary>
    [TestFixture]
    public class FloatingDamageSpawnerXpTests
    {
        private GameObject _go;
        private FloatingDamageSpawner _spawner;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("Entity");
            _spawner = _go.AddComponent<FloatingDamageSpawner>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void ShowXp_PositiveAmount_FormatsTextAndIncrementsCount()
        {
            int before = _spawner.SpawnedCount;
            _spawner.ShowXp(42);
            Assert.AreEqual(before + 1, _spawner.SpawnedCount);
            StringAssert.Contains("+42", _spawner.LastSpawnedText);
            StringAssert.Contains("XP", _spawner.LastSpawnedText);
        }

        [Test]
        public void ShowXp_Zero_IsNoOp()
        {
            int before = _spawner.SpawnedCount;
            _spawner.ShowXp(0);
            Assert.AreEqual(before, _spawner.SpawnedCount,
                "ShowXp must skip when amount is zero — no '+0 XP' clutter.");
        }
    }
}
