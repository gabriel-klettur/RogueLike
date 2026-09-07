using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// The coin faucet — what a kill is worth, and the fact that there IS one.
    ///
    /// <para>Before this existed the game had no currency source at all: no monster in the
    /// shipped catalogue dropped coins, no quest granted them, and the wallet started empty,
    /// so the only way to earn was felling a tree for a coin a swing. The sinks were all
    /// built; nothing flowed into them. Nothing failed, because a shop with prices the player
    /// cannot reach looks exactly like a shop.</para>
    /// </summary>
    public class CoinFaucetTests
    {
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var a in _assets) if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();
        }

        private MonsterDefinition MakeMonster(int hp, int power, int coinReward = 0)
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = "test_monster";
            def.stats.hp = hp;
            def.stats.power = power;
            def.stats.faction = "EVIL";
            def.coinReward = coinReward;
            _assets.Add(def);
            return def;
        }

        /// <summary>
        /// An authored number is the designer's last word and beats the heuristic outright.
        /// </summary>
        [Test]
        public void AuthoredCoinReward_Wins()
        {
            Assert.AreEqual(77, DeathDropSystem.ComputeCoinReward(MakeMonster(100, 10, 77), 0));
        }

        /// <summary>
        /// -1 is the explicit "this one pays nothing". It needs its own value because 0 is
        /// already spoken for by the fallback, and the alternative — editing the monster's
        /// faction — changes four other behaviours at the same time.
        /// </summary>
        [Test]
        public void NegativeCoinReward_MeansNoPayout()
        {
            Assert.AreEqual(0, DeathDropSystem.ComputeCoinReward(MakeMonster(5000, 99, -1), 0));
        }

        /// <summary>
        /// Zero means "use the heuristic", which is what lets every monster shipped before the
        /// field existed start paying out without a data edit. Same contract xpReward uses.
        /// </summary>
        [Test]
        public void ZeroCoinReward_FallsBackToTheHeuristic()
        {
            // hp/40 + power/4 — a trash barbol.
            Assert.AreEqual(100 / 40 + 10 / 4, DeathDropSystem.ComputeCoinReward(MakeMonster(100, 10), 0));
        }

        /// <summary>
        /// A tougher monster is worth more. Pinned as a monotonic property rather than as
        /// literals so retuning the divisors stays possible without rewriting the fixture —
        /// what must never change is the direction.
        /// </summary>
        [Test]
        public void ToughMonsters_PayMoreThanTrash()
        {
            int trash = DeathDropSystem.ComputeCoinReward(MakeMonster(30, 10), 0);
            int mid = DeathDropSystem.ComputeCoinReward(MakeMonster(140, 14), 0);
            int boss = DeathDropSystem.ComputeCoinReward(MakeMonster(2000, 10), 0);

            Assert.That(mid, Is.GreaterThan(trash));
            Assert.That(boss, Is.GreaterThan(mid));
        }

        /// <summary>
        /// Nothing is ever worth zero by accident. A kill that drops nothing at all reads as
        /// the drop system having failed, which is a bug report about a working system.
        /// </summary>
        [Test]
        public void AnUnknownHostile_IsStillWorthStoopingFor()
        {
            Assert.That(DeathDropSystem.ComputeCoinReward(null, 0), Is.GreaterThan(0));
            Assert.That(DeathDropSystem.ComputeCoinReward(null, 250), Is.GreaterThan(0));
            Assert.That(DeathDropSystem.ComputeCoinReward(MakeMonster(1, 0), 0), Is.GreaterThan(0));
        }

        /// <summary>
        /// The scaled stats, not the authored ones — a levelled copy of a monster was a longer
        /// fight and has to pay like one. XP already reads them this way; a reward that did
        /// not would make every late-zone variant quietly poorer than the monster it copies.
        /// </summary>
        [Test]
        public void ALevelledMonster_PaysMoreThanItsBaseline()
        {
            var curve = ScriptableObject.CreateInstance<LevelStatCurve>();
            curve.hpPerLevel = 100;
            _assets.Add(curve);

            var baseline = MakeMonster(200, 12);
            var levelled = MakeMonster(200, 12);
            levelled.level = 5;
            levelled.levelScaling = curve;

            Assert.That(DeathDropSystem.ComputeCoinReward(levelled, 0),
                Is.GreaterThan(DeathDropSystem.ComputeCoinReward(baseline, 0)),
                "a levelled monster paid the same as its baseline; the heuristic is reading authored stats");
        }

        /// <summary>
        /// The whole shipped roster, checked against the shipped prices. This is the fixture
        /// that would have caught the original defect: every unit test above passes on a game
        /// where not one monster asset produces a coin.
        /// </summary>
        [Test]
        public void EveryShippedHostile_PaysSomething()
        {
            var monsters = AssetDatabase.FindAssets("t:MonsterDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                .Where(m => m != null)
                .Where(m => string.IsNullOrEmpty(m.stats.faction) ||
                            m.stats.faction.Equals("EVIL", System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.That(monsters.Count, Is.GreaterThan(5), "hostile roster did not load; this test would be vacuous");

            var free = monsters
                .Where(m => m.coinReward >= 0)
                .Where(m => DeathDropSystem.ComputeCoinReward(m, 0) <= 0)
                .Select(m => m.monsterKey)
                .ToList();

            Assert.IsEmpty(free, "hostiles that drop no coins: " + string.Join(", ", free));
        }
    }
}
