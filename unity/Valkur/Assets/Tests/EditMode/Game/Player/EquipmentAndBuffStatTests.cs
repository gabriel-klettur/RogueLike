using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Pins the two stat sources that turn already-authored content into gameplay:
    /// equipment (180 shipped items whose combat fields no combat system read) and timed
    /// buffs (a consumable path that was a <c>Debug.Log</c> and a <c>WaitForSeconds</c>).
    /// </summary>
    [TestFixture]
    public class EquipmentAndBuffStatTests
    {
        private static ItemDefinition MakeItem(string id, EquipSlot slot)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = id;
            item.equipSlot = slot;
            item.critMultiplier = 1f;   // the schema's resting value
            return item;
        }

        // ── Equipment mapping ───────────────────────────────────────────────────

        [Test]
        public void AttackSpeed_ConvertsAsAReciprocal_NotANegation()
        {
            // The shipped field is a RATE multiplier (authored 0.8 to 1.5, 1 = normal) while
            // the stat store speaks in cooldown SECONDS. 1.5 attacks per second is a cooldown
            // of 1/1.5, i.e. -33 %. Writing -0.5 there would halve the interval instead.
            var sword = MakeItem("sword", EquipSlot.Weapon);
            sword.attackSpeed = 1.5f;
            try
            {
                var mods = new List<StatModifier>();
                EquipmentStatSource.AppendItem(sword, mods);

                var cooldown = mods.Find(m => m.stat == StatKind.MeleeCooldown);
                Assert.AreEqual(StatOp.PercentAdd, cooldown.op);
                Assert.AreEqual(-1f / 3f, cooldown.value, 0.001f);
            }
            finally { Object.DestroyImmediate(sword); }
        }

        [Test]
        public void CritMultiplier_ContributesOnlyWhatItExceedsOneBy()
        {
            // The field rests at 1 in the schema. Adding it raw would hand every weapon in
            // the catalogue a free +150 % crit damage for carrying its own default.
            var plain = MakeItem("plain", EquipSlot.Weapon);
            var fancy = MakeItem("fancy", EquipSlot.Weapon);
            fancy.critMultiplier = 2.5f;
            try
            {
                var plainMods = new List<StatModifier>();
                EquipmentStatSource.AppendItem(plain, plainMods);
                Assert.AreEqual(0, plainMods.FindAll(m => m.stat == StatKind.CritMultiplier).Count,
                    "A weapon at the resting value must contribute no crit damage at all.");

                var fancyMods = new List<StatModifier>();
                EquipmentStatSource.AppendItem(fancy, fancyMods);
                var crit = fancyMods.Find(m => m.stat == StatKind.CritMultiplier);
                Assert.AreEqual(1.5f, crit.value, 0.001f);
            }
            finally { Object.DestroyImmediate(plain); Object.DestroyImmediate(fancy); }
        }

        [Test]
        public void AnUnequippableItem_ContributesNothing()
        {
            // A potion in the bag with a damage value is not a weapon in the hand.
            var potion = MakeItem("potion", EquipSlot.None);
            potion.damage = 99;
            try
            {
                var mods = new List<StatModifier>();
                EquipmentStatSource.AppendItem(potion, mods);
                Assert.AreEqual(0, mods.Count);
            }
            finally { Object.DestroyImmediate(potion); }
        }

        [Test]
        public void ItemRange_IsDeliberatelyNotMapped()
        {
            // Its shipped values are 1, 2, 5, 6 and 8 against a melee reach authored between
            // 0.6 and 3.0 world units, so it is plainly in another unit — almost certainly
            // the Python scale this project has already caught leaking five times. A guessed
            // conversion would be the sixth. This test exists so the omission is a decision
            // rather than an oversight somebody "fixes" later.
            var spear = MakeItem("spear", EquipSlot.Weapon);
            spear.range = 8;
            try
            {
                var mods = new List<StatModifier>();
                EquipmentStatSource.AppendItem(spear, mods);
                Assert.AreEqual(0, mods.FindAll(m => m.stat == StatKind.MeleeRange).Count,
                    "ItemDefinition.range must stay unmapped until its unit is established. " +
                    "Author reach through statModifiers.");
            }
            finally { Object.DestroyImmediate(spear); }
        }

        [Test]
        public void AuthoredStatModifiers_PassThroughUnchanged()
        {
            var helm = MakeItem("helm", EquipSlot.Helmet);
            helm.statModifiers = new[] { StatModifier.Flat(StatKind.MaxHp, 25f) };
            try
            {
                var mods = new List<StatModifier>();
                EquipmentStatSource.AppendItem(helm, mods);
                Assert.AreEqual(1, mods.Count);
                Assert.AreEqual(StatKind.MaxHp, mods[0].stat);
                Assert.AreEqual(25f, mods[0].value, 0.001f);
            }
            finally { Object.DestroyImmediate(helm); }
        }

        // ── Timed buffs ─────────────────────────────────────────────────────────

        [Test]
        public void SameKeyRefreshes_DifferentKeysStack()
        {
            // The rule StatusEffectManager already follows for burns, and the one CLAUDE.md
            // records for the cone breath: re-applying is churn, not stacking.
            var go = new GameObject("Player");
            try
            {
                var stats = go.AddComponent<PlayerStats>();
                var buffs = go.AddComponent<TimedBuffSource>();
                stats.SetBase(StatKind.MeleeDamage, 10f);

                buffs.Apply("flask_of_might", StatKind.MeleeDamage, 5f, 30f);
                Assert.AreEqual(15f, stats.Get(StatKind.MeleeDamage), 0.001f);

                buffs.Apply("flask_of_might", StatKind.MeleeDamage, 5f, 30f);
                Assert.AreEqual(15f, stats.Get(StatKind.MeleeDamage), 0.001f,
                    "A second flask of the same kind refreshes the timer, it does not stack.");
                Assert.AreEqual(1, buffs.ActiveCount);

                buffs.Apply("shrine_blessing", StatKind.MeleeDamage, 4f, 30f);
                Assert.AreEqual(19f, stats.Get(StatKind.MeleeDamage), 0.001f,
                    "Two different sources stack normally.");
                Assert.AreEqual(2, buffs.ActiveCount);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RemovingOneBuff_LeavesTheOther()
        {
            var go = new GameObject("Player");
            try
            {
                var stats = go.AddComponent<PlayerStats>();
                var buffs = go.AddComponent<TimedBuffSource>();
                stats.SetBase(StatKind.MaxHp, 100f);

                buffs.Apply("a", StatKind.MaxHp, 10f, 30f);
                buffs.Apply("b", StatKind.MaxHp, 20f, 30f);
                Assert.AreEqual(130f, stats.Get(StatKind.MaxHp), 0.001f);

                buffs.Remove("a");
                Assert.AreEqual(120f, stats.Get(StatKind.MaxHp), 0.001f);
                Assert.IsTrue(buffs.IsActive("b"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AZeroDurationBuff_IsRefused()
        {
            // A permanent stat change belongs in a layer with an owner who can remove it,
            // and the buff layer's owner is a clock.
            var go = new GameObject("Player");
            try
            {
                var stats = go.AddComponent<PlayerStats>();
                var buffs = go.AddComponent<TimedBuffSource>();
                stats.SetBase(StatKind.MaxHp, 100f);

                buffs.Apply("forever", StatKind.MaxHp, 50f, 0f);

                Assert.AreEqual(0, buffs.ActiveCount);
                Assert.AreEqual(100f, stats.Get(StatKind.MaxHp), 0.001f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── Crits ───────────────────────────────────────────────────────────────

        [Test]
        public void AnAttackerWithNoStatStore_NeverCrits()
        {
            // Every monster in the game. Keeps the whole mechanic on the player's side of
            // combat, where its tuning lives.
            var go = new GameObject("Monster");
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    int dealt = Valkur.Gameplay.Combat.CritResolver.Resolve(10, go, out bool crit);
                    Assert.IsFalse(crit);
                    Assert.AreEqual(10, dealt);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GuaranteedCrit_AppliesTheMultiplier()
        {
            var go = new GameObject("Player");
            try
            {
                var stats = go.AddComponent<PlayerStats>();
                stats.SetBase(StatKind.CritChance, 1f);
                stats.SetBase(StatKind.CritMultiplier, 2f);

                int dealt = Valkur.Gameplay.Combat.CritResolver.Resolve(10, go, out bool crit);
                Assert.IsTrue(crit);
                Assert.AreEqual(20, dealt);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ACritAlwaysBeatsANormalHit_EvenAtAMultiplierOfOne()
        {
            // A "critical" that deals identical damage is a stat the player cannot see
            // working, which is how critChance sat unnoticed in the item schema for as long
            // as it did.
            var go = new GameObject("Player");
            try
            {
                var stats = go.AddComponent<PlayerStats>();
                stats.SetBase(StatKind.CritChance, 1f);
                stats.SetBase(StatKind.CritMultiplier, 1f);

                int dealt = Valkur.Gameplay.Combat.CritResolver.Resolve(10, go, out bool crit);
                Assert.IsTrue(crit);
                Assert.Greater(dealt, 10);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
