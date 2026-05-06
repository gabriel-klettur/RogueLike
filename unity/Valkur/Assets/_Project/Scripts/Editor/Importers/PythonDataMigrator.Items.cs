using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Items importer: reads <c>python/data/items/items_export.json</c> (produced by
    /// <c>python/scripts/export_items_to_json.py</c>) and creates one
    /// <see cref="ItemDefinition"/> ScriptableObject per row, plus a single
    /// <see cref="ItemCatalog"/> singleton that indexes them all.
    ///
    /// Why JSON and not direct SQLite reads? The Unity assemblies stay free of any
    /// native SQLite dependency, the export is diff-friendly in git, and re-running
    /// the Python exporter after any DB tweak yields a deterministic snapshot.
    ///
    /// The import is idempotent: re-running upserts each row by <c>itemId</c>, so
    /// existing references in scenes / prefabs survive.
    /// </summary>
    public static partial class PythonDataMigrator
    {
        private const string ITEMS_EXPORT_REL = "items/items_export.json";
        private const string ITEMS_OUTPUT_DIR = "Assets/_Project/Data/Catalogs/Items";
        private const string ITEMS_CATALOG_PATH = ITEMS_OUTPUT_DIR + "/ItemCatalog.asset";

        [MenuItem("Valkur/Migration/Import Items from Python SQLite")]
        public static void ImportItems() => ImportItems(dryRun: false);

        [MenuItem("Valkur/Migration/Dry-Run Items (Validate Only)")]
        public static void DryRunItems() => ImportItems(dryRun: true);

        public static MigrationReport ImportItems(bool dryRun)
        {
            var report = new MigrationReport();
            const string source = "items_export.json";

            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_DATA_ROOT, ITEMS_EXPORT_REL));

            if (!File.Exists(jsonPath))
            {
                report.AddError(source, "-",
                    $"Export not found at '{jsonPath}'. Run python/scripts/export_items_to_json.py first.");
                report.PrintToConsole($"Items ({(dryRun ? "DRY-RUN" : "IMPORT")})");
                return report;
            }

            string json = File.ReadAllText(jsonPath);
            ImportItemsManual(json, dryRun, report);
            report.PrintToConsole($"Items ({(dryRun ? "DRY-RUN" : "IMPORT")})");
            return report;
        }

        private static void ImportItemsManual(string json, bool dryRun, MigrationReport report)
        {
            const string source = "items_export.json";

            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                report.AddError(source, "-", "Failed to parse JSON root.");
                return;
            }

            var rows = parsed.GetValueOrDefault("items") as List<object>;
            if (rows == null)
            {
                report.AddError(source, "-", "Missing 'items' array.");
                return;
            }

            if (!dryRun)
                EnsureFolder(ITEMS_OUTPUT_DIR);

            ItemCatalog catalog = dryRun ? null : LoadOrCreateCatalog();

            int created = 0, updated = 0, skipped = 0;

            foreach (var rowObj in rows)
            {
                var row = rowObj as Dictionary<string, object>;
                if (row == null)
                {
                    report.AddError(source, "-", "Row is not an object.");
                    continue;
                }

                string id = row.GetValueOrDefault("id") as string;
                if (string.IsNullOrWhiteSpace(id))
                {
                    report.AddError(source, "(empty)", "Row missing 'id'.");
                    continue;
                }

                if (dryRun)
                {
                    report.AddOk(source, id, "Validated (dry-run).");
                    continue;
                }

                // Items are now organized in category subfolders (Equipment,
                // Consumable, Material, Quest, Other). Recursive lookup avoids
                // creating duplicates when an existing asset has been moved
                // into one of those subfolders.
                var existing = FindExistingItemAsset(id);
                var so = existing != null
                    ? existing
                    : ScriptableObject.CreateInstance<ItemDefinition>();

                ApplyRowToDefinition(row, so, source, id, report);

                if (existing == null)
                {
                    // New asset: place it in the subfolder matching its
                    // derived category so the on-disk layout stays organised.
                    string subDir = SubfolderForCategory(so.GetCategory());
                    EnsureFolder(subDir);
                    string assetPath = $"{subDir}/{id}.asset";
                    AssetDatabase.CreateAsset(so, assetPath);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(so);
                    updated++;
                }

                if (catalog != null)
                    catalog.Upsert(so);

                report.AddOk(source, id,
                    existing == null ? "Created." : "Updated.");
            }

            if (dryRun) return;

            if (catalog != null)
            {
                catalog.Compact();
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ItemsMigrator] Created {created}, updated {updated}, skipped {skipped}. " +
                      $"Catalog at {ITEMS_CATALOG_PATH} ({catalog?.Count ?? 0} entries).");
        }

        private static void ApplyRowToDefinition(
            Dictionary<string, object> row,
            ItemDefinition so,
            string source,
            string id,
            MigrationReport report)
        {
            so.itemId          = id;
            so.displayName     = row.GetValueOrDefault("name") as string ?? id;
            so.description     = row.GetValueOrDefault("description") as string ?? "";
            so.itemType        = row.GetValueOrDefault("type") as string ?? "";

            so.stackable       = GetBoolNullable(row, "stackable", defaultValue: false);
            so.maxStack        = GetIntNullable(row, "max_stack", defaultValue: 1);
            if (so.maxStack < 1) so.maxStack = 1;

            so.equipSlot       = ParseEquipSlot(row.GetValueOrDefault("equip_slot") as string);
            so.damage          = GetIntNullable(row, "damage", 0);
            so.attackSpeed     = GetFloatNullable(row, "attack_speed", 0f);
            so.range           = GetIntNullable(row, "range", 0);
            so.critChance      = GetFloatNullable(row, "crit_chance", 0f);
            so.critMultiplier  = GetFloatNullable(row, "crit_multiplier", 1f);
            so.durability      = GetIntNullable(row, "durability", 0);

            so.value           = GetIntNullable(row, "value", 0);
            so.buyPrice        = GetIntNullable(row, "buy_price", 0);
            so.sellPrice       = GetIntNullable(row, "sell_price", 0);
            so.rarity          = ParseRarity(row.GetValueOrDefault("rarity") as string);
            so.levelRequirement= GetIntNullable(row, "level_requirement", 1);
            if (so.levelRequirement < 1) so.levelRequirement = 1;
            so.weight          = GetFloatNullable(row, "weight", 0f);

            so.threshold       = GetIntNullable(row, "threshold", 0);
            so.experience      = GetIntNullable(row, "experience", 0);

            so.effect          = row.GetValueOrDefault("effect") as string ?? "";
            so.questId         = row.GetValueOrDefault("quest_id") as string ?? "";

            so.scaleEditor     = GetFloatNullable(row, "scale_editor", 1f);
            so.scaleMap        = GetFloatNullable(row, "scale_map", 1f);
            so.scaleInventory  = GetFloatNullable(row, "scale_inventory", 1f);
            so.zLayer          = GetIntNullable(row, "z_layer", 0);
            so.despawnTime     = GetFloatNullable(row, "despawn_time", 0f);

            // Icons: small / large / first frame from icon_json (multi-frame).
            string iconSmall = row.GetValueOrDefault("icon_small") as string;
            string iconLarge = row.GetValueOrDefault("icon_large") as string;
            string iconFromArray = ExtractFirstIconFromArray(row.GetValueOrDefault("icon_json"));

            so.iconSmall = ResolveItemIconSprite(iconSmall, report, source, id);
            so.iconLarge = ResolveItemIconSprite(iconLarge, report, source, id);

            // Primary icon falls back through small -> array[0] -> large so the
            // picker grid always has *something* to display when one is missing.
            so.icon = so.iconSmall
                   ?? ResolveItemIconSprite(iconFromArray, report, source, id)
                   ?? so.iconLarge;
        }

        private static string ExtractFirstIconFromArray(object iconJson)
        {
            if (iconJson is List<object> list && list.Count > 0)
                return list[0] as string;
            return null;
        }

        /// <summary>
        /// Convert a Python item asset path like <c>assets/items/Alchemy/health_potion.png</c>
        /// to a Unity Sprite at <c>Assets/_Project/Art/Items/Alchemy/health_potion.png</c>.
        /// Returns null and logs a warning when the sprite cannot be resolved.
        /// </summary>
        private static Sprite ResolveItemIconSprite(
            string pythonPath, MigrationReport report, string source, string entityKey)
        {
            if (string.IsNullOrWhiteSpace(pythonPath)) return null;

            string normalized = pythonPath.Replace('\\', '/').Trim();

            // Strip leading "assets/" and split into segments.
            if (normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("assets/".Length);

            string[] parts = normalized.Split('/');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], "items", StringComparison.OrdinalIgnoreCase))
                    parts[i] = "Items";
            }

            string unityPath = "Assets/_Project/Art/" + string.Join("/", parts);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(unityPath);
            if (sprite == null)
            {
                report.AddWarning(source, entityKey,
                    $"Sprite not found at '{unityPath}' (python: '{pythonPath}'). Asset may need importing.");
            }
            return sprite;
        }

        private static EquipSlot ParseEquipSlot(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return EquipSlot.None;
            return s.Trim().ToLowerInvariant() switch
            {
                "weapon"    => EquipSlot.Weapon,
                "head"      => EquipSlot.Head,
                "helmet"    => EquipSlot.Helmet,
                "body"      => EquipSlot.Body,
                "chest"     => EquipSlot.Chest,
                "boots"     => EquipSlot.Boots,
                "offhand"   => EquipSlot.Offhand,
                "shield"    => EquipSlot.Shield,
                "book"      => EquipSlot.Book,
                "ring"      => EquipSlot.Ring,
                "amulet"    => EquipSlot.Amulet,
                "trinket"   => EquipSlot.Trinket,
                "accessory" => EquipSlot.Accessory,
                _           => EquipSlot.None,
            };
        }

        private static ItemRarity ParseRarity(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return ItemRarity.Common;
            return s.Trim().ToLowerInvariant() switch
            {
                "common"    => ItemRarity.Common,
                "uncommon"  => ItemRarity.Uncommon,
                "rare"      => ItemRarity.Rare,
                "epic"      => ItemRarity.Epic,
                "legendary" => ItemRarity.Legendary,
                _           => ItemRarity.Common,
            };
        }

        private static int GetIntNullable(Dictionary<string, object> row, string key, int defaultValue)
        {
            if (!row.TryGetValue(key, out var v) || v == null) return defaultValue;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return defaultValue; }
        }

        private static float GetFloatNullable(Dictionary<string, object> row, string key, float defaultValue)
        {
            if (!row.TryGetValue(key, out var v) || v == null) return defaultValue;
            try { return Convert.ToSingle(v, CultureInfo.InvariantCulture); }
            catch { return defaultValue; }
        }

        private static bool GetBoolNullable(Dictionary<string, object> row, string key, bool defaultValue)
        {
            if (!row.TryGetValue(key, out var v) || v == null) return defaultValue;
            // SQLite encodes BOOLEAN as 0/1 INTEGER, but be defensive.
            try
            {
                if (v is bool b) return b;
                long n = Convert.ToInt64(v, CultureInfo.InvariantCulture);
                return n != 0;
            }
            catch { return defaultValue; }
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf   = Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static ItemCatalog LoadOrCreateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ITEMS_CATALOG_PATH);
            if (catalog != null) return catalog;

            EnsureFolder(ITEMS_OUTPUT_DIR);
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            AssetDatabase.CreateAsset(catalog, ITEMS_CATALOG_PATH);
            return catalog;
        }

        /// <summary>
        /// Recursively look up an existing <see cref="ItemDefinition"/> asset
        /// whose filename matches <paramref name="id"/>, scanning every
        /// category subfolder under <see cref="ITEMS_OUTPUT_DIR"/>. Used by
        /// the importer so re-runs upsert in place instead of creating a
        /// duplicate at the legacy flat path.
        /// </summary>
        private static ItemDefinition FindExistingItemAsset(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            string[] guids = AssetDatabase.FindAssets(
                $"{id} t:ItemDefinition", new[] { ITEMS_OUTPUT_DIR });
            if (guids == null || guids.Length == 0) return null;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                // FindAssets does substring matching, so 'iron_sword' would also
                // hit a hypothetical 'iron_sword_legendary'. Filter to exact id.
                if (string.Equals(fileName, id, StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            }
            return null;
        }

        /// <summary>
        /// Map a derived <see cref="ItemCategory"/> onto its on-disk subfolder.
        /// Keeps the importer's "create new asset" path consistent with how
        /// existing assets are organised (see <see cref="ItemCategoryUtil"/>).
        /// </summary>
        private static string SubfolderForCategory(ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Equipment:  return ITEMS_OUTPUT_DIR + "/Equipment";
                case ItemCategory.Consumable: return ITEMS_OUTPUT_DIR + "/Consumable";
                case ItemCategory.Material:   return ITEMS_OUTPUT_DIR + "/Material";
                case ItemCategory.Quest:      return ITEMS_OUTPUT_DIR + "/Quest";
                default:                      return ITEMS_OUTPUT_DIR + "/Other";
            }
        }
    }
}
