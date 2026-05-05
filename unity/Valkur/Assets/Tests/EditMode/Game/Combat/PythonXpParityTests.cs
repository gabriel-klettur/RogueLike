using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Numerical parity with the Python ExperienceComponent + ExperienceSystem.
    /// Python uses a flat <c>xp_to_next_level = 100</c> per level (resets to
    /// zero on level up). Unity uses a cumulative <c>XpRequiredForLevel(N) =
    /// baseXp * N^exponent</c>. With <c>baseXp=100, exponent=1</c> the two
    /// are mathematically equivalent at level boundaries — what matters is
    /// when level-ups fire, not how the field is named.
    /// </summary>
    [TestFixture]
    public class PythonXpParityTests
    {
        private GameObject _go;
        private Experience _xp;
        private XpCurveDefinition _curve;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            _go = new GameObject("Player");
            _xp = _go.AddComponent<Experience>();
            _curve = ScriptableObject.CreateInstance<XpCurveDefinition>();
            _curve.baseXp = 100;
            _curve.exponent = 1f;
            _xp.SetCurve(_curve);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_curve != null) Object.DestroyImmediate(_curve);
            GameEvents.Clear();
        }

        [Test]
        public void Threshold_MatchesPython_FlatHundredPerLevel()
        {
            // Python: each level requires 100 XP.
            // Unity cumulative: level N total = 100 * N.
            Assert.AreEqual(100, _xp.XpRequiredForLevel(1));
            Assert.AreEqual(200, _xp.XpRequiredForLevel(2));
            Assert.AreEqual(500, _xp.XpRequiredForLevel(5));
            Assert.AreEqual(1000, _xp.XpRequiredForLevel(10));
        }

        [Test]
        public void LevelUp_FiresAtSameTotals_AsPython()
        {
            // Drive the system step-by-step in 100-unit chunks; level should
            // increment exactly once per chunk, mirroring Python's
            // `while xp >= xp_to_next_level: xp -= ...; level += 1` loop.
            int expectedLevel = 0;
            for (int i = 0; i < 5; i++)
            {
                _xp.AddXp(100);
                expectedLevel++;
                Assert.AreEqual(expectedLevel, _xp.Level,
                    $"After {i+1} grants of 100 XP, expected L{expectedLevel}, got L{_xp.Level}.");
            }
        }

        [Test]
        public void OneShotGrant_LevelsUpMultipleTimes_LikePython()
        {
            // Python's loop is `while`, so a single big grant cascades. Unity
            // should match (the while-loop is in Experience.AddXp).
            _xp.AddXp(550);
            Assert.AreEqual(5, _xp.Level);
            // 550 - 500 (threshold for L5) = 50 left in current level.
            Assert.AreEqual(50, _xp.XpInCurrentLevel);
        }

        [Test]
        public void NormalizedProgress_MatchesPythonRatio()
        {
            // Python ratio: xp / xp_to_next_level (in current level).
            // Unity ratio: (totalXp - threshold(L)) / (threshold(L+1) - threshold(L)).
            _xp.AddXp(125); // L1 + 25 in-level.
            // Python: xp=25, xp_to_next_level=100 → ratio 0.25.
            // Unity: (125-100) / (200-100) = 25/100 = 0.25.
            Assert.That(_xp.NormalizedProgress, Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void RemoveXp_ClampedToCurrentLevel_MirrorsPythonInLevelDelta()
        {
            // Python penalty patterns subtract from the in-level xp pool,
            // not the cumulative total. Unity's clamped RemoveXp behaves
            // the same way: floor = threshold of current level.
            _xp.AddXp(140); // L1 + 40.
            int actual = _xp.RemoveXp(60, clampToCurrentLevel: true);
            Assert.AreEqual(40, actual);
            Assert.AreEqual(100, _xp.TotalXp);
            Assert.AreEqual(1, _xp.Level);
        }
    }
}
