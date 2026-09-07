using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining a vendor's configuration: economy group, inventory seed, price overrides.
    /// Maps to Python's data/vendors/registry/vendors.json entries.
    /// </summary>
    [CreateAssetMenu(fileName = "NewVendorConfig", menuName = "Valkur/Vendor/Vendor Config")]
    public class VendorConfigDefinition : ScriptableObject
    {
        [Tooltip("Vendor key matching Python vendor_id (e.g. 'gatita').")]
        public string vendorKey;

        [Tooltip("Economy group controlling margins, whitelist/blacklist.")]
        public EconomyGroupDefinition economyGroup;

        [Tooltip("Chat persona for this vendor.")]
        public NPCPersonaDefinition persona;

        [Header("Inventory Seed")]
        [Tooltip("Starting inventory for this vendor.")]
        public List<SeedSlot> inventorySeed = new List<SeedSlot>();

        [Header("Purse")]
        [Tooltip("Coins this vendor has to buy WITH. 0 is an unlimited purse and is the " +
                 "historical behaviour — which is what it has to mean, because a key absent " +
                 "from an already-shipped .asset deserialises to 0, and reading that as an " +
                 "EMPTY purse would make every existing vendor refuse to buy anything the " +
                 "moment this field was added. A positive value is a real float that drains " +
                 "as the player sells and refills as the player buys.")]
        [Min(0)] public int coinFloat;

        [Tooltip("Seconds for a vendor to recover a full purse and a full shelf from empty. " +
                 "Both refill on this one clock and PROPORTIONALLY, so a vendor half-emptied " +
                 "is whole again in half the time — a fixed per-tick amount would make a big " +
                 "shop and a small one recover at wildly different rates. 0 keeps the " +
                 "historical behaviour: stock never returns and the purse never refills.")]
        [Min(0f)] public float restockSeconds;

        [Header("Price Overrides")]
        [Tooltip("Per-item price overrides that take priority over global prices.")]
        public List<PriceOverrideEntry> priceOverrides = new List<PriceOverrideEntry>();

        [System.Serializable]
        public struct SeedSlot
        {
            public ItemDefinition item;
            public int quantity;
        }

        [System.Serializable]
        public struct PriceOverrideEntry
        {
            public string itemKey;
            public int buyPrice;
            public int sellPrice;
        }

        public bool TryGetPriceOverride(string itemKey, out int buy, out int sell)
        {
            foreach (var po in priceOverrides)
            {
                if (po.itemKey == itemKey)
                {
                    buy = po.buyPrice;
                    sell = po.sellPrice;
                    return true;
                }
            }
            buy = 0;
            sell = 0;
            return false;
        }
    }
}
