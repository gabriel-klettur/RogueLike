using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Chat.Providers;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.NPC;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// A proposal checked against the live world: what it would really cost, whether it can
    /// happen at all, and the sentence to put in front of the player.
    /// </summary>
    public readonly struct TradeQuote
    {
        public bool IsValid { get; }
        public TradeIntent Intent { get; }
        public ItemDefinition Item { get; }

        /// <summary>Units the world can actually honour. May be fewer than were proposed.</summary>
        public int Quantity { get; }

        /// <summary>Total coins moving, for <see cref="Quantity"/> units at the shop's price.</summary>
        public int TotalPrice { get; }

        /// <summary>Why it cannot happen, in the player's language. Empty when it can.</summary>
        public string Refusal { get; }

        private TradeQuote(bool valid, TradeIntent intent, ItemDefinition item,
                           int quantity, int totalPrice, string refusal)
        {
            IsValid = valid;
            Intent = intent;
            Item = item;
            Quantity = quantity;
            TotalPrice = totalPrice;
            Refusal = refusal;
        }

        public static TradeQuote Ok(TradeIntent intent, ItemDefinition item, int quantity, int total) =>
            new TradeQuote(true, intent, item, quantity, total, "");

        public static TradeQuote No(string reason) =>
            new TradeQuote(false, TradeIntent.None, null, 0, 0, reason);
    }

    /// <summary>
    /// Turns what a language model OFFERED into what the game will actually do — and does it.
    ///
    /// <para>THE MODEL IS NOT TRUSTED WITH ANYTHING. It picks an item id, a direction and a
    /// count; every one of those is looked up here against the live shop, the live purse and
    /// the live inventory, and the price is read from the same <c>GetBuyPrice</c> the Buy
    /// button charges. An id that does not exist is refused, a quantity beyond stock is cut
    /// down to stock, and a total beyond the purse is cut down to what the coins reach. The
    /// model chooses what to offer; this decides what is true.</para>
    ///
    /// <para>Separate from <c>ChatSystem</c> because it is pure decision-making over the
    /// world and nothing else: no conversation state, no UI, no async. That makes the rules
    /// testable without a chat session, which matters more here than anywhere else in the
    /// subsystem — this is the only code in the game that spends a player's money on the
    /// strength of a sentence they typed.</para>
    /// </summary>
    public static class ChatTradeBroker
    {
        /// <summary>
        /// Ceiling on a single proposed trade, whatever the model asks for.
        ///
        /// A misread "dame unos cuantos" that arrives as 500 would otherwise clear a purse
        /// and fill an inventory in one confirmation. The player can always ask again.
        /// </summary>
        public const int MAX_UNITS_PER_TRADE = 20;

        /// <summary>
        /// What <paramref name="proposal"/> would really mean, against this vendor and this
        /// player, right now.
        /// </summary>
        public static TradeQuote Quote(TradeProposal proposal, VendorNPC vendor,
                                       Inventory.Inventory inventory, CurrencyWallet wallet)
        {
            if (!proposal.IsSomething) return TradeQuote.No("");
            if (vendor == null) return TradeQuote.No(ChatLanguage.NoOneToTradeWith);

            var item = FindItem(vendor, proposal.ItemId);
            if (item == null)
                return TradeQuote.No(ChatLanguage.NotForSaleHere);

            int wanted = Mathf.Clamp(proposal.Quantity, 1, MAX_UNITS_PER_TRADE);

            return proposal.Intent == TradeIntent.Buy
                ? QuoteBuy(vendor, wallet, inventory, item, wanted)
                : QuoteSell(vendor, inventory, item, wanted);
        }

        private static TradeQuote QuoteBuy(VendorNPC vendor, CurrencyWallet wallet,
                                           Inventory.Inventory inventory, ItemDefinition item, int wanted)
        {
            int stock = StockOf(vendor, item);
            if (stock <= 0) return TradeQuote.No(ChatLanguage.SoldOut);

            if (inventory != null && inventory.IsFull)
                return TradeQuote.No(ChatLanguage.InventoryFull);

            int unitPrice = vendor.GetBuyPrice(item);
            if (unitPrice <= 0) unitPrice = 1;

            int coins = wallet != null ? wallet.Coins : 0;
            if (coins < unitPrice)
                return TradeQuote.No(ChatLanguage.CannotAfford(unitPrice, coins));

            // Cut the count down rather than refusing outright. Asking for three and being
            // told "you can have two" is a better answer than "no", and it is the answer a
            // shopkeeper would actually give.
            int affordable = coins / unitPrice;
            int quantity = Mathf.Min(wanted, Mathf.Min(stock, affordable));

            return TradeQuote.Ok(TradeIntent.Buy, item, quantity, unitPrice * quantity);
        }

        private static TradeQuote QuoteSell(VendorNPC vendor, Inventory.Inventory inventory,
                                            ItemDefinition item, int wanted)
        {
            if (inventory == null) return TradeQuote.No(ChatLanguage.CarryingNothing);

            int held = CountHeld(inventory, item);
            if (held <= 0) return TradeQuote.No(ChatLanguage.NotCarryingThat);

            int unitPrice = vendor.GetSellPrice(item);
            int quantity = Mathf.Min(wanted, held);

            return TradeQuote.Ok(TradeIntent.Sell, item, quantity, unitPrice * quantity);
        }

        /// <summary>
        /// Performs a quote, unit by unit, and reports how many actually went through.
        ///
        /// <para>Unit by unit because <c>TryBuyItem</c> and <c>TrySellItem</c> are the seams
        /// that own stock, coins, inventory space and the coin flourish — reimplementing a
        /// bulk version here would be a second copy of the rules that could disagree with
        /// the Buy button. A unit that fails stops the run: whatever changed underneath
        /// (stock gone, bag full) will not have un-changed by the next iteration.</para>
        ///
        /// <para>Returns the units completed, which can be fewer than quoted and is never
        /// assumed to be all of them. The player is told the real number.</para>
        /// </summary>
        public static int Execute(TradeQuote quote, VendorNPC vendor,
                                  Inventory.Inventory inventory, CurrencyWallet wallet)
        {
            if (!quote.IsValid || vendor == null || inventory == null || wallet == null) return 0;

            int done = 0;
            for (int i = 0; i < quote.Quantity; i++)
            {
                bool ok = quote.Intent == TradeIntent.Buy
                    ? vendor.TryBuyItem(quote.Item, inventory, wallet)
                    : vendor.TrySellItem(quote.Item, inventory, wallet);

                if (!ok) break;
                done++;
            }

            return done;
        }

        // ── Lookups ─────────────────────────────────────────────────────────

        /// <summary>
        /// The shop row matching <paramref name="itemId"/>, or null.
        ///
        /// Case-insensitive because the id is echoed back by a model that may have
        /// re-capitalised it, and an exact-case miss would read to the player as the shop
        /// denying it sells something it plainly does.
        /// </summary>
        private static ItemDefinition FindItem(VendorNPC vendor, string itemId)
        {
            foreach (var entry in vendor.ShopInventory)
            {
                if (entry.item == null) continue;
                if (string.Equals(entry.item.itemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                    return entry.item;
            }
            return null;
        }

        private static int StockOf(VendorNPC vendor, ItemDefinition item)
        {
            foreach (var entry in vendor.ShopInventory)
                if (entry.item == item) return entry.stock;
            return 0;
        }

        private static int CountHeld(Inventory.Inventory inventory, ItemDefinition item)
        {
            int held = 0;
            foreach (var slot in inventory.Slots)
                if (!slot.IsEmpty && slot.Item == item) held += slot.Quantity;
            return held;
        }
    }
}
