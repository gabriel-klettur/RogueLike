using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Gameplay.Vendors
{
    /// <summary>
    /// The vendor's own money — the half of a shop that makes it a participant in the economy
    /// rather than a wall.
    ///
    /// <para>Before this, a vendor had infinite coin and finite stock that never came back,
    /// which is exactly backwards: the player could sell an unbounded quantity of anything
    /// forever, so the game's only real gold faucet was uncapped, while the shop itself ran
    /// dry and stayed dry.</para>
    /// </summary>
    public class VendorPurseTests
    {
        private readonly List<Object> _assets = new List<Object>();
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            foreach (var a in _assets) if (a != null) Object.DestroyImmediate(a);
            _objects.Clear();
            _assets.Clear();
        }

        private ItemDefinition MakeItem(int buy, int sell)
        {
            var it = ScriptableObject.CreateInstance<ItemDefinition>();
            it.itemId = "widget";
            it.itemType = "misc";
            it.buyPrice = buy;
            it.sellPrice = sell;
            it.stackable = true;
            it.maxStack = 99;
            _assets.Add(it);
            return it;
        }

        /// <summary>
        /// A vendor plus the pricing service it reads. Built as real components because the
        /// purse rules live on <c>VendorNPC</c> and the price it debits has to be the one the
        /// counter actually charges — asserting against a hand-computed price would let the
        /// two drift.
        /// </summary>
        private VendorNPC MakeVendor(ItemDefinition stocked, int stock, int coinFloat)
        {
            var svcGo = new GameObject("EconomyService");
            svcGo.AddComponent<VendorEconomyService>();
            _objects.Add(svcGo);

            var cfg = ScriptableObject.CreateInstance<VendorConfigDefinition>();
            cfg.vendorKey = "test_vendor";
            cfg.coinFloat = coinFloat;
            cfg.inventorySeed.Add(new VendorConfigDefinition.SeedSlot { item = stocked, quantity = stock });
            _assets.Add(cfg);

            var go = new GameObject("Vendor");
            go.AddComponent<NPCInteractable>();
            var vendor = go.AddComponent<VendorNPC>();
            _objects.Add(go);

            vendor.Configure(cfg);
            return vendor;
        }

        /// <summary>
        /// Units of <paramref name="item"/> held, summed across stacks. <c>UsedSlots</c> is
        /// the wrong measure for a stackable item — five of them are one slot.
        /// </summary>
        private static int CountOf(Inventory inv, ItemDefinition item)
        {
            int n = 0;
            for (int i = 0; i < inv.Slots.Count; i++)
                if (!inv.Slots[i].IsEmpty && inv.Slots[i].Item == item) n += inv.Slots[i].Quantity;
            return n;
        }

        private (Inventory, CurrencyWallet) MakePlayer(int coins, ItemDefinition carrying, int qty)
        {
            var go = new GameObject("Player");
            _objects.Add(go);

            var inv = go.AddComponent<Inventory>();
            inv.Initialize(Inventory.DefaultBagCapacity);
            for (int i = 0; i < qty; i++) inv.AddItem(carrying);

            var wallet = go.AddComponent<CurrencyWallet>();
            wallet.SetBalance(coins);
            return (inv, wallet);
        }

        /// <summary>
        /// 0 means unlimited, and it has to, because a key absent from an already-shipped
        /// <c>.asset</c> deserialises to 0. Reading that as an EMPTY purse would have made all
        /// five shipped vendors refuse to buy anything the moment the field was added —
        /// silently, and only for players who tried to sell.
        /// </summary>
        [Test]
        public void ZeroCoinFloat_MeansUnlimited_NotBroke()
        {
            var item = MakeItem(100, 50);
            var vendor = MakeVendor(item, 1, coinFloat: 0);
            var (inv, wallet) = MakePlayer(0, item, 4);

            Assert.IsFalse(vendor.HasLimitedPurse);
            Assert.AreEqual(int.MaxValue, vendor.AffordableUnits(50));

            for (int i = 0; i < 4; i++)
                Assert.IsTrue(vendor.TrySellItem(item, inv, wallet), $"sale {i + 1} was refused by an unlimited purse");
        }

        /// <summary>
        /// A finite purse actually runs out, and the refusal happens BEFORE the item leaves
        /// the bag. The other order destroys the player's goods for a vendor who then turns
        /// out to be broke, which is the one failure a trade must never have.
        ///
        /// <para><b>The unit price is READ from the vendor, never assumed.</b> In EditMode
        /// <c>AddComponent</c> does not run <c>Awake</c>, so <c>VendorEconomyService.Instance</c>
        /// is null however carefully the fixture builds one — and <c>GetSellPrice</c> then
        /// takes its legacy branch, which is a flat <c>sellPriceMultiplier</c> of 0.5 rather
        /// than the margin pipeline. A test that hard-codes the arithmetic is measuring which
        /// pricing path happened to be live, not whether the purse empties: this fixture first
        /// shipped asserting a 50-coin price against a 25-coin reality and failed a correct
        /// implementation. Ask the vendor what it charges, then count.</para>
        /// </summary>
        [Test]
        public void AFinitePurse_RunsOut_AndTheItemIsNotLost()
        {
            const int Float = 120;
            var item = MakeItem(100, 50);
            var vendor = MakeVendor(item, 1, coinFloat: Float);
            var (inv, wallet) = MakePlayer(0, item, 12);

            int unitPrice = vendor.GetSellPrice(item);
            Assert.That(unitPrice, Is.GreaterThan(0), "the vendor quoted a free purchase");

            int coverable = Float / unitPrice;
            Assert.That(coverable, Is.InRange(1, 11),
                "the fixture's float must cover at least one sale and fewer than the bag holds");

            for (int i = 0; i < coverable; i++)
                Assert.IsTrue(vendor.TrySellItem(item, inv, wallet), $"sale {i + 1} of {coverable} was refused");

            // QUANTITY, not UsedSlots. The item is stackable, so twelve of them occupy one
            // slot and a slot count cannot see one leaving — the assertion would pass on a
            // bag the sale had emptied.
            int heldBefore = CountOf(inv, item);
            int paidBefore = wallet.Coins;

            Assert.IsFalse(vendor.TrySellItem(item, inv, wallet),
                $"a purse of {Float - coverable * unitPrice} bought a {unitPrice}-coin item");
            Assert.AreEqual(heldBefore, CountOf(inv, item), "the item left the bag on a refused sale");
            Assert.AreEqual(paidBefore, wallet.Coins, "the player was paid for a refused sale");
        }

        /// <summary>
        /// What the player spends goes INTO the vendor's purse. Without it money leaves the
        /// world at the counter and a vendor's float only ever falls, which makes a busy shop
        /// permanently insolvent instead of busy.
        /// </summary>
        [Test]
        public void BuyingFromAVendor_RefillsTheirPurse()
        {
            var item = MakeItem(100, 50);
            var vendor = MakeVendor(item, 5, coinFloat: 60);
            var (inv, wallet) = MakePlayer(500, item, 0);

            int before = vendor.Coins;
            Assert.IsTrue(vendor.TryBuyItem(item, inv, wallet), "purchase refused");
            Assert.That(vendor.Coins, Is.GreaterThan(before), "the vendor took the money and did not keep it");
        }

        /// <summary>
        /// The affordability cut, which is the mirror of the one <c>ChatTradeBroker</c>
        /// already makes on the PLAYER's purse. "You can have two" is the answer a shopkeeper
        /// gives; refusing five because they cannot afford all five is the same mistake in the
        /// other direction.
        /// </summary>
        [Test]
        public void AffordableUnits_CutsTheQuantity_RatherThanRefusing()
        {
            var item = MakeItem(100, 50);
            var vendor = MakeVendor(item, 1, coinFloat: 125);

            Assert.AreEqual(2, vendor.AffordableUnits(50), "a 125 purse should cover two 50-coin units");
            Assert.AreEqual(0, vendor.AffordableUnits(200), "an unaffordable unit price should cut to zero");
        }

        /// <summary>
        /// A zero unit price is unaffordable, not infinite. The pipeline floors prices at 1 so
        /// it cannot arrive today — but dividing by it is the one way this method could hand
        /// back a number that empties a shop, and the guard costs a line.
        /// </summary>
        [Test]
        public void AZeroUnitPrice_IsRefused_NotTreatedAsInfinite()
        {
            var item = MakeItem(100, 50);
            var vendor = MakeVendor(item, 1, coinFloat: 500);
            Assert.AreEqual(0, vendor.AffordableUnits(0));
        }
    }
}
