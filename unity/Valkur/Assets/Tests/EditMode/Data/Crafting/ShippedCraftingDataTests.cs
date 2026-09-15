using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Tests.Support;

namespace Valkur.Tests.EditMode.Data.Crafting
{
    /// <summary>
    /// The SHIPPED crafting content — the assets a player actually meets.
    ///
    /// <para>A different question from <see cref="CraftingServiceTests"/>, which pins the rules
    /// against synthetic data. These read the real catalog, because the failure this project
    /// keeps repeating is not a broken rule: it is authored data that is internally consistent
    /// and disagrees only with the screen. A recipe whose ingredient reference went null does
    /// not throw and does not warn — it silently stops being craftable, which from inside the
    /// game is indistinguishable from a player who has not found the ingredient yet.</para>
    ///
    /// <para>The composition checks are the point, not either half. That is the lesson of
    /// <c>SPAWNER_COORDINATE_SPACE_DRIFT</c>: the art existed, the recipes existed, and only
    /// the JOIN between them could be wrong.</para>
    /// </summary>
    [TestFixture]
    [Category(TestCategories.ShippedData)]
    public class ShippedCraftingDataTests
    {
        private const string CATALOG_PATH = "Assets/_Project/Resources/Crafting/RecipeCatalog.asset";
        private const string MANIFEST_RELATIVE = "tools/atlas/generated/crafting_manifest.json";

        private RecipeCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<RecipeCatalog>(CATALOG_PATH);
            if (_catalog == null)
                Assert.Ignore($"No RecipeCatalog at {CATALOG_PATH} — run " +
                              "Valkur > Crafting > Import Crafting Content.");
        }

        [Test]
        public void EveryRecipe_IsWellFormed()
        {
            var bad = _catalog.FindMalformed();
            if (bad.Count == 0) return;

            var names = new List<string>();
            foreach (var r in bad) names.Add(r != null ? r.recipeId : "<null>");
            Assert.Fail($"{bad.Count} recipe(s) missing an output, a profession or an " +
                        $"ingredient: {string.Join(", ", names)}");
        }

        [Test]
        public void EveryRecipe_BelongsToACatalogedProfession()
        {
            // A recipe whose profession is not in the catalog draws no tab, so it is
            // unreachable from the panel — and nothing throws to say so.
            foreach (var recipe in _catalog.Recipes)
            {
                if (recipe == null) continue;
                Assert.IsNotNull(recipe.profession, $"{recipe.recipeId} has no profession");
                Assert.AreSame(recipe.profession,
                    _catalog.GetProfession(recipe.profession.professionKey),
                    $"{recipe.recipeId}'s profession is not the one the catalog indexes under " +
                    $"'{recipe.profession.professionKey}'");
            }
        }

        [Test]
        public void EveryProfession_TrainsACatalogedCraftingSkill()
        {
            // A trade's progression is its skill. One pointing at nothing makes every recipe of it
            // malformed; one pointing at a skill the catalog does not carry is a trade the SKILLS
            // tab never shows.
            var skills = SkillCatalog.Shared;
            Assert.IsNotNull(skills, "no SkillCatalog under Resources/Skills");

            foreach (var p in _catalog.Professions)
            {
                Assert.IsNotNull(p);
                Assert.IsFalse(string.IsNullOrEmpty(p.professionKey), "a profession with no key "
                    + "cannot be joined to a recipe");
                Assert.IsNotNull(p.skill, $"{p.professionKey} trains no skill");
                Assert.AreSame(p.skill, skills.Find(p.skill.skillKey),
                    $"{p.professionKey}'s skill '{p.skill.skillKey}' is not the one the catalog carries");
            }
        }

        [Test]
        public void TheLumberjackTrade_IsGone()
        {
            // Felling trees IS the woodcutting skill. A lumberjack trade beside it is the
            // duplicate the skills table would show twice.
            Assert.IsNull(_catalog.GetProfession("lumberjack"));
        }

        [Test]
        public void RecipeRequirements_AreInRange_AndSomethingIsLearnableFromZero()
        {
            // A requirement past 100 % is unreachable content that looks valid in the Inspector;
            // a trade whose every recipe needs training can never be started.
            var byTrade = new Dictionary<ProfessionDefinition, int>();
            foreach (var recipe in _catalog.Recipes)
            {
                if (recipe?.profession == null) continue;
                Assert.That(recipe.requiredSkill, Is.InRange(0, 100), recipe.recipeId);
                Assert.GreaterOrEqual(recipe.skillGainRolls, 1, $"{recipe.recipeId} teaches nothing");
                if (recipe.requiredSkill == 0)
                    byTrade[recipe.profession] = byTrade.TryGetValue(recipe.profession, out int n) ? n + 1 : 1;
            }

            foreach (var p in _catalog.Professions)
            {
                if (_catalog.RecipesFor(p).Count == 0) continue;
                Assert.IsTrue(byTrade.ContainsKey(p), $"{p.professionKey} has no recipe a beginner can make");
            }
        }

