using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor tool to import vendor data from Python JSON files into Unity ScriptableObjects.
    /// Sources:
    ///   - python/data/vendors/registry/vendors.json → VendorConfigDefinition SOs
    ///   - python/data/vendors/economy/groups/*.json → EconomyGroupDefinition SOs
    ///
    /// Menu: Valkur > Vendors > Import Economy Groups / Import Vendor Registry
    /// </summary>
    public static class VendorDataImporter
    {
        private const string ECONOMY_GROUPS_PATH = "python/data/vendors/economy/groups";
        private const string VENDORS_REGISTRY_PATH = "python/data/vendors/registry/vendors.json";
        private const string ECONOMY_OUTPUT = "Assets/_Project/Data/Vendor/EconomyGroups";
        private const string VENDOR_OUTPUT = "Assets/_Project/Data/Vendor/Configs";

        [MenuItem("Valkur/Vendors/Import Economy Groups from Python JSON")]
        public static void ImportEconomyGroups()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string groupsDir = Path.Combine(projectRoot, ECONOMY_GROUPS_PATH);

            if (!Directory.Exists(groupsDir))
            {
                Debug.LogWarning($"[VendorDataImporter] Economy groups directory not found: {groupsDir}");
                return;
            }

            EnsureDirectory(ECONOMY_OUTPUT);

            int count = 0;
            foreach (string file in Directory.GetFiles(groupsDir, "*.json"))
            {
                string json = File.ReadAllText(file);
                string groupKey = Path.GetFileNameWithoutExtension(file);
                var so = ImportEconomyGroup(groupKey, json);
                if (so != null) count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VendorDataImporter] Imported {count} economy groups.");
        }

        [MenuItem("Valkur/Vendors/Import Vendor Registry from Python JSON")]
        public static void ImportVendorRegistry()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string registryFile = Path.Combine(projectRoot, VENDORS_REGISTRY_PATH);

            if (!File.Exists(registryFile))
            {
                Debug.LogWarning($"[VendorDataImporter] Vendor registry not found: {registryFile}");
                return;
            }

            EnsureDirectory(VENDOR_OUTPUT);

            string json = File.ReadAllText(registryFile);
            var root = ParseJsonDict(json);
            if (!root.TryGetValue("vendors", out object vendorsObj))
            {
                Debug.LogWarning("[VendorDataImporter] No 'vendors' key in registry JSON.");
                return;
            }

            int count = 0;
            if (vendorsObj is Dictionary<string, object> vendors)
            {
                foreach (var kvp in vendors)
                {
                    string vendorKey = kvp.Key;
                    if (kvp.Value is Dictionary<string, object> entry)
                    {
                        var so = ImportVendorConfig(vendorKey, entry);
                        if (so != null) count++;
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VendorDataImporter] Imported {count} vendor configs.");
        }

        [MenuItem("Valkur/Vendors/Copy Collision Data to StreamingAssets")]
        public static void CopyCollisionData()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));

            string[] sources =
            {
                "python/data/buildings/buildings_collisions_by_image.json",
                "python/data/worlds/base/buildings/buildings_collisions_by_building_instance_id.json",
                "python/data/worlds/base/buildings/buildings_collisions_by_spawn_id.json"
            };

            string destDir = Path.Combine(Application.streamingAssetsPath, "Buildings");
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            int count = 0;
            foreach (string srcRel in sources)
            {
                string srcFull = Path.Combine(projectRoot, srcRel);
                if (!File.Exists(srcFull))
                {
                    Debug.LogWarning($"[VendorDataImporter] Source not found: {srcFull}");
                    continue;
                }
                string destFile = Path.Combine(destDir, Path.GetFileName(srcRel));
                File.Copy(srcFull, destFile, true);
                count++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[VendorDataImporter] Copied {count} collision data files to StreamingAssets/Buildings/.");
        }

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

        /// <summary>
        /// Minimal JSON deserializer supporting nested dicts, lists, strings, numbers, bools, null.
        /// Based on Unity's MiniJSON pattern.
        /// </summary>
        private static class MiniJson
        {
            public static object Deserialize(string json)
            {
                if (string.IsNullOrEmpty(json)) return null;
                int index = 0;
                return ParseValue(json, ref index);
            }

            private static object ParseValue(string json, ref int index)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length) return null;
                char c = json[index];
                if (c == '{') return ParseObject(json, ref index);
                if (c == '[') return ParseArray(json, ref index);
                if (c == '"') return ParseString(json, ref index);
                if (c == 't' || c == 'f') return ParseBool(json, ref index);
                if (c == 'n') { index += 4; return null; }
                return ParseNumber(json, ref index);
            }

            private static Dictionary<string, object> ParseObject(string json, ref int index)
            {
                var dict = new Dictionary<string, object>();
                index++; // skip '{'
                while (index < json.Length)
                {
                    SkipWhitespace(json, ref index);
                    if (json[index] == '}') { index++; return dict; }
                    if (json[index] == ',') { index++; continue; }
                    string key = ParseString(json, ref index);
                    SkipWhitespace(json, ref index);
                    index++; // skip ':'
                    object value = ParseValue(json, ref index);
                    dict[key] = value;
                }
                return dict;
            }

            private static List<object> ParseArray(string json, ref int index)
            {
                var list = new List<object>();
                index++; // skip '['
                while (index < json.Length)
                {
                    SkipWhitespace(json, ref index);
                    if (json[index] == ']') { index++; return list; }
                    if (json[index] == ',') { index++; continue; }
                    list.Add(ParseValue(json, ref index));
                }
                return list;
            }

            private static string ParseString(string json, ref int index)
            {
                index++; // skip opening '"'
                var sb = new System.Text.StringBuilder();
                while (index < json.Length)
                {
                    char c = json[index++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\' && index < json.Length)
                    {
                        char next = json[index++];
                        switch (next)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (index + 4 <= json.Length)
                                {
                                    string hex = json.Substring(index, 4);
                                    sb.Append((char)System.Convert.ToInt32(hex, 16));
                                    index += 4;
                                }
                                break;
                            default: sb.Append(next); break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            private static object ParseNumber(string json, ref int index)
            {
                int start = index;
                while (index < json.Length && "0123456789.eE+-".IndexOf(json[index]) >= 0) index++;
                string num = json.Substring(start, index - start);
                if (num.Contains(".") || num.Contains("e") || num.Contains("E"))
                {
                    if (double.TryParse(num, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double d))
                        return d;
                }
                else
                {
                    if (long.TryParse(num, out long l)) return l;
                }
                return 0;
            }

            private static bool ParseBool(string json, ref int index)
            {
                if (json[index] == 't') { index += 4; return true; }
                index += 5;
                return false;
            }

            private static void SkipWhitespace(string json, ref int index)
            {
                while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            }
        }
    }
}
