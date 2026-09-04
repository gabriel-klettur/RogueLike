using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Chat
{
    /// <summary>
    /// Joins the imported personas to the entity definitions that spawn them, and gives the
    /// five trading vendors a shop.
    ///
    /// Kept apart from <see cref="ChatPersonaImporter"/> because they answer different
    /// questions and fail differently: that one turns recovered JSON into assets, this one
    /// decides WHICH entity is WHICH character and what a blacksmith sells. Re-running
    /// either must not need the other.
    ///
    /// <para>The join key is <c>MonsterDefinition.displayName</c> against the catalogue's
    /// <c>entityName</c> — "Gatita", "Felipondor", "Roberto" — which is the same key
    /// Python's <c>assignments.json</c> used and the same one <c>ChatSystem</c> falls back
    /// to at runtime. No prefix juggling between <c>vendor_cheff_gatita</c> and
    /// <c>npc_barbol_brother_felipondor</c>, which agree on nothing.</para>
    ///
    /// <para><c>EditorUtility.SetDirty</c> only, never <c>Undo.RecordObject</c> — see the
    /// note on <see cref="ChatPersonaImporter"/>.</para>
    /// </summary>
    public static class ChatEntityWiring
    {
        private const string MONSTER_DIR = "Assets/_Project/Data/Catalogs/Monsters";
        private const string VENDOR_CONFIG_DIR = "Assets/_Project/Data/Vendor/Configs";
        private const string CATALOG_PATH = "Assets/_Project/Resources/Chat/ChatAssignmentCatalog.asset";

        /// <summary>
        /// What each vendor stocks, as an <c>ItemDefinition.itemType</c>.
        ///
        /// The field already exists on every shipped item and already carries exactly these
        /// five trades — 8 food, 6 alchemy, 16 blacksmith, 12 lumberjack, 4 magic — so this
        /// table selects rather than invents. Its own doc comment says it is there "to group
        /// items independently of category and equipSlot", which is this.
        ///
        /// Abigail is deliberately absent. She is a BANKER: her persona offers "cofres
        /// seguros y certificados de depósito", and no such item exists in the catalogue.
        /// Giving her a config would put a Trade button on an empty shop, which reads as a
        /// bug; with no config she has no VendorNPC, no button, and simply talks.
        /// </summary>
        private static readonly Dictionary<string, string> TradeByPersonaId = new Dictionary<string, string>
        {
            { "vendor_cheff_gatita",      "food" },
            { "vendor_alchemist_valeria", "alchemy" },
            { "vendor_blacksmith_smith",  "blacksmith" },
            { "vendor_lumberjack_pavel",  "lumberjack" },
            { "vendor_mague_roberto",     "magic" },
        };

        /// <summary>
        /// Opening stock. A stackable is a consumable the player buys several of; a piece of
        /// equipment is bought once. Both are floors, not economy — restocking is
        /// <c>VendorEconomyService</c>'s problem, not this tool's.
        /// </summary>
        private const int STACKABLE_STOCK = 10;
        private const int UNIQUE_STOCK = 2;

        [MenuItem("Valkur/Chat/Wire Entities To Personas")]
        public static void Wire()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ChatAssignmentCatalog>(CATALOG_PATH);
            if (catalog == null || catalog.assignments.Count == 0)
            {
                Debug.LogError(
                    "[ChatEntityWiring] The assignment catalogue is missing or empty. " +
                    "Run 'Valkur > Chat > Import Personas' first.");
                return;
            }
            catalog.RebuildLookup();

            var items = AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>)
                .Where(i => i != null)
                .ToList();

            int wired = 0, shops = 0;

            foreach (var def in AssetDatabase.FindAssets("t:MonsterDefinition", new[] { MONSTER_DIR })
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                         .Where(d => d != null))
            {
                var persona = catalog.GetPersona(def.displayName);
                if (persona == null) continue;

                def.chatPersona = persona;
                wired++;

                if (TradeByPersonaId.TryGetValue(persona.personaId, out string itemType))
                {
                    def.vendorConfig = BuildVendorConfig(persona, itemType, items);
                    shops++;
                }

                EditorUtility.SetDirty(def);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ChatEntityWiring] {wired} definitions given a persona, {shops} of them a shop.");
        }

        /// <summary>
        /// Creates or refreshes the vendor's config and seeds it from the catalogue.
        ///
        /// The seed is REPLACED on every run, unlike the persona importer's fields. It is
        /// derived state — "everything of this trade the game ships" — so a stale seed is
        /// simply a vendor missing items added since, and there is nothing here a designer
        /// would have hand-tuned. Per-item price overrides, which they would, are left
        /// untouched.
        /// </summary>
        private static VendorConfigDefinition BuildVendorConfig(
            NPCPersonaDefinition persona, string itemType, List<ItemDefinition> items)
        {
            EnsureDirectory(VENDOR_CONFIG_DIR);

            string path = $"{VENDOR_CONFIG_DIR}/{persona.personaId}.asset";
            var config = AssetDatabase.LoadAssetAtPath<VendorConfigDefinition>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<VendorConfigDefinition>();
                AssetDatabase.CreateAsset(config, path);
            }

            config.vendorKey = persona.personaId;
            config.persona = persona;

            config.inventorySeed.Clear();
            foreach (var item in items.Where(i => i.itemType == itemType).OrderBy(i => i.itemId))
            {
                config.inventorySeed.Add(new VendorConfigDefinition.SeedSlot
                {
                    item = item,
                    quantity = item.stackable ? STACKABLE_STOCK : UNIQUE_STOCK,
                });
            }

            if (config.inventorySeed.Count == 0)
                Debug.LogWarning(
                    $"[ChatEntityWiring] '{persona.displayName}' trades '{itemType}' and the " +
                    "catalogue holds no item of that type. Their shop would open empty.");

            EditorUtility.SetDirty(config);
            return config;
        }

        private static void EnsureDirectory(string assetDir)
        {
            if (AssetDatabase.IsValidFolder(assetDir)) return;

            string parent = System.IO.Path.GetDirectoryName(assetDir)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(assetDir);
            if (!string.IsNullOrEmpty(parent)) EnsureDirectory(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
