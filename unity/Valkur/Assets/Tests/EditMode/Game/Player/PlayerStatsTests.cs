using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Pins the arithmetic and the ownership rules of <see cref="PlayerStats"/>.
    ///
    /// The tests that matter most here are the COMPOSITION ones — a layer removed leaves
    /// exactly the other layers, a recompute changes nothing, the resolved number is what
    /// the combat component actually holds. Every failure this project has recorded in the
    /// stat/progression space was a composition failure that each half passed on its own:
    /// the spawner writing world coordinates into a zone-relative field, the boomerang
    /// inheriting a stranger's <c>Projectile.range</c>, the ice wall authored in pixels.
    /// </summary>
    [TestFixture]
    public class PlayerStatsTests
    {
        private GameObject _go;
        private PlayerStats _stats;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Player");
            _stats = _go.AddComponent<PlayerStats>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static List<StatModifier> Mods(params StatModifier[] m) => new List<StatModifier>(m);

        // ── Arithmetic ──────────────────────────────────────────────────────────

        [Test]
        public void Composition_FollowsThePublishedOrder()
        {
            // (base + flat) * (1 + Σ percentAdd) * Π (1 + percentMult)
            //   = (100 + 50) * (1 + 0.10 + 0.10) * 1.20 = 216
            _stats.SetBase(StatKind.MaxHp, 100f);
            _stats.SetLayer(StatLayer.Equipment, Mods(
                StatModifier.Flat(StatKind.MaxHp, 50f),
                StatModifier.Percent(StatKind.MaxHp, 0.10f)));
            _stats.SetLayer(StatLayer.Skill, Mods(
                StatModifier.Percent(StatKind.MaxHp, 0.10f),
                StatModifier.Multiplicative(StatKind.MaxHp, 0.20f)));

            Assert.AreEqual(216f, _stats.Get(StatKind.MaxHp), 0.001f);
        }

        [Test]
        public void PercentAdd_Pools_WhilePercentMult_StaysIndependent()
        {
            // The single reason both operations exist. Four sources of +25 %:
            //   additive       → 1 + 1.00 = 2.00
            //   multiplicative → 1.25^4   = 2.4414
            // Folding them into one bucket is the classic late-game stacking bug.
            _stats.SetBase(StatKind.MeleeDamage, 100f);
            _stats.SetLayer(StatLayer.Skill, Mods(
                StatModifier.Percent(StatKind.MeleeDamage, 0.25f),
                StatModifier.Percent(StatKind.MeleeDamage, 0.25f),
                StatModifier.Percent(StatKind.MeleeDamage, 0.25f),
                StatModifier.Percent(StatKind.MeleeDamage, 0.25f)));
            Assert.AreEqual(200f, _stats.Get(StatKind.MeleeDamage), 0.001f);

            _stats.ClearLayer(StatLayer.Skill);
            _stats.SetLayer(StatLayer.Skill, Mods(
                StatModifier.Multiplicative(StatKind.MeleeDamage, 0.25f),
                StatModifier.Multiplicative(StatKind.MeleeDamage, 0.25f),
                StatModifier.Multiplicative(StatKind.MeleeDamage, 0.25f),
                StatModifier.Multiplicative(StatKind.MeleeDamage, 0.25f)));
            Assert.AreEqual(244.14f, _stats.Get(StatKind.MeleeDamage), 0.01f);
        }

        [Test]
        public void NeutralBase_IsOneForMultiplierStats()
        {
            // Getting this backwards makes a fresh character deal ZERO spell damage, which
            // is a whole-game failure produced by a single wrong default.
            _stats.ApplyClassBase(null);
            Assert.AreEqual(1f, _stats.Get(StatKind.SpellPower), 0.001f);
            Assert.AreEqual(1f, _stats.Get(StatKind.XpGain), 0.001f);
            Assert.AreEqual(0f, _stats.Get(StatKind.CritChance), 0.001f);
        }

        [Test]
        public void Clamp_HoldsCooldownReductionBelowTotal()
        {
            // At 100 % every spell is instant and free, which deletes the resource game the
            // whole spell layer rests on.
            _stats.SetLayer(StatLayer.Skill, Mods(
                StatModifier.Flat(StatKind.SpellCooldownReduction, 5f)));

            Assert.AreEqual(StatCatalog.Max(StatKind.SpellCooldownReduction),
                            _stats.Get(StatKind.SpellCooldownReduction), 0.001f);
            Assert.Less(_stats.Get(StatKind.SpellCooldownReduction), 1f);
        }

        [Test]
        public void Clamp_KeepsCritChanceAProbability()
        {
            _stats.SetLayer(StatLayer.Equipment, Mods(
                StatModifier.Flat(StatKind.CritChance, 9f)));
            Assert.AreEqual(1f, _stats.Get(StatKind.CritChance), 0.001f);
        }

        // ── Ownership ───────────────────────────────────────────────────────────

        [Test]
        public void RemovingOneLayer_LeavesEveryOtherLayerExactlyAsItWas()
        {
            // The whole reason the store is layered. Unequipping a sword must remove the
            // sword's +6 and nothing else, even though a potion and a talent also touched
            // the same stat while it was worn.
            _stats.SetBase(StatKind.MeleeDamage, 10f);
            _stats.SetLayer(StatLayer.Skill, Mods(StatModifier.Flat(StatKind.MeleeDamage, 4f)));
            _stats.SetLayer(StatLayer.Buff, Mods(StatModifier.Flat(StatKind.MeleeDamage, 3f)));

            float withoutSword = _stats.Get(StatKind.MeleeDamage);

            _stats.SetLayer(StatLayer.Equipment, Mods(StatModifier.Flat(StatKind.MeleeDamage, 6f)));
            Assert.AreEqual(withoutSword + 6f, _stats.Get(StatKind.MeleeDamage), 0.001f);

            _stats.ClearLayer(StatLayer.Equipment);
            Assert.AreEqual(withoutSword, _stats.Get(StatKind.MeleeDamage), 0.001f,
                "Removing a layer must restore the exact prior value, with no drift.");
        }

        [Test]
        public void SetLayer_Replaces_RatherThanAccumulating()
        {
            _stats.SetBase(StatKind.MaxMana, 50f);
            _stats.SetLayer(StatLayer.Equipment, Mods(StatModifier.Flat(StatKind.MaxMana, 10f)));
            _stats.SetLayer(StatLayer.Equipment, Mods(StatModifier.Flat(StatKind.MaxMana, 10f)));

            Assert.AreEqual(60f, _stats.Get(StatKind.MaxMana), 0.001f,
                "A source rebuilding its own layer must not stack with itself.");
        }

        [Test]
        public void GetLayerContribution_ReportsTheDifferenceTheLayerMakes()
        {
            // Not the sum of its raw values: a percentage's contribution depends on every
            // other layer present, and reporting "0.05" for "+5%" is not a number a player
            // can add up to the total they are looking at.
            _stats.SetBase(StatKind.MaxHp, 100f);
            _stats.SetLayer(StatLayer.Equipment, Mods(StatModifier.Flat(StatKind.MaxHp, 100f)));
            _stats.SetLayer(StatLayer.Skill, Mods(StatModifier.Percent(StatKind.MaxHp, 0.50f)));

            Assert.AreEqual(300f, _stats.Get(StatKind.MaxHp), 0.001f);
            Assert.AreEqual(100f, _stats.GetLayerContribution(StatKind.MaxHp, StatLayer.Skill), 0.001f,
                "The +50 % talent is worth 100 HP here, and that is what the sheet must say.");
        }

        // ── Composition with the live components ────────────────────────────────

        [Test]
        public void ResolvedValues_ReachTheComponentsThatUseThem()
        {
            var health = _go.AddComponent<Health>();
            var mana = _go.AddComponent<Mana>();
            var melee = _go.AddComponent<MeleeCombat>();
            health.Initialize(100);
            mana.Initialize(50);
            melee.Initialize(5, 1f, 1f);

            _stats.SetBase(StatKind.MaxHp, 100f);
            _stats.SetBase(StatKind.MaxMana, 50f);
            _stats.SetBase(StatKind.MeleeDamage, 5f);
            _stats.SetBase(StatKind.Defense, 0f);
            _stats.ForcePush();

            _stats.SetLayer(StatLayer.Equipment, Mods(
                StatModifier.Flat(StatKind.MaxHp, 40f),
                StatModifier.Flat(StatKind.MeleeDamage, 7f),
                StatModifier.Flat(StatKind.Defense, 3f)));

            Assert.AreEqual(140, health.MaxHp, "A stat nobody can observe in combat is not a stat.");
            Assert.AreEqual(12, melee.Damage);
            Assert.AreEqual(3, health.Defense,
                "Defense reaches Health through the same seam monsters have always used.");
        }

        [Test]
        public void Recompute_IsIdempotent_AndDoesNotHealOnUnrelatedChanges()
        {
            // The contract the whole design rests on. The old skill layer pushed through
            // Health.IncreaseMaxHp, a DELTA API — recomputing twice granted the bonus twice,
            // so every expiring potion healed the player a little.
            var health = _go.AddComponent<Health>();
            health.Initialize(100);

            _stats.SetBase(StatKind.MaxHp, 100f);
            _stats.ForcePush();
            health.TakeDamage(30);
            Assert.AreEqual(70, health.CurrentHp);

            for (int i = 0; i < 5; i++)
                _stats.SetLayer(StatLayer.Buff, Mods(StatModifier.Flat(StatKind.MoveSpeed, 1f)));

            Assert.AreEqual(70, health.CurrentHp,
                "Recomputing for an unrelated stat must not touch current HP.");
            Assert.AreEqual(100, health.MaxHp);
        }

        [Test]
        public void RaisingMaxHp_GrantsTheDifference_AndLoweringItClipsOnly()
        {
            var health = _go.AddComponent<Health>();
            health.Initialize(100);
            _stats.SetBase(StatKind.MaxHp, 100f);
            _stats.ForcePush();

            health.TakeDamage(50);
            Assert.AreEqual(50, health.CurrentHp);

            _stats.SetLayer(StatLayer.Level, Mods(StatModifier.Flat(StatKind.MaxHp, 20f)));
            Assert.AreEqual(120, health.MaxHp);
            Assert.AreEqual(70, health.CurrentHp,
                "A bigger pool arrives with the new room already filled.");

            _stats.ClearLayer(StatLayer.Level);
            Assert.AreEqual(100, health.MaxHp);
            Assert.AreEqual(70, health.CurrentHp,
                "Shrinking the pool clips only what no longer fits.");
        }

        [Test]
        public void PushIsRefused_UntilABaseHasBeenAuthored()
        {
            // The state worth refusing is bases NOBODY has written. Until then every base is
            // its neutral value, and pushing those seats a character no designer authored.
            //
            // This replaces a test that pinned the previous guard, `MaxHp > 0` — a VALUE
            // standing in for "has this component been configured". That proxy could not
            // tell an uninitialised pool from a legitimately empty one, and its own comment
            // conceded the first half was unreachable anyway (Health's serialized field
            // defaults to 100, so a freshly added component already looks configured). The
            // half that was reachable is the one that shipped the bug: see
            // ManaPoolIsRepaired_AfterANeutralPushEmptiedIt below.
            var health = _go.AddComponent<Health>();
            var mana = _go.AddComponent<Mana>();
            health.Initialize(100);
            mana.Initialize(60);

            _stats.SetLayer(StatLayer.Equipment, Mods(StatModifier.Flat(StatKind.MaxHp, 40f)));
            _stats.ForcePush();

            Assert.AreEqual(100, health.MaxHp,
                "A layer alone must not push: with no base authored the resolved MaxHp is " +
                "the neutral 0 plus the layer, which is not a character anyone asked for.");
            Assert.AreEqual(60, mana.MaxMana,
                "And the mana pool must still be the one Initialize seated.");
        }

        [Test]
        public void ManaPoolIsRepaired_AfterANeutralPushEmptiedIt()
        {
            // The regression. Health.SetMaxHp floors its argument at 1, so a push carrying
            // unseeded bases cannot empty the HP pool; Mana.SetMaxMana accepts 0, which is
            // correct on its own — a class with no mana is a legitimate thing to author.
            // Paired with a `MaxMana > 0` push guard those two made an unrecoverable state:
            // the pool went to 0 and every later recompute was then skipped by the very
            // guard that had just been satisfied. Measured in the shipped game, every player
            // class spawned with Mana 0/0 and could not cast at all.
            var mana = _go.AddComponent<Mana>();
            mana.Initialize(0, 0f);
            Assert.AreEqual(0, mana.MaxMana, "Sanity: the pool starts empty.");

            _stats.SetBase(StatKind.MaxMana, 95f);
            _stats.SetBase(StatKind.ManaRegen, 1f);
            _stats.ForcePush();

            Assert.AreEqual(95, mana.MaxMana,
                "An empty pool must be repairable — that is the whole difference between " +
                "a state flag and a value test.");
            Assert.AreEqual(1f, mana.RegenPerSecond, 0.001f);
        }
    }
}
