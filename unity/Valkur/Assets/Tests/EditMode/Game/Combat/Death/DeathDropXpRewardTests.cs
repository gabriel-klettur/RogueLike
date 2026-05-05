using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// Pins the XP reward precedence on <see cref="DeathDropSystem.ComputeXpReward"/>:
    ///   1. Explicit <c>xpReward</c> wins.
    ///   2. Heuristic (<c>hp/5 + power</c>) is used when xpReward = 0.
    ///   3. Falls back to maxHp/5 when no definition is supplied.
    ///   4. Constant default of 5 when neither is available.
    /// </summary>
    [TestFixture]
    public class DeathDropXpRewardTests
    {
        private MonsterDefinition _def;

        [SetUp]
        public void SetUp()
        {
            _def = ScriptableObject.CreateInstance<MonsterDefinition>();
            _def.monsterKey = "test";
            _def.stats = new EntityStats { hp = 100, power = 4 };
        }

        [TearDown]
        public void TearDown()
        {
            if (_def != null) Object.DestroyImmediate(_def);
        }

        [Test]
        public void ExplicitXpReward_TakesPrecedence_OverHeuristic()
        {
            _def.xpReward = 50;
            // Heuristic would return 100/5 + 4 = 24. Explicit wins.
            Assert.AreEqual(50, DeathDropSystem.ComputeXpReward(_def, maxHpFallback: 0));
        }

        [Test]
        public void ZeroXpReward_FallsBackToHeuristic()
        {
            _def.xpReward = 0;
            // hp=100, power=4 → 100/5 + 4 = 24.
            Assert.AreEqual(24, DeathDropSystem.ComputeXpReward(_def, maxHpFallback: 0));
        }

        [Test]
        public void NegativeXpReward_FallsBackToHeuristic()
        {
            // Treat any non-positive value as "not set" so a stray -1 in
            // a designer asset doesn't grant negative XP at runtime.
            _def.xpReward = -10;
            Assert.AreEqual(24, DeathDropSystem.ComputeXpReward(_def, maxHpFallback: 0));
        }

        [Test]
        public void HeuristicNeverReturnsZero_WhenStatsAreZero()
        {
            _def.stats = new EntityStats { hp = 0, power = 0 };
            _def.xpReward = 0;
            Assert.AreEqual(1, DeathDropSystem.ComputeXpReward(_def, maxHpFallback: 0),
                "Mathf.Max(1, ...) guards against 0-XP loot from glass-cannon NPCs.");
        }

        [Test]
        public void NoDefinition_UsesMaxHpFallback()
        {
            Assert.AreEqual(20, DeathDropSystem.ComputeXpReward(def: null, maxHpFallback: 100));
        }

        [Test]
        public void NoDefinition_NoFallback_ReturnsConstantDefault()
        {
            Assert.AreEqual(5, DeathDropSystem.ComputeXpReward(def: null, maxHpFallback: 0));
        }

        [Test]
        public void DesignerCanSetSmallReward_AndItIsRespected()
        {
            // Common case: a low-tier monster designed to drop only 1 XP.
            _def.xpReward = 1;
            Assert.AreEqual(1, DeathDropSystem.ComputeXpReward(_def, maxHpFallback: 0));
        }
    }
}