        [Test]
        public void EveryRecipeOutput_CarriesAnIcon()
        {
            // A row with no icon reads as a broken import. The panel degrades gracefully, which
            // is exactly why nobody would notice.
            foreach (var recipe in _catalog.Recipes)
            {
                if (recipe?.output == null) continue;
                Assert.IsNotNull(recipe.output.icon,
                    $"{recipe.recipeId} produces {recipe.output.itemId}, which has no icon");
            }
        }

        /// <summary>
        /// The taxonomy the whole feature rests on: ingredients are Materials, products are
        /// Consumables — and neither is STORED, both are derived from the item's own fields by
        /// <c>ItemCategoryUtil</c>. Writing a raw ingredient's hunger onto the item would file
        /// it as a Consumable and collapse the split on the next import, which is why the
        /// importer deliberately drops <c>rawHunger</c>.
        /// </summary>
        [Test]
        public void CookingItems_FallOnTheRightSideOfTheCategorySplit()
        {
            int ingredients = 0, products = 0;

            foreach (var recipe in _catalog.Recipes)
            {
                if (recipe?.output == null) continue;
                Assert.AreEqual(ItemCategory.Consumable, recipe.output.GetCategory(),
                    $"{recipe.output.itemId} is a finished dish and must be a Consumable");
                products++;

                foreach (var line in recipe.ingredients)
                {
                    if (!line.IsValid) continue;
                    Assert.AreEqual(ItemCategory.Material, line.item.GetCategory(),
                        $"{line.item.itemId} is an ingredient and must be a Material — a "
                        + "non-zero hunger would file it as a Consumable");
                    ingredients++;
                }
            }

            Assert.Greater(products, 0, "no products found — did the import run?");
            Assert.Greater(ingredients, 0, "no ingredients found — did the import run?");
        }

        /// <summary>
        /// Crafting must never lose money, or the whole system is a trap the player learns to
        /// avoid. Checked against the recipe's own ingredients rather than a constant, so it
        /// keeps meaning something after a price retune.
        /// </summary>
        [Test]
        public void EveryProduct_IsWorthMoreThanItsIngredients()
        {
            foreach (var recipe in _catalog.Recipes)
            {
                if (recipe?.output == null || !recipe.IsWellFormed) continue;

                int raw = 0;
                foreach (var line in recipe.ingredients) raw += line.item.value * line.quantity;
                int made = recipe.output.value * recipe.outputQuantity;

                Assert.Greater(made, raw,
                    $"{recipe.recipeId} costs {raw} in ingredients and is worth {made}");
            }
        }

        /// <summary>
        /// The generated manifest and the imported assets must agree.
        ///
        /// <para>This is the structural check CLAUDE.md prescribes: a count comparison that is
        /// independent of any individual value, so it catches a half-finished import that every
        /// per-field test would pass. Reading the manifest off DISK rather than through Unity
        /// also sidesteps the stale-asset trap — <c>AssetDatabase.LoadAssetAtPath</c> returns
        /// Unity's in-memory copy, which can disagree with the file after a data edit.</para>
        /// </summary>
        [Test]
        public void CatalogCounts_MatchTheGeneratedManifest()
        {
            string path = Path.Combine(RepoRoot(), MANIFEST_RELATIVE);
            if (!File.Exists(path))
                Assert.Ignore($"No manifest at {path} — run " +
                              "python tools/crafting/build_crafting_manifest.py");

            string json = File.ReadAllText(path);
            int manifestRecipes = CountOccurrences(json, "\"recipeId\"");
            int manifestProfessions = CountOccurrences(json, "\"professionKey\"");

            Assert.AreEqual(manifestRecipes, _catalog.Count,
                "the catalog and the manifest disagree on how many recipes exist — a partial "
                + "import passes every per-field test");
            Assert.AreEqual(manifestProfessions, _catalog.Professions.Count,
                "the catalog and the manifest disagree on how many professions exist");
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, index = 0;
            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        private static string RepoRoot()
        {
            // <repo>/unity/Valkur/Assets -> <repo>
            var dir = new DirectoryInfo(Application.dataPath);
            return dir.Parent?.Parent?.Parent?.FullName ?? Application.dataPath;
        }
    }
}
