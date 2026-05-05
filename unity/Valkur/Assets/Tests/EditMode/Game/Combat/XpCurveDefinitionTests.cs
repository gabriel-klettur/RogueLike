using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins <see cref="XpCurveDefinition"/>: formula matches legacy
    /// inline values, the explicit-threshold table wins inside its range
    /// and falls back to formula outside, level cap is honoured.
    /// </summary>
    [TestFixture]
    public class XpCurveDefinitionTests
    {
        private XpCurveDefinition _curve;

        [SetUp]
        public void SetUp()
        {
            _curve = ScriptableObject.CreateInstance<XpCurveDefinition>();
            _curve.baseXp = 100;
            _curve.exponent = 1.5f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_curve != null) Object.DestroyImmediate(_curve);
        }

        [Test]
        public void Formula_MatchesLegacyExperienceComponent()
        {
            // Legacy Experience.cs: Mathf.RoundToInt(baseXp * level^exponent)
            // L1: 100, L2: round(100 * 2^1.5) = round(282.84) = 283.
            Assert.AreEqual(0,   _curve.XpRequiredForLevel(0));
            Assert.AreEqual(100, _curve.XpRequiredForLevel(1));
            Assert.AreEqual(283, _curve.XpRequiredForLevel(2));
        }

        [Test]
        public void NegativeLevel_Returns0()
        {
            Assert.AreEqual(0, _curve.XpRequiredForLevel(-5));
        }

        [Test]
        public void ExplicitThresholdTable_WinsInsideRange()
        {
            _curve.explicitThresholds = new[] { 50, 150, 350 };
            Assert.AreEqual(50,  _curve.XpRequiredForLevel(1));
            Assert.AreEqual(150, _curve.XpRequiredForLevel(2));
            Assert.AreEqual(350, _curve.XpRequiredForLevel(3));
        }

        [Test]
        public void ExplicitThresholdTable_FallsBackToFormulaOutsideRange()
        {
            _curve.explicitThresholds = new[] { 50, 150 };
            // Level 3 is outside the table → formula: round(100 * 3^1.5) = round(519.6) = 520.
            Assert.AreEqual(520, _curve.XpRequiredForLevel(3));
        }

        [Test]
        public void LevelCap_Reported()
        {
            _curve.levelCap = 50;
            Assert.IsFalse(_curve.IsAtCap(49));
            Assert.IsTrue(_curve.IsAtCap(50));
            Assert.IsTrue(_curve.IsAtCap(51));
        }

        [Test]
        public void LevelCap_Zero_DisablesCap()
        {
            _curve.levelCap = 0;
            Assert.IsFalse(_curve.IsAtCap(int.MaxValue),
                "levelCap=0 must disable the cap entirely.");
        }
    }
}
