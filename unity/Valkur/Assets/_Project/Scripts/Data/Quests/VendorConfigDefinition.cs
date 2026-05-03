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
