using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.NPC
{
    /// <summary>
    /// Runtime service that resolves buy/sell prices for vendor transactions.
    /// Implements the Python 7-step price pipeline:
    ///   1. Per-entity price override (VendorConfigDefinition.priceOverrides)
    ///   2. EconomyGroup margins
    ///   3. Global price from ItemDefinition.buyPrice / sellPrice
    ///   4. Fallback heuristic (stackable=1g, non-stackable=10g)
    ///   5. The economic cycle (MarketService) — boom and bust, one factor for everyone
    ///   6. Persona negotiation discount
    ///   7. Floor at MIN_PRICE (1g)
    ///
    /// Maps to Python: PriceService + EconomyService + PersonaService combined.
    ///
    /// <para><b>The cycle is applied AFTER the margins and BEFORE the discount, and that
    /// order is the design.</b> After the margins, because a boom should lift a specialist's
    /// fair price and a generalist's mark-up in the same proportion — folding it into the
    /// margin would make the cycle bite hardest exactly where the vendor was already dear.
    /// Before the discount, because haggling is the LAST word: a character who offers 20 %
    /// off is offering it on what the thing costs TODAY, not on a pre-cycle number the player
    /// never sees.</para>
    ///
    /// <para><b>A per-vendor price override skips the margins and the fallback but NOT the
    /// cycle.</b> An override says "this vendor charges this for this thing" — a statement
    /// about relative worth, not about immunity to the economy. Leaving it outside would make
    /// the overridden items the only stable prices in the world and hand the player an
    /// arbitrage against the cycle itself.</para>
    /// </summary>
    public class VendorEconomyService : SingletonMonoBehaviour<VendorEconomyService>
    {
        private const int STACKABLE_FALLBACK = 1;
        private const int NON_STACKABLE_FALLBACK = 10;
        private const int MIN_PRICE = 1;
        private const float MAX_DISCOUNT_CAP = 0.9f;

        /// <summary>Resolve the buy price (player pays to buy from vendor).</summary>
        public int GetBuyPrice(VendorConfigDefinition config, ItemDefinition item, float negotiationDiscount = 0f)
        {
            if (config == null || item == null) return MIN_PRICE;

            // Step 1: Per-vendor price override
            if (config.TryGetPriceOverride(item.itemId, out int overrideBuy, out _))
            {
                if (overrideBuy > 0)
                    return ApplyDiscount(ApplyCycle(overrideBuy, MarketBuyMultiplier), negotiationDiscount);
            }

            // Step 2-3: Global price with economy group margin
            float basePrice = item.buyPrice > 0 ? item.buyPrice : GetFallbackPrice(item);
            float margin = GetBuyMargin(config.economyGroup, item.itemId, item.itemType);
            int price = Mathf.RoundToInt(basePrice * margin);

            // Step 5: The economic cycle
            price = ApplyCycle(price, MarketBuyMultiplier);

            // Step 6: Negotiation discount
            price = ApplyDiscount(price, negotiationDiscount);

            // Step 7: Floor
            return Mathf.Max(price, MIN_PRICE);
        }

        /// <summary>Resolve the sell price (player receives when selling to vendor).</summary>
        public int GetSellPrice(VendorConfigDefinition config, ItemDefinition item, float negotiationDiscount = 0f)
        {
            if (config == null || item == null) return MIN_PRICE;

            // Step 1: Per-vendor price override
            if (config.TryGetPriceOverride(item.itemId, out _, out int overrideSell))
            {
                if (overrideSell > 0)
                    return ApplyPremium(ApplyCycle(overrideSell, MarketSellMultiplier), negotiationDiscount);
            }

            // Step 2-3: Global price with economy group margin
            float basePrice = item.sellPrice > 0 ? item.sellPrice
                            : (item.buyPrice > 0 ? item.buyPrice : GetFallbackPrice(item));
            float margin = GetSellMargin(config.economyGroup, item.itemId, item.itemType);
            int price = Mathf.RoundToInt(basePrice * margin);

            // Step 5: The economic cycle
            price = ApplyCycle(price, MarketSellMultiplier);

            // Step 6: Negotiation, in the player's favour — a PREMIUM here, not a discount
            price = ApplyPremium(price, negotiationDiscount);

            // Step 7: Floor
            return Mathf.Max(price, MIN_PRICE);
        }

        /// <summary>Check if an item is allowed for trade at this vendor.</summary>
        public bool IsAllowed(VendorConfigDefinition config, ItemDefinition item)
        {
            if (config == null || config.economyGroup == null || item == null) return true;
            return config.economyGroup.IsAllowed(item.itemId, item.effect);
        }

        private float GetBuyMargin(EconomyGroupDefinition group, string itemKey, string itemType)
        {
            if (group == null) return 1f;
            var margin = group.GetMargin(itemKey, itemType);
            return margin.buyMultiplier;
        }

        private float GetSellMargin(EconomyGroupDefinition group, string itemKey, string itemType)
        {
            if (group == null) return 1f;
            var margin = group.GetMargin(itemKey, itemType);
            return margin.sellMultiplier;
        }

        /// <summary>
        /// What the cycle is doing to a purchase right now, or 1 when no market is running.
        ///
        /// <para>Null-safe rather than required: every EditMode price test, and any scene that
        /// has not built the service yet, must resolve prices exactly as they did before this
        /// layer existed. A pricing pipeline that throws or shifts when an optional service is
        /// absent is one nobody can test in isolation.</para>
        /// </summary>
        private static float MarketBuyMultiplier =>
            MarketService.HasInstance ? MarketService.Instance.BuyMultiplier : 1f;

        /// <summary>What the cycle is doing to a sale right now, or 1 with no market.</summary>
        private static float MarketSellMultiplier =>
            MarketService.HasInstance ? MarketService.Instance.SellMultiplier : 1f;

        /// <summary>
        /// Applies a cycle multiplier and floors the result, so a Trough can never round a
        /// cheap item down to nothing and hand it over free.
        /// </summary>
        private static int ApplyCycle(int price, float multiplier)
        {
            if (Mathf.Approximately(multiplier, 1f)) return price;
            return Mathf.Max(Mathf.RoundToInt(price * multiplier), MIN_PRICE);
        }

        private int GetFallbackPrice(ItemDefinition item)
        {
            return item.stackable ? STACKABLE_FALLBACK : NON_STACKABLE_FALLBACK;
        }

        private int ApplyDiscount(int price, float discount)
        {
            discount = Mathf.Clamp(discount, 0f, MAX_DISCOUNT_CAP);
            return Mathf.Max(Mathf.RoundToInt(price * (1f - discount)), MIN_PRICE);
        }

        /// <summary>
        /// The sell-side twin of <see cref="ApplyDiscount"/>: the same negotiation strength,
        /// pointed the same way — at the player's benefit.
        ///
        /// <para><b>The shipped code applied the DISCOUNT to both sides</b>, so a persona
        /// willing to come down 20 % also paid 20 % LESS for what the player brought in. The
        /// friendliest character in the world was the worst one to sell to, and nothing said
        /// so. It went unnoticed only because no caller ever passed a non-zero discount — the
        /// bug and the field's inertness were hiding each other.</para>
        ///
        /// <para>Capped by the same <see cref="MAX_DISCOUNT_CAP"/>, which matters more here:
        /// buying is bounded below by the price floor, while a sell premium is bounded by
        /// nothing at all, and an uncapped one on an item the player can farm is a printing
        /// press.</para>
        /// </summary>
        private int ApplyPremium(int price, float strength)
        {
            strength = Mathf.Clamp(strength, 0f, MAX_DISCOUNT_CAP);
            return Mathf.Max(Mathf.RoundToInt(price * (1f + strength)), MIN_PRICE);
        }
    }
}
