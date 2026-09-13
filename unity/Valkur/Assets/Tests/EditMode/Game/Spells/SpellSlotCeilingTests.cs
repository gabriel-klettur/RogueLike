using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// A caster may hold as many spells as it is given.
    ///
    /// <para><c>SpellCaster</c> serializes four slots, which is a reasonable number of buttons
    /// for a player and was never a statement about how many abilities a creature may have.
    /// <c>SetSpell</c> used to DROP any index past that, silently, and both
    /// <c>EntitySetup.ConfigureMonsterAutoCast</c> and <c>BossConfigurator</c> cut their lists
    /// at it — so a fifth authored key was registered in the spell book, listed by every tool
    /// that reads the book, and cast by nobody. <c>dark_mague</c> and <c>dark_vampire</c> ship
    /// exactly four, which is what a ceiling looks like from the inside just before somebody
    /// walks into it.</para>
    /// </summary>
    public class SpellSlotCeilingTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
        }

        private SpellCaster NewCaster()
        {
            _go = new GameObject("SlotCeilingProbe");
            return _go.AddComponent<SpellCaster>();
        }

        private static SpellDefinition Spell(string key)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = key;
            spell.displayName = key;
            return spell;
        }

        [Test]
        public void ASlotPastTheSerializedFour_IsKeptRatherThanDropped()
        {
            var caster = NewCaster();
            int before = caster.SlotCount;

            var fifth = Spell("fifth");
            caster.SetSpell(before, fifth);

            Assert.That(caster.SlotCount, Is.GreaterThan(before),
                "The array must grow. Silently refusing is what made a fifth authored spell " +
                "invisible: it was in the book, in every listing, and never cast.");
            Assert.That(caster.GetSpellAtSlot(before), Is.SameAs(fifth));

            Object.DestroyImmediate(fifth);
        }

        [Test]
        public void GrowingKeepsWhatWasAlreadyInTheEarlierSlots()
        {
            var caster = NewCaster();
            var first = Spell("first");
            var far = Spell("far");

            caster.SetSpell(0, first);
            caster.SetSpell(9, far);

            Assert.That(caster.GetSpellAtSlot(0), Is.SameAs(first),
                "Widening the array must copy, not replace: a caster mid-fight would " +
                "otherwise lose every spell it already held the moment it learned one more.");
            Assert.That(caster.GetSpellAtSlot(9), Is.SameAs(far));

            Object.DestroyImmediate(first);
            Object.DestroyImmediate(far);
        }

        [Test]
        public void ANegativeSlotIsRefusedOutright()
        {
            var caster = NewCaster();
            int before = caster.SlotCount;

            Assert.DoesNotThrow(() => caster.SetSpell(-1, Spell("nope")));
            Assert.That(caster.SlotCount, Is.EqualTo(before),
                "There is no reading of a negative index that means anything, so it must not " +
                "size the array -- SetSpell(-1) allocating is how a typo becomes an " +
                "OutOfMemory instead of a no-op.");
        }
    }
}
