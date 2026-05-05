using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins <see cref="Experience"/> ↔ <see cref="XpCurveDefinition"/> integration:
    /// curve thresholds drive level-ups, level cap is honoured, and existing
    /// inline-formula behaviour is preserved when no curve is assigned.
    /// </summary>
    [TestFixture]
    public class ExperienceCurveIntegrationTests
    {
        private GameObject _go;
        private Experience _xp;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            _go = new GameObject("Player");
            _xp = _go.AddComponent<Experience>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            GameEvents.Clear();
        }

        [Test]
        public void NoCurveAssigned_PreservesLegacyFormula()
        {
            // Default fields baseXp=100, exponent=1.5 → L0→L1 needs 100.
            Assert.AreEqual(100, _xp.XpRequiredForLevel(1));
            _xp.AddXp(100);
            Assert.AreEqual(1, _xp.Level);
        }

        [Test]
        public void CurveAssigned_TakesPrecedence()
        {
            var curve = ScriptableObject.CreateInstance<XpCurveDefinition>();
            curve.baseXp = 50;
            curve.exponent = 1f;
            try
            {
                _xp.SetCurve(curve);
                Assert.AreEqual(50, _xp.XpRequiredForLevel(1));
                _xp.AddXp(50);
                Assert.AreEqual(1, _xp.Level);
            }
            finally { Object.DestroyImmediate(curve); }
        }

        [Test]
        public void LevelCap_StopsLevelUpAtCeiling()
        {
            var curve = ScriptableObject.CreateInstance<XpCurveDefinition>();
            curve.baseXp = 10;
            curve.exponent = 1f;
            curve.levelCap = 3;
            try
            {
                _xp.SetCurve(curve);
                _xp.AddXp(10_000); // way over what would normally take to L99
                Assert.AreEqual(3, _xp.Level,
                    "Level cap must be respected even with arbitrarily large XP grants.");
                Assert.IsTrue(_xp.IsAtLevelCap);
            }
            finally { Object.DestroyImmediate(curve); }
        }

        [Test]
        public void AddXp_AfterCap_IsNoOp()
        {
            var curve = ScriptableObject.CreateInstance<XpCurveDefinition>();
            curve.baseXp = 10;
            curve.exponent = 1f;
            curve.levelCap = 1;
            try
            {
                _xp.SetCurve(curve);
                _xp.AddXp(10);
                Assert.AreEqual(1, _xp.Level);
                int totalAtCap = _xp.TotalXp;

                _xp.AddXp(500);
                Assert.AreEqual(totalAtCap, _xp.TotalXp,
                    "Once at the cap, AddXp must not accumulate further total XP " +
                    "(no telemetry / UI noise).");
            }
            finally { Object.DestroyImmediate(curve); }
        }
    }
}
