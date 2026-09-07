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

        [Tooltip("Per-item margin overrides. Beat everything else.")]
        public List<ItemMarginEntry> itemMargins = new List<ItemMarginEntry>();

        [Tooltip("Per-itemType margins, consulted when no per-item override matches. This is " +
                 "the layer that lets a vendor be a SPECIALIST without listing every id they " +
                 "trade well: the catalogue has six types (lumberjack, food, mineral, " +
                 "blacksmith, alchemy, magic) and 64 minerals, so 'the smith pays fairly for " +
                 "ore' is one row here and 64 rows in itemMargins.")]
        public List<TypeMarginEntry> typeMargins = new List<TypeMarginEntry>();

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

        [Serializable]
        public struct TypeMarginEntry
        {
            public string itemType;
            public MarginEntry margin;
        }

        /// <summary>
        /// The margin pair for one item, most specific first: the per-item override, then
        /// the per-type row, then the group default.
        ///
        /// <para><paramref name="itemType"/> is optional so the pre-existing single-argument
        /// call shape keeps compiling and keeps meaning what it meant; passing it is what
        /// makes a specialist a specialist.</para>
        /// </summary>
        public MarginEntry GetMargin(string itemKey, string itemType = null)
        {
            foreach (var im in itemMargins)
            {
                if (im.itemKey == itemKey) return im.margin;
            }
            if (!string.IsNullOrEmpty(itemType))
            {
                foreach (var tm in typeMargins)
                {
                    if (tm.itemType == itemType) return tm.margin;
                }
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
