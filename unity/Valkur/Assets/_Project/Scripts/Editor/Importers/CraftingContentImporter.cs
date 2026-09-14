using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Editor.Crafting
{
    /// <summary>
    /// Builds every trade's professions, items and recipes from
    /// <c>tools/atlas/generated/crafting_manifest.json</c>.
    ///
    /// <para>THE IMPORTER DOES NO ARITHMETIC. Every number it writes was resolved by
    /// <c>tools/crafting/build_crafting_manifest.py</c> from one authored ingredient price
    /// table, so the balance is auditable from outside Unity and a price change re-prices every
    /// product that uses it. Splitting it the other way — a formula in C# — would put the
    /// balance somewhere only a running Editor can evaluate.</para>
    ///
    /// <para>WHICH FIELDS A RE-RUN OVERWRITES, and why it is not the usual rule. The persona
    /// and tileset importers fill only EMPTY fields, because everything they carry is prose a
    /// designer may rewrite. Here the fields split in two: the DERIVED numbers (value, sell
    /// price, healing, hunger, rarity, weight, stacking, and every requirement of a recipe) are
    /// always rewritten, because a generator that cannot propagate its own retune is not a
    /// generator; the PROSE (display name, description) is filled only when empty, so a
    /// rewritten name survives. <c>Import Crafting Content (Overwrite Authored)</c> replaces
    /// the prose too.</para>
    ///
    /// <para><c>EditorUtility.SetDirty</c> only, never <c>Undo.RecordObject</c> — this is a bulk
    /// asset-import tool, and recording dozens of creations onto the global undo stack is what
    /// reverted 193 building templates to their empty creation state the first time anything
    /// popped it. See the note in CLAUDE.md.</para>
    /// </summary>
    public static class CraftingContentImporter
    {
        private const string MANIFEST_RELATIVE = "tools/atlas/generated/crafting_manifest.json";

        private const string INGREDIENT_DIR = "Assets/_Project/Data/Catalogs/Items/Material/Cook";
        private const string PRODUCT_DIR = "Assets/_Project/Data/Catalogs/Items/Consumable/Cook";
        private const string PROFESSION_DIR = "Assets/_Project/Data/Catalogs/Crafting/Professions";
        private const string RECIPE_DIR = "Assets/_Project/Data/Catalogs/Crafting/Recipes";
        private const string CATALOG_DIR = "Assets/_Project/Resources/Crafting";
        private const string CATALOG_PATH = CATALOG_DIR + "/RecipeCatalog.asset";
        private const string ITEM_CATALOG_PATH = "Assets/_Project/Data/Catalogs/Items/ItemCatalog.asset";
        private const string SKILL_CATALOG_PATH = "Assets/_Project/Resources/Skills/SkillCatalog.asset";

        /// <summary>
        /// The domain tag cooking items carry. Tagging both the ingredients and the dishes
        /// <c>food</c> is what lets the cook vendor stock them — the cheapest ingredient source
        /// and the only one that needs no other system.
        ///
        /// <para>IT DOES NOT TAKE EFFECT ON ITS OWN, and assuming it did would be wrong in the
        /// quiet way this project keeps paying for. <c>VendorConfigDefinition.inventorySeed</c>
        /// is a STATIC list of items, not a live filter over <c>itemType</c> — so a newly
        /// imported ingredient is tagged correctly, sits in the item catalog, and is absent
        /// from every shop until <c>Valkur &gt; Chat &gt; Wire Entities To Personas</c> is
        /// re-run. That rebuild clears and repopulates the seed from the tag
        /// (<c>ChatEntityWiring.BuildVendorConfig</c>), so it is safe to re-run and is the
        /// second half of this import. Measured after doing so: Gatita's stock went to 64
        /// food slots.</para>
        /// </summary>
        private const string COOKING_ITEM_TYPE = "food";

        /// <summary>
        /// Six dishes shipped earlier under ad-hoc ids, from single hand-cut PNGs: the same six
        /// plates the new sheet draws. They are retired rather than kept because keeping them
        /// puts two borschts in the player's inventory with different art and different
        /// numbers, and only one of them craftable. Verified unreferenced before this list was
        /// written — the sole hit across the project is a worked EXAMPLE inside an LLM prompt
        /// string in <c>ChatLlmSettings</c>, which names no asset.
        ///
        /// <para><c>food_chicken</c> is deliberately NOT here: it is roast chicken, a distinct
        /// item the new sheet does not draw, and retiring it would delete content.</para>
        /// </summary>
        private static readonly string[] LEGACY_DISH_IDS =
        {
            "borsh_01", "perogi_01", "completo_chileno_01",
            "paella_01", "tortilla_spain_01", "hakarl_01",
        };

        [MenuItem("Valkur/Crafting/Import Crafting Content")]
        public static void Import() => Run(overwriteAuthored: false);

        [MenuItem("Valkur/Crafting/Import Crafting Content (Overwrite Authored)")]
        public static void ImportOverwrite()
        {
            if (!EditorUtility.DisplayDialog(
                    "Overwrite authored crafting prose?",
                    "Every display name and description will be replaced by the manifest, " +
                    "discarding anything edited in the Inspector or the Skills editor.\n\n" +
                    "The plain 'Import Crafting Content' already refreshes every derived " +
                    "NUMBER and is what you normally want.",
                    "Overwrite", "Cancel"))
                return;

            Run(overwriteAuthored: true);
        }

        private static void Run(bool overwriteAuthored)
        {
            string manifestPath = Path.Combine(RepoRoot(), MANIFEST_RELATIVE).Replace('\\', '/');
            if (!File.Exists(manifestPath))
            {
                Debug.LogError(
                    $"[CraftingContentImporter] No manifest at '{manifestPath}'. " +
                    "Run: python tools/crafting/build_crafting_manifest.py");
                return;
            }

            if (!(MiniJsonRuntime.Deserialize(File.ReadAllText(manifestPath))
                    is Dictionary<string, object> root))
            {
                Debug.LogError($"[CraftingContentImporter] '{manifestPath}' is not a JSON object.");
                return;
            }

            EnsureDirectory(INGREDIENT_DIR);
            EnsureDirectory(PRODUCT_DIR);
            EnsureDirectory(PROFESSION_DIR);
            EnsureDirectory(RECIPE_DIR);
            EnsureDirectory(CATALOG_DIR);

            var itemCatalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ITEM_CATALOG_PATH);
            if (itemCatalog == null)
            {
                Debug.LogError($"[CraftingContentImporter] No ItemCatalog at '{ITEM_CATALOG_PATH}'.");
                return;
            }

            // A trade's progression is a SKILL, and the skills are seeded by
            // Valkur > Skills > Seed Skill Content, not here — this importer resolves them by key
            // and refuses to guess. A profession left without one would import cleanly and make
            // every one of its recipes malformed.
            var skillCatalog = AssetDatabase.LoadAssetAtPath<SkillCatalog>(SKILL_CATALOG_PATH);
            if (skillCatalog == null)
            {
                Debug.LogError($"[CraftingContentImporter] No SkillCatalog at '{SKILL_CATALOG_PATH}'. " +
                               "Run Valkur > Skills > Seed Skill Content first.");
                return;
            }

            int created = 0;
            var catalog = LoadOrCreate<RecipeCatalog>(CATALOG_PATH, ref created);

            // Professions first: a recipe cannot be wired without one, and resolving them up
            // front turns the manifest's profession KEY back into an asset reference exactly
            // once. Runtime code holds the reference and never the string.
            var professions = new Dictionary<string, ProfessionDefinition>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var entry in AsList(root, "professions"))
            {
                if (!(entry is Dictionary<string, object> row)) continue;
                string key = Str(row, "professionKey");
                if (string.IsNullOrEmpty(key)) continue;

                var profession = LoadOrCreate<ProfessionDefinition>(
                    $"{PROFESSION_DIR}/{key}.asset", ref created);
                ApplyProfession(profession, row, key, overwriteAuthored, skillCatalog);
                EditorUtility.SetDirty(profession);

                professions[key] = profession;
                catalog.UpsertProfession(profession);
            }

            // A trade the manifest no longer declares is retired from the catalog. The lumberjack
            // trade went this way when felling trees became the woodcutting skill: left in, it
            // would draw a crafting tab for a trade with no recipes and no skill.
            int retiredProfessions = catalog.RetireProfessionsNotIn(professions.Keys);

            int missingArt = 0;
            var byId = new Dictionary<string, ItemDefinition>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var entry in AsList(root, "items"))
            {
                if (!(entry is Dictionary<string, object> row)) continue;
                string itemId = Str(row, "itemId");
                if (string.IsNullOrEmpty(itemId)) continue;

                string dir = Str(row, "role") == "product" ? PRODUCT_DIR : INGREDIENT_DIR;
                var item = LoadOrCreate<ItemDefinition>($"{dir}/{itemId}.asset", ref created);
                if (!ApplyItem(item, row, itemId, overwriteAuthored)) missingArt++;

                EditorUtility.SetDirty(item);
                itemCatalog.Upsert(item);
                byId[itemId] = item;
            }

            int broken = 0;
            foreach (var entry in AsList(root, "recipes"))
            {
                if (!(entry is Dictionary<string, object> row)) continue;
                string recipeId = Str(row, "recipeId");
                if (string.IsNullOrEmpty(recipeId)) continue;

                var recipe = LoadOrCreate<RecipeDefinition>(
                    $"{RECIPE_DIR}/{recipeId}.asset", ref created);
                if (!ApplyRecipe(recipe, row, recipeId, byId, professions, overwriteAuthored))
                    broken++;

                EditorUtility.SetDirty(recipe);
                catalog.Upsert(recipe);
            }

            int retired = RetireLegacyDishes(itemCatalog);

            itemCatalog.Compact();
            catalog.Compact();
            EditorUtility.SetDirty(itemCatalog);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (missingArt > 0)
                Debug.LogWarning($"[CraftingContentImporter] {missingArt} item(s) imported with " +
                                 "no icon. Run: python tools/atlas/wave10/build_cook_items.py");
            if (broken > 0)
                Debug.LogError($"[CraftingContentImporter] {broken} recipe(s) reference an item " +
                               "or profession the manifest never defined — not craftable.");

            Debug.Log($"[CraftingContentImporter] {catalog.Professions.Count} professions, " +
                      $"{itemCatalog.Count} items, {catalog.Count} recipes " +
                      $"({created} assets created), {retired} legacy dish(es) and " +
                      $"{retiredProfessions} profession(s) retired.");
        }

        // ── Professions ─────────────────────────────────────────────────────

        private static void ApplyProfession(ProfessionDefinition profession,
            Dictionary<string, object> row, string key, bool overwriteAuthored, SkillCatalog skills)
        {
            profession.professionKey = key;

            if (overwriteAuthored || string.IsNullOrEmpty(profession.displayName))
                profession.displayName = Str(row, "displayName");
            if (overwriteAuthored || string.IsNullOrEmpty(profession.description))
                profession.description = Str(row, "description");
            if (overwriteAuthored || string.IsNullOrEmpty(profession.stationName))
                profession.stationName = Str(row, "stationName");

            profession.sortOrder = (int)Flt(row, "sortOrder");

            string skillKey = Str(row, "skillKey");
            profession.skill = skills.Find(skillKey);
            if (profession.skill == null)
                Debug.LogError($"[CraftingContentImporter] Profession '{key}' trains skill " +
                               $"'{skillKey}', which the SkillCatalog does not carry.");

            var rgb = AsDict(row, "accentColor");
            if (rgb != null)
                profession.accentColor = new Color(Flt(rgb, "r"), Flt(rgb, "g"),
                    Flt(rgb, "b"), Mathf.Max(0.01f, Flt(rgb, "a")));
        }

        // ── Items ───────────────────────────────────────────────────────────

        /// <summary>
        /// Write one item. Returns false when its icon could not be resolved.
        ///
        /// <para>RAW_INGREDIENTS_ARE_MATERIALS. The manifest carries a <c>rawHunger</c> for
        /// every ingredient and this deliberately does not write it to <c>hunger</c>.
        /// <c>ItemCategory</c> is DERIVED, not stored, and any non-zero hunger sends
        /// <c>ItemCategoryUtil.GetCategory</c> to Consumable — so a potato that could be
        /// nibbled would file itself in the Consumables tab alongside the finished plates, and
        /// the split the whole feature rests on would collapse on its first import. The field
        /// stays in the manifest because eating raw is a real feature to add later; it needs a
        /// separate consume path, not a hunger value.</para>
        /// </summary>
        private static bool ApplyItem(ItemDefinition item, Dictionary<string, object> row,
            string itemId, bool overwriteAuthored)
        {
            item.itemId = itemId;
            if (Str(row, "profession") == "cooking") item.itemType = COOKING_ITEM_TYPE;

            if (overwriteAuthored || string.IsNullOrEmpty(item.displayName))
                item.displayName = Str(row, "displayName");
            if (overwriteAuthored || string.IsNullOrEmpty(item.description))
                item.description = Str(row, "description");

            // Derived: always rewritten. See the class note on why this half does not follow
            // the "authored value wins" rule the prose above does.
            item.stackable = Bl(row, "stackable", true);
            item.maxStack = Mathf.Max(1, (int)Flt(row, "maxStack"));
            item.value = (int)Flt(row, "value");
            item.buyPrice = item.value;
            item.sellPrice = (int)Flt(row, "sellPrice");
            item.rarity = (ItemRarity)Mathf.Clamp((int)Flt(row, "rarity"), 0, 4);
            item.weight = Flt(row, "weight");
            item.healing = Flt(row, "healing");
            item.hunger = Flt(row, "hunger");

            // Matches the shipped food items so a product dropped on the ground reads at the
            // same size as the ones authored before this pipeline existed.
            item.scaleMap = 0.04f;
            item.scaleInventory = 0.05f;
            item.despawnTime = 60f;
            item.zLayer = 1;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Str(row, "art"));
            if (sprite == null) return false;

            item.icon = sprite;
            item.iconSmall = sprite;
            return true;
        }

        // ── Recipes ─────────────────────────────────────────────────────────

        /// <summary>
        /// Write one recipe. Returns false when the profession, the output or any ingredient
        /// could not be resolved — which leaves a recipe <c>IsWellFormed</c> refuses and the
        /// panel silently drops, so the caller reports it as an error rather than letting it
        /// pass.
        /// </summary>
        private static bool ApplyRecipe(RecipeDefinition recipe, Dictionary<string, object> row,
            string recipeId, Dictionary<string, ItemDefinition> byId,
            Dictionary<string, ProfessionDefinition> professions, bool overwriteAuthored)
        {
            recipe.recipeId = recipeId;
            recipe.group = Str(row, "group");

            if (overwriteAuthored || string.IsNullOrEmpty(recipe.displayName))
                recipe.displayName = Str(row, "displayName");

            recipe.requiresStation = Bl(row, "requiresStation", false);
            recipe.requiredSkill = Mathf.Clamp((int)Flt(row, "requiredSkill"), 0, 100);
            recipe.skillGainRolls = Mathf.Max(0, (int)Flt(row, "skillGainRolls"));
            recipe.craftSeconds = Flt(row, "craftSeconds");
            recipe.outputQuantity = Mathf.Max(1, (int)Flt(row, "outputQuantity"));

            bool ok = professions.TryGetValue(Str(row, "profession"), out var profession);
            recipe.profession = profession;

            ok &= byId.TryGetValue(Str(row, "outputItemId"), out var output);
            recipe.output = output;

            var lines = AsList(row, "ingredients");
            var resolved = new List<RecipeIngredient>(lines.Count);
            foreach (var line in lines)
            {
                if (!(line is Dictionary<string, object> l)) continue;
                if (!byId.TryGetValue(Str(l, "itemId"), out var ingredient))
                {
                    ok = false;
                    continue;
                }
                resolved.Add(new RecipeIngredient(ingredient, Mathf.Max(1, (int)Flt(l, "quantity"))));
            }
            recipe.ingredients = resolved.ToArray();
            return ok && resolved.Count == lines.Count;
        }

        // ── Legacy retirement ───────────────────────────────────────────────

        /// <summary>
        /// Delete the six superseded dish assets and their art, and take them out of the item
        /// catalog. Idempotent: a second run finds nothing and reports zero.
        /// </summary>
        private static int RetireLegacyDishes(ItemCatalog catalog)
        {
            int retired = 0;
            foreach (string id in LEGACY_DISH_IDS)
            {
                string assetPath = $"Assets/_Project/Data/Catalogs/Items/Consumable/{id}.asset";
                string artPath = $"Assets/_Project/Art/Items/cook/{id}.png";

                bool removed = catalog.Remove(id);
                bool deleted = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath) != null
                               && AssetDatabase.DeleteAsset(assetPath);
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(artPath) != null)
                    AssetDatabase.DeleteAsset(artPath);

                if (removed || deleted) retired++;
            }
            return retired;
        }

        // ── Asset plumbing ──────────────────────────────────────────────────

        private static T LoadOrCreate<T>(string path, ref int created) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created++;
            return asset;
        }

        private static void EnsureDirectory(string assetDir)
        {
            if (AssetDatabase.IsValidFolder(assetDir)) return;

            string parent = Path.GetDirectoryName(assetDir)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureDirectory(parent);

            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetDir));
        }

        /// <summary>
        /// Repo root, derived from <c>Application.dataPath</c>. The manifest lives in
        /// <c>tools/</c>, outside the Unity project, because it is generated by Python and
        /// nothing in the game loads it at runtime.
        /// </summary>
        private static string RepoRoot()
        {
            // <repo>/unity/Valkur/Assets -> <repo>
            var dir = new DirectoryInfo(Application.dataPath);
            return dir.Parent?.Parent?.Parent?.FullName ?? Application.dataPath;
        }

        // ── MiniJson accessors ──────────────────────────────────────────────

        private static Dictionary<string, object> AsDict(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        private static List<object> AsList(Dictionary<string, object> d, string key) =>
            (d != null && d.TryGetValue(key, out var v) ? v as List<object> : null) ?? new List<object>();

        private static string Str(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as string ?? "" : "";

        private static float Flt(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? ToFloat(v) : 0f;

        private static bool Bl(Dictionary<string, object> d, string key, bool fallback) =>
            d != null && d.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        private static float ToFloat(object value)
        {
            switch (value)
            {
                case double d: return (float)d;
                case float f: return f;
                case long l: return l;
                case int i: return i;
                default: return 0f;
            }
        }
    }
}
