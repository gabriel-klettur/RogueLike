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
    ///   5. Persona negotiation discount
    ///   6. Floor at MIN_PRICE (1g)
    ///
    /// Maps to Python: PriceService + EconomyService + PersonaService combined.
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
                    return ApplyDiscount(overrideBuy, negotiationDiscount);
            }

            // Step 2-3: Global price with economy group margin
            float basePrice = item.buyPrice > 0 ? item.buyPrice : GetFallbackPrice(item);
            float margin = GetBuyMargin(config.economyGroup, item.itemId);
            int price = Mathf.RoundToInt(basePrice * margin);

            // Step 5: Negotiation discount
            price = ApplyDiscount(price, negotiationDiscount);

            // Step 6: Floor
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
                    return ApplyDiscount(overrideSell, negotiationDiscount);
            }

            // Step 2-3: Global price with economy group margin
            float basePrice = item.sellPrice > 0 ? item.sellPrice
                            : (item.buyPrice > 0 ? item.buyPrice : GetFallbackPrice(item));
            float margin = GetSellMargin(config.economyGroup, item.itemId);
            int price = Mathf.RoundToInt(basePrice * margin);

            // Step 5: Negotiation discount
            price = ApplyDiscount(price, negotiationDiscount);

            // Step 6: Floor
            return Mathf.Max(price, MIN_PRICE);
        }

        /// <summary>Check if an item is allowed for trade at this vendor.</summary>
        public bool IsAllowed(VendorConfigDefinition config, ItemDefinition item)
        {
            if (config == null || config.economyGroup == null || item == null) return true;
            return config.economyGroup.IsAllowed(item.itemId, item.effect);
        }

        private float GetBuyMargin(EconomyGroupDefinition group, string itemKey)
        {
            if (group == null) return 1f;
            var margin = group.GetMargin(itemKey);
            return margin.buyMultiplier;
        }

        private float GetSellMargin(EconomyGroupDefinition group, string itemKey)
        {
            if (group == null) return 1f;
            var margin = group.GetMargin(itemKey);
            return margin.sellMultiplier;
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
    }
}
