using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Gameplay.Vendors
{
    /// <summary>
    /// The vendor price pipeline, and specifically the three of its seven steps that were
    /// DEAD in shipped data when this was written.
    ///
    /// <para>The audit that produced these tests found the pipeline's architecture sound and
    /// its wiring absent: no vendor referenced an economy group, no vendor carried a price
    /// override, and not one of the six call sites of <c>GetBuyPrice</c>/<c>GetSellPrice</c>
    /// ever passed a negotiation discount — so three steps resolved to the identity and every
    /// counter in the world charged the same. A step that cannot be observed is a step that
    /// can be broken without anything failing, which is what these fixtures now prevent.</para>
    /// </summary>
    public class EconomyPipelineTests
    {
        private GameObject _serviceGo;
        private VendorEconomyService _service;
        private readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _serviceGo = new GameObject("VendorEconomyService_Test");
            _service = _serviceGo.AddComponent<VendorEconomyService>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_serviceGo != null) Object.DestroyImmediate(_serviceGo);
            foreach (var a in _assets) if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();
        }

        private ItemDefinition MakeItem(string id, string type, int buy, int sell, bool stackable = true)
        {
            var it = ScriptableObject.CreateInstance<ItemDefinition>();
            it.itemId = id;
            it.itemType = type;
            it.buyPrice = buy;
            it.sellPrice = sell;
            it.stackable = stackable;
            _assets.Add(it);
            return it;
        }

        private EconomyGroupDefinition MakeGroup(float defBuy, float defSell,
                                                 params (string type, float buy, float sell)[] types)
        {
            var g = ScriptableObject.CreateInstance<EconomyGroupDefinition>();
            g.defaultMargin = new EconomyGroupDefinition.MarginEntry
            { buyMultiplier = defBuy, sellMultiplier = defSell };
            foreach (var t in types)
            {
                g.typeMargins.Add(new EconomyGroupDefinition.TypeMarginEntry
                {
                    itemType = t.type,
                    margin = new EconomyGroupDefinition.MarginEntry
                    { buyMultiplier = t.buy, sellMultiplier = t.sell },
                });
            }
            _assets.Add(g);
            return g;
        }

        private VendorConfigDefinition MakeConfig(EconomyGroupDefinition group)
        {
            var c = ScriptableObject.CreateInstance<VendorConfigDefinition>();
            c.vendorKey = "test_vendor";
            c.economyGroup = group;
            _assets.Add(c);
            return c;
        }

        /// <summary>
        /// A specialist's own trade is the honest price; everything else carries the spread.
        /// This is the entire reason to walk across town to the right counter, and it is
        /// expressed purely in data.
        /// </summary>
        [Test]
        public void TypeMargin_MakesASpecialistCheaperThanAGeneralist()
        {
            var group = MakeGroup(1.35f, 0.70f, ("mineral", 1.00f, 1.00f));
            var config = MakeConfig(group);

            var ore = MakeItem("iron_ore", "mineral", 100, 50);
            var bread = MakeItem("bread", "food", 100, 50);

            Assert.AreEqual(100, _service.GetBuyPrice(config, ore), "specialty buy took a margin it should not");
            Assert.AreEqual(50, _service.GetSellPrice(config, ore), "specialty sell took a margin it should not");

            Assert.AreEqual(135, _service.GetBuyPrice(config, bread), "outside the trade should be dearer");
            Assert.AreEqual(35, _service.GetSellPrice(config, bread), "outside the trade should pay less");
        }

        /// <summary>
        /// Most specific wins: per-item beats per-type beats the group default. Getting this
        /// order wrong is silent — every price still looks plausible, it is just answering a
        /// less specific question than the designer asked.
        /// </summary>
        [Test]
        public void MarginResolution_PrefersItemThenTypeThenDefault()
        {
            var group = MakeGroup(2f, 2f, ("mineral", 3f, 3f));
            group.itemMargins.Add(new EconomyGroupDefinition.ItemMarginEntry
            {
                itemKey = "special_ore",
                margin = new EconomyGroupDefinition.MarginEntry { buyMultiplier = 4f, sellMultiplier = 4f },
            });

            Assert.AreEqual(4f, group.GetMargin("special_ore", "mineral").buyMultiplier, "item override lost to type");
            Assert.AreEqual(3f, group.GetMargin("plain_ore", "mineral").buyMultiplier, "type margin lost to default");
            Assert.AreEqual(2f, group.GetMargin("plain_ore", "food").buyMultiplier, "unknown type did not fall to default");
            Assert.AreEqual(2f, group.GetMargin("plain_ore").buyMultiplier, "the one-argument shape changed meaning");
        }

        /// <summary>
        /// Negotiation must point at the player's benefit on BOTH sides of the counter.
        ///
        /// <para>The shipped code applied the same DISCOUNT to buying and to selling, so the
        /// friendliest character in the world also paid the least for what you brought in.
        /// It survived because no caller ever passed a non-zero discount: the direction bug
        /// and the field's inertness were hiding each other, and fixing only one would have
        /// shipped the other.</para>
        /// </summary>
        [Test]
        public void Negotiation_LowersWhatYouPay_AndRaisesWhatYouGet()
        {
            var config = MakeConfig(MakeGroup(1f, 1f));
            var item = MakeItem("thing", "misc", 100, 100);

            int buyPlain = _service.GetBuyPrice(config, item, 0f);
            int buyHaggled = _service.GetBuyPrice(config, item, 0.20f);
            int sellPlain = _service.GetSellPrice(config, item, 0f);
            int sellHaggled = _service.GetSellPrice(config, item, 0.20f);

            Assert.AreEqual(100, buyPlain);
            Assert.AreEqual(80, buyHaggled, "haggling did not make buying cheaper");
            Assert.AreEqual(100, sellPlain);
            Assert.AreEqual(120, sellHaggled, "haggling made SELLING worse — the discount is pointed the wrong way");
        }

        /// <summary>
        /// A price floor exists so nothing is ever free. Worth pinning on both sides: a
        /// Trough plus a deep discount on a 1-coin item is exactly where a rounding path
        /// reaches zero, and a zero-cost item in a shop is an infinite inventory.
        /// </summary>
        [Test]
        public void NothingIsEverFree()
        {
            var config = MakeConfig(MakeGroup(0.1f, 0.1f));
            var item = MakeItem("scrap", "misc", 1, 1);

            Assert.That(_service.GetBuyPrice(config, item, 0.9f), Is.GreaterThanOrEqualTo(1));
            Assert.That(_service.GetSellPrice(config, item, 0.9f), Is.GreaterThanOrEqualTo(1));
        }

        /// <summary>
        /// With no market service in the scene — every EditMode test, and any scene built
        /// before the cycle existed — prices must resolve exactly as they did before the
        /// layer was added. A pipeline that shifts when an optional service is absent cannot
        /// be tested in isolation, and every fixture above would be measuring the cycle
        /// instead of the margins.
        /// </summary>
        [Test]
        public void WithNoMarketRunning_PricesAreUnchanged()
        {
            Assert.IsFalse(MarketService.HasInstance,
                "a MarketService leaked in from another fixture; these prices would be cycle-shifted");

            var config = MakeConfig(MakeGroup(1f, 1f));
            var item = MakeItem("thing", "misc", 250, 125);

            Assert.AreEqual(250, _service.GetBuyPrice(config, item));
            Assert.AreEqual(125, _service.GetSellPrice(config, item));
        }
    }
}
