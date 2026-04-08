using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class VendorDataImporter
    {
        // ------------------------------------------------------------------
        // Economy Group Import
        // ------------------------------------------------------------------

        private static EconomyGroupDefinition ImportEconomyGroup(string groupKey, string json)
        {
            string assetPath = $"{ECONOMY_OUTPUT}/{groupKey}.asset";
            var so = AssetDatabase.LoadAssetAtPath<EconomyGroupDefinition>(assetPath);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<EconomyGroupDefinition>();
                AssetDatabase.CreateAsset(so, assetPath);
            }

            so.groupKey = groupKey;

            var dict = ParseJsonDict(json);

            // Whitelist
            if (dict.TryGetValue("whitelist", out object wl) && wl is List<object> wlList)
            {
                so.whitelist = new List<string>();
                foreach (var item in wlList)
                    so.whitelist.Add(item.ToString());
            }

            // Blacklist
            if (dict.TryGetValue("blacklist", out object bl) && bl is List<object> blList)
            {
                so.blacklist = new List<string>();
                foreach (var item in blList)
                    so.blacklist.Add(item.ToString());
            }

            // Allowed types
            if (dict.TryGetValue("allowed_types", out object at) && at is List<object> atList)
            {
                so.allowedTypes = new List<string>();
                foreach (var item in atList)
                    so.allowedTypes.Add(item.ToString());
            }

            // Margins
            if (dict.TryGetValue("margins", out object mg) && mg is Dictionary<string, object> margins)
            {
                // Default margin
                if (margins.TryGetValue("default", out object defObj) && defObj is Dictionary<string, object> defDict)
                {
                    so.defaultMargin = ParseMarginEntry(defDict);
                }

                // Per-item margins
                if (margins.TryGetValue("items", out object itemsObj) && itemsObj is Dictionary<string, object> items)
                {
                    so.itemMargins = new List<EconomyGroupDefinition.ItemMarginEntry>();
                    foreach (var kvp in items)
                    {
                        if (kvp.Value is Dictionary<string, object> itemDict)
                        {
                            var margin = ParseMarginEntry(itemDict);
                            so.itemMargins.Add(new EconomyGroupDefinition.ItemMarginEntry
                            {
                                itemKey = kvp.Key,
                                margin = margin
                            });
                        }
                    }
                }
            }

            EditorUtility.SetDirty(so);
            return so;
        }

        private static EconomyGroupDefinition.MarginEntry ParseMarginEntry(Dictionary<string, object> dict)
        {
            var entry = new EconomyGroupDefinition.MarginEntry();
            if (dict.TryGetValue("buy", out object buy))
                entry.buyMultiplier = ParseFloat(buy);
            if (dict.TryGetValue("sell", out object sell))
                entry.sellMultiplier = ParseFloat(sell);
            return entry;
        }

        // ------------------------------------------------------------------
        // Vendor Config Import
        // ------------------------------------------------------------------

        private static VendorConfigDefinition ImportVendorConfig(string vendorKey, Dictionary<string, object> entry)
        {
            string assetPath = $"{VENDOR_OUTPUT}/{vendorKey}.asset";
            var so = AssetDatabase.LoadAssetAtPath<VendorConfigDefinition>(assetPath);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<VendorConfigDefinition>();
                AssetDatabase.CreateAsset(so, assetPath);
            }

            so.vendorKey = vendorKey;

            // Link economy group
            if (entry.TryGetValue("economy_group", out object egObj))
            {
                string egKey = egObj.ToString();
                string egPath = $"{ECONOMY_OUTPUT}/{egKey}.asset";
                so.economyGroup = AssetDatabase.LoadAssetAtPath<EconomyGroupDefinition>(egPath);
            }

            // Price overrides
            if (entry.TryGetValue("prices_override", out object poObj) && poObj is Dictionary<string, object> overrides)
            {
                so.priceOverrides = new List<VendorConfigDefinition.PriceOverrideEntry>();
                foreach (var kvp in overrides)
                {
                    if (kvp.Value is Dictionary<string, object> priceDict)
                    {
                        var po = new VendorConfigDefinition.PriceOverrideEntry { itemKey = kvp.Key };
                        if (priceDict.TryGetValue("buy", out object b))
                            po.buyPrice = ParseInt(b);
                        if (priceDict.TryGetValue("sell", out object s))
                            po.sellPrice = ParseInt(s);
                        so.priceOverrides.Add(po);
                    }
                }
            }

            EditorUtility.SetDirty(so);
            return so;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static void EnsureDirectory(string assetPath)
        {
            string fullPath = Path.Combine(Application.dataPath, assetPath.Replace("Assets/", ""));
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }

        private static float ParseFloat(object obj)
        {
            if (obj is double d) return (float)d;
            if (obj is long l) return l;
            if (float.TryParse(obj.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float f))
                return f;
            return 1f;
        }

        private static int ParseInt(object obj)
        {
            if (obj is long l) return (int)l;
            if (obj is double d) return (int)d;
            if (int.TryParse(obj.ToString(), out int i)) return i;
            return 0;
        }

        /// <summary>
        /// Minimal recursive JSON parser for dict/list structures.
        /// Unity's JsonUtility doesn't handle dict-of-dicts so we parse manually.
        /// </summary>
        private static Dictionary<string, object> ParseJsonDict(string json)
        {
            var result = new Dictionary<string, object>();
            try
            {
                result = (Dictionary<string, object>)MiniJson.Deserialize(json);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VendorDataImporter] JSON parse error: {ex.Message}");
            }
            return result ?? new Dictionary<string, object>();
        }
    }
}
