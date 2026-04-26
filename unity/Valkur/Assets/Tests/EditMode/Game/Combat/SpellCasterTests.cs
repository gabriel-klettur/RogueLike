using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    public class SpellCasterTests
    {
        private SpellDefinition CreateSpell(string key, float prepare = 0f, float channel = 0f, float cooldown = 1f, float manaCost = 0f)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = key;
            spell.displayName = key;
            spell.type = SpellType.Projectile;
            spell.manaCost = manaCost;
            spell.damage = 10f;
            spell.speed = 5f;
            spell.prepareDuration = prepare;
            spell.channelDuration = channel;
            spell.cooldownDuration = cooldown;
            spell.range = 10f;
            spell.lifetime = 3f;
            return spell;
        }

        private SpellCaster CreateCaster()
        {
            var go = new GameObject("Caster");
            var caster = go.AddComponent<SpellCaster>();
            // Awake doesn't run in EditMode — initialize _cooldownTimers via reflection
            var field = typeof(SpellCaster).GetField("_cooldownTimers", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(caster, new float[caster.SlotCount]);
            return caster;
        }

        private SpellCaster CreateCasterWithMana(int currentMana, int maxMana = 100)
        {
            var caster = CreateCaster();
            var mana = caster.gameObject.AddComponent<Mana>();
            mana.Initialize(maxMana, regen: 0f);

            int consumeToTarget = Mathf.Max(0, maxMana - currentMana);
            if (consumeToTarget > 0)
                mana.TryConsume(consumeToTarget);

            return caster;
        }

        private void Cleanup(SpellCaster caster)
        {
            Object.DestroyImmediate(caster.gameObject);
        }

        // --- Slot Management ---

        [Test]
        public void SlotCount_DefaultIs4()
        {
            var caster = CreateCaster();
            Assert.AreEqual(4, caster.SlotCount);
            Cleanup(caster);
        }

        [Test]
        public void GetSlotName_EmptySlot_ReturnsDash()
        {
            var caster = CreateCaster();
            Assert.AreEqual("-", caster.GetSlotName(0));
            Assert.AreEqual("-", caster.GetSlotName(3));
            Cleanup(caster);
        }

        [Test]
        public void GetSlotName_OutOfRange_ReturnsDash()
        {
            var caster = CreateCaster();
            Assert.AreEqual("-", caster.GetSlotName(-1));
            Assert.AreEqual("-", caster.GetSlotName(99));
            Cleanup(caster);
        }

        [Test]
        public void SetSpell_And_GetSlotName_ReturnsDisplayName()
        {
            var caster = CreateCaster();
            var spell = CreateSpell("fireball");
            caster.SetSpell(0, spell);
            Assert.AreEqual("fireball", caster.GetSlotName(0));
            Cleanup(caster);
        }

        // --- Phase State ---

        [Test]
        public void CurrentPhase_InitiallyReady()
        {
            var caster = CreateCaster();
            Assert.AreEqual(SpellCaster.CastPhase.Ready, caster.CurrentPhase);
            Cleanup(caster);
        }

        [Test]
        public void ActiveSlot_InitiallyNegativeOne()
        {
            var caster = CreateCaster();
            Assert.AreEqual(-1, caster.ActiveSlot);
            Cleanup(caster);
        }

        // --- CanCast ---

        [Test]
        public void CanCast_EmptySlot_ReturnsFalse()
        {
            var caster = CreateCaster();
            Assert.IsFalse(caster.CanCast(0));
            Cleanup(caster);
        }

        [Test]
        public void CanCast_WithSpell_ReturnsTrue()
        {
            var caster = CreateCaster();
            caster.SetSpell(0, CreateSpell("fireball"));
            Assert.IsTrue(caster.CanCast(0));
            Cleanup(caster);
        }

        [Test]
        public void CanCast_OutOfRange_ReturnsFalse()
        {
            var caster = CreateCaster();
            Assert.IsFalse(caster.CanCast(-1));
            Assert.IsFalse(caster.CanCast(99));
            Cleanup(caster);
        }

        // --- Cooldown ---

        [Test]
        public void GetCooldownRemaining_InitiallyZero()
        {
            var caster = CreateCaster();
            Assert.AreEqual(0f, caster.GetCooldownRemaining(0), 0.001f);
            Cleanup(caster);
        }

        [Test]
        public void GetCooldownRemaining_OutOfRange_ReturnsZero()
        {
            var caster = CreateCaster();
            Assert.AreEqual(0f, caster.GetCooldownRemaining(-1));
            Assert.AreEqual(0f, caster.GetCooldownRemaining(99));
            Cleanup(caster);
        }

        // --- TryCast ---

        [Test]
        public void TryCast_EmptySlot_ReturnsFalse()
        {
            var caster = CreateCaster();
            bool result = caster.TryCast(0, Vector2.right);
            Assert.IsFalse(result);
            Cleanup(caster);
        }

        [Test]
        public void TryCast_OutOfRange_ReturnsFalse()
        {
            var caster = CreateCaster();
            Assert.IsFalse(caster.TryCast(-1, Vector2.right));
            Assert.IsFalse(caster.TryCast(99, Vector2.right));
            Cleanup(caster);
        }

        [Test]
        public void TryCast_WithPrepare_EntersPreparePhase()
        {
            var caster = CreateCaster();
            caster.SetSpell(0, CreateSpell("fireball", prepare: 1f));
            bool result = caster.TryCast(0, Vector2.right);
            Assert.IsTrue(result);
            Assert.AreEqual(SpellCaster.CastPhase.Prepare, caster.CurrentPhase);
            Assert.AreEqual(0, caster.ActiveSlot);
            Cleanup(caster);
        }

        [Test]
        public void TryCast_WhileCasting_ReturnsFalse()
        {
            var caster = CreateCaster();
            caster.SetSpell(0, CreateSpell("fireball", prepare: 1f));
            caster.SetSpell(1, CreateSpell("iceball", prepare: 1f));
            caster.TryCast(0, Vector2.right);
            bool second = caster.TryCast(1, Vector2.right);
            Assert.IsFalse(second);
            Cleanup(caster);
        }

        [Test]
        public void TryCast_WithManaCostAndEnoughMana_ConsumesMana()
        {
            var caster = CreateCasterWithMana(currentMana: 10, maxMana: 10);
            caster.SetSpell(0, CreateSpell("fireball", manaCost: 3f));

            bool result = caster.TryCast(0, Vector2.right);

            var mana = caster.GetComponent<Mana>();
            Assert.IsTrue(result);
            Assert.IsNotNull(mana);
            Assert.AreEqual(7, mana.CurrentMana);
            Cleanup(caster);
        }

        [Test]
        public void TryCast_WithManaCostAndInsufficientMana_ReturnsFalse()
        {
            var caster = CreateCasterWithMana(currentMana: 2, maxMana: 10);
            caster.SetSpell(0, CreateSpell("fireball", manaCost: 5f));

            bool result = caster.TryCast(0, Vector2.right);

            var mana = caster.GetComponent<Mana>();
            Assert.IsFalse(result);
            Assert.IsNotNull(mana);
            Assert.AreEqual(2, mana.CurrentMana);
            Cleanup(caster);
        }

        [Test]
        public void TryCast_WithManaCostAndNoManaComponent_ReturnsFalse()
        {
            var caster = CreateCaster();
            caster.SetSpell(0, CreateSpell("fireball", manaCost: 2f));

            LogAssert.Expect(LogType.Warning,
                "[SpellCaster] Spell 'fireball' requires mana (2) but no Mana component is present on 'Caster'. Cast cancelled.");
            bool result = caster.TryCast(0, Vector2.right);

            Assert.IsFalse(result);
            Cleanup(caster);
        }

        [Test]
        public void CanCast_WithManaCostAndInsufficientMana_ReturnsFalse()
        {
            var caster = CreateCasterWithMana(currentMana: 1, maxMana: 10);
            caster.SetSpell(0, CreateSpell("fireball", manaCost: 3f));

            bool canCast = caster.CanCast(0);

            Assert.IsFalse(canCast);
            Cleanup(caster);
        }
    }
}
