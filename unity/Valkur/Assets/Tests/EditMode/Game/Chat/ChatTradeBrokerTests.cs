using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Coverage for the only code in this game that spends a player's money on the strength
    /// of a sentence they typed.
    ///
    /// <para>A language model proposes; this decides. Nothing the model sends is trusted —
    /// not the item id, not the direction, not the count — because the failure it exists to
    /// prevent is already on record: Gatita offered apples, pears and plums she does not
    /// stock and then said "aquí tienes, dos manzanas" for a sale that moved nothing. Given
    /// a tool to call, a model that confident will call it with an id it invented.</para>
    ///
    /// <para>So the assertions here are adversarial. They feed the broker ids that do not
    /// exist, counts of zero and five hundred, purchases with an empty purse and sales of
    /// things the player is not carrying, and check that each one is refused with a reason a
    /// player can read rather than accepted, thrown, or silently clamped to something
    /// surprising.</para>
    /// </summary>
    [TestFixture]
    public class ChatTradeBrokerTests
    {
        private readonly List<Object> _scene = new List<Object>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _scene) if (o != null) Object.DestroyImmediate(o);
            _scene.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private T Track<T>(T o) where T : Object { _scene.Add(o); return o; }

        private ItemDefinition MakeItem(string id, int buyPrice)
        {
            var item = Track(ScriptableObject.CreateInstance<ItemDefinition>());
            item.itemId = id;
            item.displayName = id.ToUpperInvariant();
            item.buyPrice = buyPrice;
            item.stackable = true;
            item.maxStack = 99;
            return item;
        }

        /// <summary>A vendor stocking one item, built the way EntitySetup builds one.</summary>
        private VendorNPC MakeVendor(string itemId, int price, int stock)
        {
            var go = Track(new GameObject("vendor"));
            go.AddComponent<NPCInteractable>().Configure("Tendera", 5f);

            var config = Track(ScriptableObject.CreateInstance<VendorConfigDefinition>());
            config.vendorKey = "test_vendor";
            config.inventorySeed.Add(new VendorConfigDefinition.SeedSlot
            {
                item = MakeItem(itemId, price),
                quantity = stock,
            });

            var vendor = go.AddComponent<VendorNPC>();
            vendor.Configure(config);
            return vendor;
        }

        private Valkur.Gameplay.Inventory.Inventory MakeInventory()
        {
            var go = Track(new GameObject("bag"));
            return go.AddComponent<Valkur.Gameplay.Inventory.Inventory>();
        }

        private CurrencyWallet MakeWallet(int coins)
        {
            var go = Track(new GameObject("purse"));
            var wallet = go.AddComponent<CurrencyWallet>();
            wallet.SetBalance(coins);
            return wallet;
        }

        private static TradeProposal Buy(string id, int qty) =>
            new TradeProposal(TradeIntent.Buy, id, qty);

        private static TradeProposal Sell(string id, int qty) =>
            new TradeProposal(TradeIntent.Sell, id, qty);

        // ── Refusals: everything the model can get wrong ────────────────────

        [Test]
        public void Quote_ItemThatDoesNotExist_IsRefused()
        {
            var quote = ChatTradeBroker.Quote(
                Buy("manzana_dorada", 2), MakeVendor("borsh", 15, 5), MakeInventory(), MakeWallet(100));

            Assert.IsFalse(quote.IsValid,
                "This is the exact failure on record: a model offering apples in a shop that " +
                "sells soup. An id it invented must be refused, never looked up loosely.");
            Assert.IsNotEmpty(quote.Refusal, "A refusal the player cannot read is a silent no-op.");
        }

        [Test]
        public void Quote_NoVendor_IsRefused()
        {
            var quote = ChatTradeBroker.Quote(Buy("borsh", 1), null, MakeInventory(), MakeWallet(100));
            Assert.IsFalse(quote.IsValid);
        }

        [Test]
        public void Quote_EmptyProposal_IsRefusedWithoutComplaint()
        {
            var quote = ChatTradeBroker.Quote(
                TradeProposal.None, MakeVendor("borsh", 15, 5), MakeInventory(), MakeWallet(100));

            Assert.IsFalse(quote.IsValid);
            Assert.IsEmpty(quote.Refusal,
                "Nothing was proposed, so there is nothing to tell the player. A refusal line " +
                "here would put 'no puedo' under every ordinary sentence.");
        }

        [Test]
        public void Quote_EmptyPurse_IsRefusedWithTheRealNumbers()
        {
            var quote = ChatTradeBroker.Quote(
                Buy("borsh", 1), MakeVendor("borsh", 15, 5), MakeInventory(), MakeWallet(0));

            Assert.IsFalse(quote.IsValid);
            StringAssert.Contains("15", quote.Refusal, "The refusal must quote the real price.");
        }

        [Test]
        public void Quote_OutOfStock_IsRefused()
        {
            var vendor = MakeVendor("borsh", 15, 1);
            var inventory = MakeInventory();
            var wallet = MakeWallet(100);

            // Clear the shelf through the same path the Buy button uses.
            Assert.IsTrue(vendor.TryBuyItem(vendor.ShopInventory[0].item, inventory, wallet));

            var quote = ChatTradeBroker.Quote(Buy("borsh", 1), vendor, inventory, wallet);
            Assert.IsFalse(quote.IsValid);
        }

        [Test]
        public void Quote_SellingSomethingNotCarried_IsRefused()
        {
            var quote = ChatTradeBroker.Quote(
                Sell("borsh", 1), MakeVendor("borsh", 15, 5), MakeInventory(), MakeWallet(0));

            Assert.IsFalse(quote.IsValid,
                "A model that decides the player is selling something they do not have would " +
                "otherwise mint coins out of nothing.");
        }

        // ── Clamping: the model asks for more than the world can give ───────

        [Test]
        public void Quote_MoreThanIsAffordable_IsCutToWhatTheCoinsReach()
        {
            var quote = ChatTradeBroker.Quote(
                Buy("borsh", 10), MakeVendor("borsh", 15, 99), MakeInventory(), MakeWallet(38));

            Assert.IsTrue(quote.IsValid, "Two are affordable, so this is a smaller yes, not a no.");
            Assert.AreEqual(2, quote.Quantity, "38 coins buys two at 15, not ten.");
            Assert.AreEqual(30, quote.TotalPrice, "And the total is for what is actually being sold.");
        }

        [Test]
        public void Quote_MoreThanIsInStock_IsCutToStock()
        {
            var quote = ChatTradeBroker.Quote(
                Buy("borsh", 10), MakeVendor("borsh", 5, 3), MakeInventory(), MakeWallet(1000));

            Assert.AreEqual(3, quote.Quantity);
            Assert.AreEqual(15, quote.TotalPrice);
        }

        [Test]
        public void Quote_AbsurdQuantity_IsCappedRatherThanHonoured()
        {
            var quote = ChatTradeBroker.Quote(
                Buy("borsh", 500), MakeVendor("borsh", 1, 9999), MakeInventory(), MakeWallet(999999));

            Assert.LessOrEqual(quote.Quantity, ChatTradeBroker.MAX_UNITS_PER_TRADE,
                "A misread 'dame unos cuantos' arriving as 500 would clear a purse and fill a " +
                "bag on one confirmation. The player can always ask again.");
        }

        [TestCase(0)]
        [TestCase(-4)]
        public void Quote_NonPositiveQuantity_BecomesOne(int asked)
        {
            var quote = ChatTradeBroker.Quote(
                Buy("borsh", asked), MakeVendor("borsh", 5, 9), MakeInventory(), MakeWallet(100));

            Assert.IsTrue(quote.IsValid);
            Assert.AreEqual(1, quote.Quantity,
                "Zero of something is a confirmation prompt nobody can make sense of.");
        }

        [Test]
        public void Quote_ItemIdCasing_DoesNotMatter()
        {
            var quote = ChatTradeBroker.Quote(
                Buy("BORSH", 1), MakeVendor("borsh", 5, 9), MakeInventory(), MakeWallet(100));

            Assert.IsTrue(quote.IsValid,
                "The id is echoed back by a model that may have re-capitalised it, and an " +
                "exact-case miss reads as the shop denying it sells what it plainly does.");
        }

        [Test]
        public void Quote_PriceComesFromTheShop_NotFromTheItem()
        {
            var vendor = MakeVendor("borsh", 15, 9);
            var item = vendor.ShopInventory[0].item;

            var quote = ChatTradeBroker.Quote(Buy("borsh", 1), vendor, MakeInventory(), MakeWallet(100));

            Assert.AreEqual(vendor.GetBuyPrice(item), quote.TotalPrice,
                "Same call the Buy button makes, so what the character quotes and what the " +
                "counter charges cannot drift apart.");
        }

        // ── Execution ───────────────────────────────────────────────────────

        [Test]
        public void Execute_MovesCoinsAndGoods()
        {
            var vendor = MakeVendor("borsh", 15, 9);
            var inventory = MakeInventory();
            var wallet = MakeWallet(100);

            var quote = ChatTradeBroker.Quote(Buy("borsh", 2), vendor, inventory, wallet);
            int done = ChatTradeBroker.Execute(quote, vendor, inventory, wallet);

            Assert.AreEqual(2, done);
            Assert.AreEqual(70, wallet.Coins, "Two at 15 leaves 70 of 100.");
            Assert.AreEqual(2, CountHeld(inventory, quote.Item));
        }

        [Test]
        public void Execute_DecrementsTheVendorsStock()
        {
            var vendor = MakeVendor("borsh", 5, 4);
            var inventory = MakeInventory();
            var wallet = MakeWallet(100);

            var quote = ChatTradeBroker.Quote(Buy("borsh", 3), vendor, inventory, wallet);
            ChatTradeBroker.Execute(quote, vendor, inventory, wallet);

            Assert.AreEqual(1, vendor.ShopInventory[0].stock,
                "A chat purchase must consume the same shelf the counter sells from, or the " +
                "two views of the shop diverge.");
        }

        [Test]
        public void Execute_Selling_ReturnsCoinsAndRemovesTheItem()
        {
            var vendor = MakeVendor("borsh", 15, 9);
            var inventory = MakeInventory();
            var wallet = MakeWallet(100);

            var bought = ChatTradeBroker.Quote(Buy("borsh", 1), vendor, inventory, wallet);
            ChatTradeBroker.Execute(bought, vendor, inventory, wallet);
            int afterBuying = wallet.Coins;

            var sold = ChatTradeBroker.Quote(Sell("borsh", 1), vendor, inventory, wallet);
            Assert.IsTrue(sold.IsValid);
            int done = ChatTradeBroker.Execute(sold, vendor, inventory, wallet);

            Assert.AreEqual(1, done);
            Assert.Greater(wallet.Coins, afterBuying, "Selling must pay.");
            Assert.AreEqual(0, CountHeld(inventory, bought.Item));
        }

        [Test]
        public void Execute_AnInvalidQuote_DoesNothing()
        {
            var vendor = MakeVendor("borsh", 15, 9);
            var inventory = MakeInventory();
            var wallet = MakeWallet(100);

            int done = ChatTradeBroker.Execute(TradeQuote.No("nope"), vendor, inventory, wallet);

            Assert.AreEqual(0, done);
            Assert.AreEqual(100, wallet.Coins, "A refused quote must not be executable at all.");
        }

        [Test]
        public void Execute_StopsAtTheFirstUnitThatFails()
        {
            var vendor = MakeVendor("borsh", 10, 5);
            var inventory = MakeInventory();
            var wallet = MakeWallet(100);

            var quote = ChatTradeBroker.Quote(Buy("borsh", 3), vendor, inventory, wallet);

            // Empty the purse behind the quote's back — the world moved between the offer and
            // the tap, which over a conversation is a normal amount of time.
            wallet.SetBalance(10);
            int done = ChatTradeBroker.Execute(quote, vendor, inventory, wallet);

            Assert.AreEqual(1, done,
                "One unit is affordable and the rest are not. Executing must report what " +
                "really happened, not the number that was quoted.");
            Assert.AreEqual(0, wallet.Coins);
        }

        // ── The offer the player is asked to confirm ────────────────────────

        [Test]
        public void Quote_ValidOffer_CarriesEverythingTheConfirmationNeeds()
        {
            var quote = ChatTradeBroker.Quote(
                Buy("borsh", 2), MakeVendor("borsh", 15, 9), MakeInventory(), MakeWallet(100));

            // The confirmation row is built from these three and nothing the model said, so
            // what the player agrees to is exactly what will happen.
            Assert.IsNotNull(quote.Item, "Without the item there is nothing to name in the row.");
            Assert.AreEqual(2, quote.Quantity);
            Assert.AreEqual(30, quote.TotalPrice);
            Assert.AreEqual(TradeIntent.Buy, quote.Intent,
                "Direction decides whether the row reads 'Comprar' or 'Vender', and whether " +
                "the coin flourish flies out or in.");
        }

        [Test]
        public void Execute_IsNotReachableWithoutQuotingFirst()
        {
            var vendor = MakeVendor("borsh", 15, 9);
            var inventory = MakeInventory();
            var wallet = MakeWallet(100);

            // A default quote is what a caller gets by skipping Quote entirely. It must be
            // inert: this is the only path in the game where a language model's output could
            // otherwise reach a wallet unchecked.
            int done = ChatTradeBroker.Execute(default, vendor, inventory, wallet);

            Assert.AreEqual(0, done);
            Assert.AreEqual(100, wallet.Coins);
        }

        private static int CountHeld(Valkur.Gameplay.Inventory.Inventory inventory, ItemDefinition item)
        {
            int held = 0;
            foreach (var slot in inventory.Slots)
                if (!slot.IsEmpty && slot.Item == item) held += slot.Quantity;
            return held;
        }
    }
}
