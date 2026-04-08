using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining an economy group with margins, whitelist/blacklist.
    /// Maps to Python's data/vendors/economy/groups/{group}.json.
    /// </summary>
    [CreateAssetMenu(fileName = "NewEconomyGroup", menuName = "Valkur/Vendor/Economy Group")]
    public class EconomyGroupDefinition : ScriptableObject
    {
        [Tooltip("Economy group key (e.g. vendor_cheff, vendor_alchemist).")]
        public string groupKey;

        [Tooltip("Allowed item IDs. Empty = no whitelist filtering.")]
        public List<string> whitelist = new List<string>();

        [Tooltip("Blocked item IDs. Take priority over whitelist.")]
        public List<string> blacklist = new List<string>();

        [Tooltip("Allowed item types for this group (e.g. 'food'). Empty = all types.")]
        public List<string> allowedTypes = new List<string>();

        [Header("Margins")]
        [Tooltip("Default buy/sell margins applied to all items.")]
        public MarginEntry defaultMargin = new MarginEntry { buyMultiplier = 1f, sellMultiplier = 1f };

        [Tooltip("Per-item margin overrides.")]
        public List<ItemMarginEntry> itemMargins = new List<ItemMarginEntry>();

        [Serializable]
        public struct MarginEntry
        {
            [Range(0.1f, 3f)] public float buyMultiplier;
            [Range(0.1f, 3f)] public float sellMultiplier;
        }

        [Serializable]
        public struct ItemMarginEntry
        {
            public string itemKey;
            public MarginEntry margin;
        }

        public MarginEntry GetMargin(string itemKey)
        {
            foreach (var im in itemMargins)
            {
                if (im.itemKey == itemKey) return im.margin;
            }
            return defaultMargin;
        }

        public bool IsAllowed(string itemKey, string itemType, string operation = null)
        {
            if (blacklist.Contains(itemKey)) return false;
            if (whitelist.Count > 0 && !whitelist.Contains(itemKey)) return false;
            if (allowedTypes.Count > 0 && !string.IsNullOrEmpty(itemType) && !allowedTypes.Contains(itemType))
                return false;
            return true;
        }
    }
}
