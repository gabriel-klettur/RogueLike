using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Pins <see cref="MonsterDefinition.level"/> / <see cref="MonsterDefinition.levelScaling"/> /
    /// <see cref="MonsterDefinition.GetScaledStats"/> — a monster can now be authored as "the
    /// same monster, but for a later zone" without duplicating the asset and retyping every
    /// stat by hand.
    ///
    /// The one non-negotiable constraint: every monster shipped BEFORE this feature existed
    /// must be numerically identical to today. That's exactly what "level 1" (the default) and
    /// "no curve assigned" (the default) each independently guarantee — see
    /// <see cref="Level1Monster_ScaledStats_AreByteIdenticalToBaseStats"/> and
    /// <see cref="NoLevelScalingAssigned_ScaledStats_AreByteIdenticalToBaseStats_RegardlessOfLevel"/>.
    /// </summary>
    [TestFixture]
    public class MonsterDefinitionLevelScalingTests
    {
        private MonsterDefinition _def;
        private LevelStatCurve _curve;

        [SetUp]
        public void SetUp()
        {
            _def = ScriptableObject.CreateInstance<MonsterDefinition>();
            _def.monsterKey = "test_monster";
            _def.stats = new EntityStats
            {
                hp = 100,
                meleeDamage = 10,
                defense = 5,
                speed = 3.5f,
                chasingSpeed = 5f,
                meleeRange = 1.2f,
                meleeCooldown = 0.8f,
                aggroRange = 6f,
                power = 8,
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_def != null) Object.DestroyImmediate(_def);
            if (_curve != null) Object.DestroyImmediate(_curve);
        }

        // ── Default state: byte-identical ────────────────────────────────────────

        [Test]
        public void Level1Monster_ScaledStats_AreByteIdenticalToBaseStats()
        {
            _def.level = 1;
            _curve = MakeLinearCurve(hpPerLevel: 20);
            _def.levelScaling = _curve;

            var scaled = _def.GetScaledStats();

            AssertStatsEqual(_def.stats, scaled,
                "A level-1 monster must be numerically identical to its un-levelled stats, " +
                "even with a curve assigned — every monster shipped before this field " +
                "existed defaults to level 1 and must not change.");
        }

        [Test]
        public void NewMonsterDefinition_DefaultsToLevel1_WithNoScalingCurve()
        {
            var fresh = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                Assert.AreEqual(1, fresh.level, "Default level must be 1 — the baseline.");
                Assert.IsNull(fresh.levelScaling, "Default scaling curve must be unassigned.");
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
        }

        [Test]
        public void NoLevelScalingAssigned_ScaledStats_AreByteIdenticalToBaseStats_RegardlessOfLevel()
        {
            _def.level = 50;
            _def.levelScaling = null;

            var scaled = _def.GetScaledStats();

            AssertStatsEqual(_def.stats, scaled,
                "With no curve assigned, GetScaledStats() must return 'stats' verbatim no " +
                "matter how high 'level' is set — a level field alone must never change a " +
                "shipped monster's numbers.");
        }

        // ── Scaling actually applied ─────────────────────────────────────────────

        [Test]
        public void LevelN_Linear_AddsCumulativeHpAcrossEveryLevelUpToN()
        {
            _curve = MakeLinearCurve(hpPerLevel: 20);
            _def.levelScaling = _curve;
            _def.level = 4; // levels 2,3,4 each contribute 20 => +60

            var scaled = _def.GetScaledStats();

            Assert.AreEqual(160, scaled.hp,
                "hp must grow by hpPerLevel for every level from 2 up to 'level' inclusive " +
                "(3 level-ups * 20 = 60 on top of the base 100).");
        }

        [Test]
        public void LevelN_ScalesMeleeDamageAndDefense_ByTheSameRatioHpGrewBy()
        {
            _curve = MakeLinearCurve(hpPerLevel: 100); // base 100 -> level 2 => 200 (2x ratio)
            _def.levelScaling = _curve;
            _def.level = 2;

            var scaled = _def.GetScaledStats();

            Assert.AreEqual(200, scaled.hp, "Precondition: hp must have doubled.");
            Assert.AreEqual(20, scaled.meleeDamage,
                "meleeDamage must scale by the same ratio hp grew by (10 * 2.0 = 20).");
            Assert.AreEqual(10, scaled.defense,
                "defense must scale by the same ratio hp grew by (5 * 2.0 = 10).");
        }

        [Test]
        public void LevelN_LeavesMovementAndTimingStatsUntouched()
        {
            _curve = MakeLinearCurve(hpPerLevel: 20);
            _def.levelScaling = _curve;
            _def.level = 5;

            var scaled = _def.GetScaledStats();

            Assert.AreEqual(_def.stats.speed, scaled.speed,
                "speed must never scale with level — a scaled monster should still move " +
                "like the monster it's a scaled copy of.");
            Assert.AreEqual(_def.stats.chasingSpeed, scaled.chasingSpeed);
            Assert.AreEqual(_def.stats.meleeRange, scaled.meleeRange);
            Assert.AreEqual(_def.stats.meleeCooldown, scaled.meleeCooldown);
            Assert.AreEqual(_def.stats.aggroRange, scaled.aggroRange);
            Assert.AreEqual(_def.stats.power, scaled.power,
                "power is not part of the scaling rule (only hp/meleeDamage/defense are).");
        }

        [Test]
        public void LevelN_WithCurveOverride_UsesCurveInsteadOfLinear()
        {
            _curve = ScriptableObject.CreateInstance<LevelStatCurve>();
            _curve.hpPerLevel = 999; // must be ignored once the curve has keys
            _curve.hpCurve = new AnimationCurve(
                new Keyframe(2, 15f),
                new Keyframe(3, 25f));
            _def.levelScaling = _curve;
            _def.level = 3;

            var scaled = _def.GetScaledStats();

            // HpDelta(2) + HpDelta(3) via the curve, not hpPerLevel.
            int expectedBonus = _curve.HpDelta(2) + _curve.HpDelta(3);
            Assert.AreEqual(_def.stats.hp + expectedBonus, scaled.hp,
                "When the curve has keys, GetScaledStats must evaluate it instead of the " +
                "linear hpPerLevel field — mirrors LevelStatCurve.HpDelta's own precedence.");
        }

        [Test]
        public void LevelN_WithZeroBaseHp_AddsHpBonus_WithoutDividingByZero()
        {
            _def.stats = new EntityStats { hp = 0, meleeDamage = 3, defense = 1 };
            _curve = MakeLinearCurve(hpPerLevel: 20);
            _def.levelScaling = _curve;
            _def.level = 3;

            EntityStats scaled = default;
            Assert.DoesNotThrow(() => scaled = _def.GetScaledStats());
            Assert.AreEqual(40, scaled.hp, "hp must still grow (2 level-ups * 20) from a 0 base.");
            Assert.AreEqual(3, scaled.meleeDamage,
                "With no ratio to derive from (base hp is 0), meleeDamage must stay at its " +
                "authored value rather than scale by an undefined ratio.");
            Assert.AreEqual(1, scaled.defense);
        }

        [Test]
        public void GetScaledStats_PreservesArrayReferences_ResistancesAndImmunities()
        {
            var resistances = new[] { new ElementResistance() };
            var immunities  = new[] { StatusEffectKind.Burn };
            _def.stats = new EntityStats { hp = 100, resistances = resistances, statusImmunities = immunities };
            _curve = MakeLinearCurve(hpPerLevel: 10);
            _def.levelScaling = _curve;
            _def.level = 2;

            var scaled = _def.GetScaledStats();

            Assert.AreSame(resistances, scaled.resistances,
                "Scaling must not clone or drop array-typed fields it doesn't touch.");
            Assert.AreSame(immunities, scaled.statusImmunities);
        }

        // ── Real shipped catalog: byte-identical at the default level ───────────

        [Test]
        public void ShippedCatalog_EveryDefinition_IsLevel1_AndScalesToItsOwnStatsVerbatim()
        {
            const string CatalogPath = "Assets/_Project/Data/Catalogs/Monsters/MonsterCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, $"MonsterCatalog asset must exist at {CatalogPath}.");

            foreach (var def in catalog.Definitions)
            {
                if (def == null) continue;
                Assert.AreEqual(1, def.level,
                    $"'{def.monsterKey}' must ship at level 1 — this feature is additive and " +
                    "opt-in, no shipped monster should default to anything else.");
                var scaled = def.GetScaledStats();
                AssertStatsEqual(def.stats, scaled,
                    $"'{def.monsterKey}': GetScaledStats() must equal 'stats' verbatim for every " +
                    "shipped monster today.");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static LevelStatCurve MakeLinearCurve(int hpPerLevel)
        {
            var curve = ScriptableObject.CreateInstance<LevelStatCurve>();
            curve.hpPerLevel = hpPerLevel;
            curve.hpCurve = new AnimationCurve(); // empty => linear mode
            return curve;
        }

        private static void AssertStatsEqual(EntityStats expected, EntityStats actual, string message)
        {
            Assert.AreEqual(expected.hp, actual.hp, message + " (hp)");
            Assert.AreEqual(expected.meleeDamage, actual.meleeDamage, message + " (meleeDamage)");
            Assert.AreEqual(expected.defense, actual.defense, message + " (defense)");
            Assert.AreEqual(expected.speed, actual.speed, message + " (speed)");
            Assert.AreEqual(expected.chasingSpeed, actual.chasingSpeed, message + " (chasingSpeed)");
            Assert.AreEqual(expected.meleeRange, actual.meleeRange, message + " (meleeRange)");
            Assert.AreEqual(expected.meleeCooldown, actual.meleeCooldown, message + " (meleeCooldown)");
            Assert.AreEqual(expected.aggroRange, actual.aggroRange, message + " (aggroRange)");
            Assert.AreEqual(expected.power, actual.power, message + " (power)");
        }
    }
}
