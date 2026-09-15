using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.SpellBar
{
    /// <summary>
    /// Which slots each posture's face of the action bar carries — the decision, without a canvas.
    ///
    /// <para>The bar these replace printed 24 keys of which 24 were wrong, offered 8 slots that
    /// could hold nothing, and cast damage in Peace. Every test here pins one half of "the face
    /// shows exactly what the player can do right now".</para>
    /// </summary>
    [TestFixture]
    public class SpellBarModelTests
    {
        [SetUp]
        public void SetUp() => InputContextPolicy.ResetForTests();

        [TearDown]
        public void TearDown() => InputContextPolicy.ResetForTests();

        private static List<string> Keys(List<SpellBarEntry> entries) => entries.Select(e => e.Key).ToList();

        [Test]
        public void War_ShowsOnlyKnownSpells_InCatalogOrder_ThenTheSwitch()
        {
            var known = new HashSet<string> { "teleport", "iceball", "darkball" };
            var war = SpellBarModel.War(InputActionCatalog.Spells(), known.Contains, 5);

            Assert.AreEqual(new[] { "darkball", "iceball", "teleport", SpellBarModel.StanceKey }, Keys(war).ToArray(),
                "Catalog order is the keyboard's order: the number row first, then the letters.");
            Assert.AreEqual(SpellBarEntryKind.Stance, war[war.Count - 1].Kind);
        }

        [Test]
        public void War_EverySpellSlot_NamesTheCatalogActionThatCastsIt()
        {
            var war = SpellBarModel.War(InputActionCatalog.Spells(), k => true, 5);
            foreach (var e in war.Where(e => e.Kind == SpellBarEntryKind.Spell))
            {
                var d = InputActionCatalog.Find(e.ActionId);
                Assert.IsNotNull(d, e.Key + " has no catalog action, so its key cap would be blank.");
                Assert.AreEqual(e.Key, d.PayloadKey,
                    "The key cap must be the key that casts THIS spell. The old bar labelled slot 1 with a key that cast another.");
            }
        }

        [Test]
        public void War_OnePagePerKeyboardLayer_AndNoSpellOnBoth()
        {
            // The bar's two War pages: bare keys, and the Shift layer while Shift is held. A
            // spell belongs to exactly one — iceball on 2 and ice_lance on Shift+2 are the pair.
            var shifted = new HashSet<string> { "ice_lance" };
            var known = new HashSet<string> { "iceball", "ice_lance" };
            System.Func<InputActionDescriptor, bool> isShift = d => shifted.Contains(d.PayloadKey);

            var bare = SpellBarModel.War(InputActionCatalog.Spells(), known.Contains, 5, onPage: d => !isShift(d));
            var shift = SpellBarModel.War(InputActionCatalog.Spells(), known.Contains, 5, onPage: isShift);

            Assert.AreEqual(new[] { "iceball", SpellBarModel.StanceKey }, Keys(bare).ToArray());
            Assert.AreEqual(new[] { "ice_lance", SpellBarModel.StanceKey }, Keys(shift).ToArray(),
                "Both pages end in the posture switch: it must stay in reach whichever layer is up.");
        }

        [Test]
        public void War_DropsASpellSilencedInWar()
        {
            var war = SpellBarModel.War(InputActionCatalog.Spells(), k => k == "iceball" || k == "darkball", 5,
                                        d => d.PayloadKey != "iceball");
            CollectionAssert.DoesNotContain(Keys(war), "iceball",
                "A spell whose key the player switched off in War cannot be cast by key; the face must not offer it.");
            CollectionAssert.Contains(Keys(war), "darkball");
        }

        [Test]
        public void War_GroupsSpellsInFives_AndTheSwitchIsItsOwnGroup()
        {
            var war = SpellBarModel.War(InputActionCatalog.Spells(), k => true, 5);
            var spells = war.Where(e => e.Kind == SpellBarEntryKind.Spell).ToList();
            Assert.GreaterOrEqual(spells.Count, 7);
            for (int i = 0; i < spells.Count; i++) Assert.AreEqual(i / 5, spells[i].Group);
            Assert.AreEqual(SpellBarModel.StanceGroup, war[war.Count - 1].Group);
        }

        [Test]
        public void Peace_NeverOffersAnythingThatReachesDamage()
        {
            var peace = SpellBarModel.Peace(id => true);
            Assert.IsFalse(peace.Any(e => e.Kind == SpellBarEntryKind.Spell), "Peace must carry no spell slot at all.");
            foreach (var e in peace)
            {
                if (string.IsNullOrEmpty(e.ActionId)) continue;
                var d = InputActionCatalog.Find(e.ActionId);
                Assert.IsNotNull(d, e.ActionId);
                Assert.IsFalse(d.ReachesDamage, e.ActionId + " reaches the damage path and is on the Peace face.");
                Assert.IsTrue(InputContextPolicy.IsLive(d, Stance.Peace), e.ActionId + " is not live in Peace.");
            }
        }

        [Test]
        public void Peace_DropsAVerbSilencedInPeace_AndOneWhoseSurfaceIsMissing()
        {
            var peace = SpellBarModel.Peace(id => id != "crafting", d => d.Action != "Inventory");
            CollectionAssert.DoesNotContain(Keys(peace), "inventory", "Silenced for Peace in the Controls editor.");
            CollectionAssert.DoesNotContain(Keys(peace), "crafting", "No crafting panel in this scene.");
            CollectionAssert.Contains(Keys(peace), "map");
        }

        [Test]
        public void EveryPeaceVerbAction_ExistsInTheCatalog()
        {
            foreach (var v in SpellBarModel.PeaceVerbs)
            {
                if (string.IsNullOrEmpty(v.Action)) continue;
                Assert.IsNotNull(InputActionCatalog.Find(v.ActionId),
                    v.Id + " names " + v.ActionId + ", which the catalog does not have; its key cap would be blank forever.");
            }
        }

        [Test]
        public void BothFaces_EndInTheSwitch_BecauseItIsTheOnlyWayOut()
        {
            var war = SpellBarModel.War(InputActionCatalog.Spells(), k => false, 5);
            var peace = SpellBarModel.Peace(id => false);
            Assert.AreEqual(1, war.Count, "A character who knows no spell still gets the switch.");
            Assert.AreEqual(SpellBarEntryKind.Stance, war[0].Kind);
            Assert.AreEqual(1, peace.Count);
            Assert.AreEqual(SpellBarEntryKind.Stance, peace[0].Kind);
            Assert.AreEqual(SpellBarModel.StanceActionId, peace[0].ActionId);
            Assert.IsNotNull(InputActionCatalog.Find(SpellBarModel.StanceActionId));
        }

        [Test]
        public void Signature_ChangesWithTheFace_AndWithTheContent()
        {
            var a = SpellBarModel.War(InputActionCatalog.Spells(), k => k == "teleport", 5);
            var b = SpellBarModel.War(InputActionCatalog.Spells(), k => k == "teleport" || k == "iceball", 5);
            Assert.AreNotEqual(SpellBarModel.Signature(Stance.War, a), SpellBarModel.Signature(Stance.War, b));
            Assert.AreNotEqual(SpellBarModel.Signature(Stance.War, a), SpellBarModel.Signature(Stance.Peace, a));
            Assert.AreEqual(SpellBarModel.Signature(Stance.War, a), SpellBarModel.Signature(Stance.War, a));
        }
    }
}
