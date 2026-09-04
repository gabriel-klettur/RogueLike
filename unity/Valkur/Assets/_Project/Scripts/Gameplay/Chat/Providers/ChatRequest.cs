using System.Collections.Generic;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.NPC;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>One line of a vendor's counter, as the character knows it.</summary>
    public readonly struct TradeStockLine
    {
        public string ItemId { get; }
        public string DisplayName { get; }
        public int Price { get; }
        public int Stock { get; }

        public TradeStockLine(string itemId, string displayName, int price, int stock)
        {
            ItemId = itemId;
            DisplayName = displayName;
            Price = price;
            Stock = stock;
        }
    }

    /// <summary>
    /// What the game knows about buying and selling with the character being talked to.
    ///
    /// <para>This is the ONLY channel through which shop facts reach a language model, and
    /// every fact in it is one the GAME owns: the coins in the player's purse, and the
    /// counter exactly as the counter has it. Prices come from the same
    /// <see cref="VendorNPC.GetBuyPrice"/> the Buy button charges, so what the character
    /// says and what the shop does cannot disagree.</para>
    ///
    /// <para>Default (<see cref="IsVendor"/> false) is the correct state for everyone who
    /// does not trade, and for a conversation opened before the shop exists.</para>
    /// </summary>
    public readonly struct ChatTradeContext
    {
        /// <summary>Whether this character sells anything at all.</summary>
        public bool IsVendor { get; }

        /// <summary>Coins the player is carrying right now.</summary>
        public int PlayerCoins { get; }

        /// <summary>Number of distinct items on the counter with stock left.</summary>
        public int StockCount { get; }

        /// <summary>How many of those the player could pay for. Zero is a meaningful answer.</summary>
        public int AffordableCount { get; }

        /// <summary>Price of the cheapest thing in stock, or 0 when there is nothing to sell.</summary>
        public int CheapestPrice { get; }

        /// <summary>
        /// Everything on the counter, by name and price.
        ///
        /// <para>This is sent to the model, and withholding it was a mistake I made
        /// deliberately: the reasoning was that "a model handed an inventory writes prices
        /// for it". Half true, and it produced something far worse — with NO inventory the
        /// model invents the whole thing. Measured in a shipped conversation, Gatita offered
        /// "manzanas jugosas, peras tiernas o ciruelas dulces… arándanos pequeños y moras",
        /// not one of which exists in the catalogue, and then said "aquí tienes, dos
        /// manzanas" for a sale that never happened.</para>
        ///
        /// <para>With the real list she can only name real things at the real prices, and a
        /// player who asks "¿cuánto cuestan?" gets an answer instead of a deflection.</para>
        /// </summary>
        public IReadOnlyList<TradeStockLine> Stock { get; }

        public ChatTradeContext(bool isVendor, int playerCoins, int stockCount,
                                int affordableCount, int cheapestPrice,
                                IReadOnlyList<TradeStockLine> stock = null)
        {
            IsVendor = isVendor;
            PlayerCoins = playerCoins;
            StockCount = stockCount;
            AffordableCount = affordableCount;
            CheapestPrice = cheapestPrice;
            Stock = stock;
        }

        /// <summary>
        /// Reads the live shop and the live purse.
        ///
        /// Prices come from <see cref="VendorNPC.GetBuyPrice"/> — the same call the shop UI
        /// makes — so what the NPC says about affording something and what the counter
        /// charges for it cannot disagree.
        /// </summary>
        public static ChatTradeContext FromLive(VendorNPC vendor, CurrencyWallet wallet)
        {
            int coins = wallet != null ? wallet.Coins : 0;
            if (vendor == null) return new ChatTradeContext(false, coins, 0, 0, 0);

            int stock = 0, affordable = 0, cheapest = int.MaxValue;
            var lines = new List<TradeStockLine>();

            foreach (var entry in vendor.ShopInventory)
            {
                if (entry.item == null || entry.stock <= 0) continue;

                stock++;
                int price = vendor.GetBuyPrice(entry.item);
                if (price < cheapest) cheapest = price;
                if (price <= coins) affordable++;

                lines.Add(new TradeStockLine(
                    entry.item.itemId,
                    string.IsNullOrWhiteSpace(entry.item.displayName) ? entry.item.itemId : entry.item.displayName,
                    price,
                    entry.stock));
            }

            return new ChatTradeContext(
                isVendor: true,
                playerCoins: coins,
                stockCount: stock,
                affordableCount: affordable,
                cheapestPrice: stock > 0 ? cheapest : 0,
                stock: lines);
        }
    }

    /// <summary>
    /// Everything a provider needs to answer one line of player chat.
    ///
    /// <para>A struct rather than four parameters because the list was already four long and
    /// growing: the trade context is the fifth thing a reply depends on, and the sixth —
    /// quests, time of day, whatever comes next — should not be another signature change
    /// rippling through every implementor and every test fake.</para>
    /// </summary>
    public readonly struct ChatRequest
    {
        public NPCPersonaDefinition Persona { get; }
        public NPCMemory Memory { get; }

        /// <summary>What the player just typed.</summary>
        public string PlayerText { get; }

        /// <summary>Shop and purse facts. Default for a character who does not trade.</summary>
        public ChatTradeContext Trade { get; }

        /// <summary>
        /// What the world is doing — the hour and the weather — for a character whose mood
        /// can be moved by it.
        ///
        /// <para>Passed IN rather than read from the live singletons inside the provider,
        /// exactly as <see cref="Trade"/> is. A provider that reaches for
        /// <c>DayNightCycle.Instance</c> itself cannot be asked what it would say at
        /// midnight without a scene that is actually at midnight, so the one branch that
        /// only fires in the small hours is the one branch no test can reach.</para>
        ///
        /// <para>The default is a clear afternoon with no cycle at all, which suggests no
        /// face — so a caller that knows nothing about the world silently changes nothing,
        /// which is the correct behaviour for every existing test fake.</para>
        /// </summary>
        public ChatMoodContext Mood { get; }

        public ChatRequest(NPCPersonaDefinition persona, NPCMemory memory,
                           string playerText, ChatTradeContext trade = default,
                           ChatMoodContext mood = default)
        {
            Persona = persona;
            Memory = memory;
            PlayerText = playerText;
            Trade = trade;
            Mood = mood;
        }
    }
}
