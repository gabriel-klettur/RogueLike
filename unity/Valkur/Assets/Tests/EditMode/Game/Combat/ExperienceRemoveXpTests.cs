using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins <see cref="Experience.RemoveXp"/>: clamping prevents de-level
    /// by default, opt-in de-level walks back through thresholds, fires
    /// OnXpLost with actual amount removed, zero/negative are no-ops.
    /// </summary>
    [TestFixture]
    public class ExperienceRemoveXpTests
    {
        private GameObject _go;
        private Experience _xp;
        private int _xpLostCalls;
        private int _xpLostAccumulated;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            _go = new GameObject("Player");
            _xp = _go.AddComponent<Experience>();
            _xpLostCalls = 0;
            _xpLostAccumulated = 0;
            GameEvents.OnXpLost += OnXpLost;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            GameEvents.Clear();
        }

        private void OnXpLost(GameObject e, int amount)
        {
            _xpLostCalls++;
            _xpLostAccumulated += amount;
        }

        [Test]
        public void ZeroAmount_IsNoOp()
        {
            _xp.AddXp(50);
            int before = _xp.TotalXp;
            int actual = _xp.RemoveXp(0);
            Assert.AreEqual(0, actual);
            Assert.AreEqual(before, _xp.TotalXp);
            Assert.AreEqual(0, _xpLostCalls);
        }

        [Test]
        public void RemoveXp_ClampedByDefault_PreservesCurrentLevel()
        {
            // Default formula: L1 needs 100, L2 needs 283. AddXp(150) → L1 with 50 in-level XP.
            _xp.AddXp(150);
            Assert.AreEqual(1, _xp.Level);
            int totalBefore = _xp.TotalXp;

            // Try to remove much more than in-level XP — clamp must hold us at L1 floor (100).
            int actual = _xp.RemoveXp(9_999, clampToCurrentLevel: true);

            Assert.AreEqual(1, _xp.Level, "Clamped removal must not de-level.");
            Assert.AreEqual(50, actual, "Actual loss = totalBefore - floor(L1) = 150-100 = 50.");
            Assert.AreEqual(100, _xp.TotalXp);
        }

        [Test]
        public void RemoveXp_CanDelevel_WhenClampDisabled()
        {
            _xp.AddXp(150); // L1 + 50.
            int actual = _xp.RemoveXp(120, clampToCurrentLevel: false);

            Assert.AreEqual(0, _xp.Level, "Without clamp the entity walks back to L0.");
            Assert.AreEqual(120, actual);
            Assert.AreEqual(30, _xp.TotalXp);
        }

        [Test]
        public void RemoveXp_FiresGlobalOnXpLost_OnceWithActualAmount()
        {
            _xp.AddXp(150);
            _xp.RemoveXp(40);
            Assert.AreEqual(1, _xpLostCalls);
            Assert.AreEqual(40, _xpLostAccumulated);
        }

        [Test]
        public void RemoveXp_NegativeAmount_IsNoOp()
        {
            _xp.AddXp(50);
            int before = _xp.TotalXp;
            Assert.AreEqual(0, _xp.RemoveXp(-100));
            Assert.AreEqual(before, _xp.TotalXp);
        }

        [Test]
        public void RemoveXp_AtFloor_IsNoOp()
        {
            _xp.AddXp(100); // exactly L1 with 0 in-level XP.
            int before = _xp.TotalXp;
            int actual = _xp.RemoveXp(50, clampToCurrentLevel: true);
            Assert.AreEqual(0, actual,
                "When already at the level floor, clamped RemoveXp must be a no-op.");
            Assert.AreEqual(before, _xp.TotalXp);
        }
    }
}
