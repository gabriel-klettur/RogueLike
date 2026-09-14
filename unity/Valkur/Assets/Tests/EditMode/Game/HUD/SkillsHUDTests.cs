using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The SKILLS tab and the split it exists for: talents (bought with points) and skills
    /// (raised by doing) are two tabs with two names.
    ///
    /// <para>Two halves on purpose. The synthetic half pins the RULES — grouping, the derived
    /// locked state, which detail a row opens — against catalogs the test controls. The shipped
    /// half pins the COMPOSITION a player meets: every skill in the shipped catalog has a row,
    /// woodcutting and cooking can be trained today, fishing and mining are shown locked.</para>
    /// </summary>
    [TestFixture]
    public class SkillsHUDTests
    {
        private readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _cleanup.Count - 1; i >= 0; i--)
                if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
            _cleanup.Clear();
        }

        private T Track<T>(T o) where T : Object { _cleanup.Add(o); return o; }

        private SkillDefinition Skill(string key, SkillCategory category, int order = 0)
        {
            var s = Track(ScriptableObject.CreateInstance<SkillDefinition>());
            s.skillKey = key;
            s.displayName = key;
            s.category = category;
            s.sortOrder = order;
            return s;
        }

        private SkillsHUD Build(SkillCatalog skills, RecipeCatalog recipes)
        {
            var go = Track(new GameObject("SkillsHUD"));
            var hud = go.AddComponent<SkillsHUD>();
            hud.BuildForTests(skills, recipes);
            return hud;
        }

        // ── The tabs ─────────────────────────────────────────────────────────

        [Test]
        public void TheSheet_SeparatesTalentsFromSkills()
        {
            var go = Track(new GameObject("Sheet"));
            var sheet = go.AddComponent<CharacterSheetController>();
            typeof(CharacterSheetController).GetMethod("BuildTabs", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sheet, null);

            var tabs = (System.Collections.IList)typeof(CharacterSheetController)
                .GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sheet);
            var labels = new List<string>();
            foreach (var tab in tabs)
                labels.Add((string)tab.GetType().GetField("Label").GetValue(tab));

            CollectionAssert.AreEqual(new[] { "CHARACTER", "TALENTOS", "GRIMOIRE", "SKILLS", "RECORDS" }, labels);
            Assert.AreEqual("TALENTOS", labels[CharacterSheetController.TabTalents]);
            Assert.AreEqual("SKILLS", labels[CharacterSheetController.TabSkills]);
            Assert.AreEqual("GRIMOIRE", labels[CharacterSheetController.TabGrimoire]);
        }

        // ── Rules, on synthetic catalogs ─────────────────────────────────────

        [Test]
        public void Rows_AreGrouped_GatheringFirst_ThenBySortOrder()
        {
            var catalog = Track(ScriptableObject.CreateInstance<SkillCatalog>());
            catalog.skills.Add(Skill("cook", SkillCategory.Crafting, 0));
            catalog.skills.Add(Skill("fish", SkillCategory.Gathering, 2));
            catalog.skills.Add(Skill("chop", SkillCategory.Gathering, 0));

            var hud = Build(catalog, null);

            CollectionAssert.AreEqual(new[] { "chop", "fish", "cook" }, hud.RowKeys);
        }

        [Test]
        public void ASkillWithNoContent_IsLocked_AndOpensTheLockedDetail()
        {
            var catalog = Track(ScriptableObject.CreateInstance<SkillCatalog>());
            catalog.skills.Add(Skill("fish", SkillCategory.Gathering));

            var hud = Build(catalog, null);

            Assert.IsFalse(hud.IsRowTrainable("fish"));
            Assert.AreEqual("fish", hud.SelectedKey, "with nothing trainable the first row still opens");
            Assert.AreEqual("locked", hud.DetailKind);
        }

        [Test]
        public void ACraftingSkillWithARecipe_IsTrainable_AndOpensTheRecipeDetail()
        {
            var cook = Skill("cook", SkillCategory.Crafting);
            var fish = Skill("fish", SkillCategory.Gathering);
            var catalog = Track(ScriptableObject.CreateInstance<SkillCatalog>());
            catalog.skills.Add(fish);
            catalog.skills.Add(cook);

            var recipes = Track(ScriptableObject.CreateInstance<RecipeCatalog>());
            var trade = Track(ScriptableObject.CreateInstance<ProfessionDefinition>());
            trade.professionKey = "cooking";
            trade.skill = cook;
            var item = Track(ScriptableObject.CreateInstance<ItemDefinition>());
            item.itemId = "stew";
            var recipe = Track(ScriptableObject.CreateInstance<RecipeDefinition>());
            recipe.recipeId = "stew";
            recipe.displayName = "Stew";
            recipe.profession = trade;
            recipe.output = item;
            recipe.ingredients = new[] { new RecipeIngredient(item, 1) };
            recipes.UpsertProfession(trade);
            recipes.Upsert(recipe);

            var hud = Build(catalog, recipes);

            Assert.IsTrue(hud.IsRowTrainable("cook"));
            Assert.IsFalse(hud.IsRowTrainable("fish"));
            Assert.AreEqual("cook", hud.SelectedKey, "the panel opens on the first skill that can be trained");
            Assert.AreEqual("crafting", hud.DetailKind);

            hud.Select("fish");
            Assert.AreEqual("locked", hud.DetailKind, "a locked row can still be read about");
        }

        [Test]
        public void Availability_IsDerivedFromContent_NotAuthored()
        {
            var skill = Skill("chop", SkillCategory.Gathering);
            Assert.IsFalse(SkillAvailability.IsTrainable(skill, null));

            var node = Track(ScriptableObject.CreateInstance<DestructionProfile>());
            skill.trainingNodes.Add(node);
            Assert.IsTrue(SkillAvailability.IsTrainable(skill, null), "a node that teaches it makes it live");
        }

        [Test]
        public void APhysicalSkill_GetsItsOwnGroup_AndIsTrainableWithNoContent()
        {
            var catalog = Track(ScriptableObject.CreateInstance<SkillCatalog>());
            catalog.skills.Add(Skill("chop", SkillCategory.Gathering));
            catalog.skills.Add(Skill("cook", SkillCategory.Crafting));
            catalog.skills.Add(Skill("athletics", SkillCategory.Physical));

            var hud = Build(catalog, null);

            // Gathering, then Crafting, then Physical — no node and no recipe needed for the last.
            CollectionAssert.AreEqual(new[] { "chop", "cook", "athletics" }, hud.RowKeys);
            Assert.IsTrue(hud.IsRowTrainable("athletics"), "a physical skill is trained by the body, not by content");

            hud.Select("athletics");
            Assert.AreEqual("physical", hud.DetailKind, "a physical skill opens its own detail, not the crafting fallback");
        }

        // ── Composition, on the shipped catalog ──────────────────────────────

        [Test]
        public void TheShippedTable_ShowsEverySkill_WithFishingAndMiningLocked()
        {
            var skills = SkillCatalog.Shared;
            var recipes = Resources.Load<RecipeCatalog>(RecipeCatalog.ResourcePath);
            if (skills == null) Assert.Ignore("No SkillCatalog — run Valkur > Skills > Seed Skill Content.");

            var hud = Build(skills, recipes);

            foreach (var key in new[] { "woodcutting", "mining", "fishing", "cooking", "blacksmith", "crafting" })
                CollectionAssert.Contains(hud.RowKeys, key, $"the SKILLS table has no row for {key}");

            Assert.IsTrue(hud.IsRowTrainable("woodcutting"), "trees teach woodcutting");
            Assert.IsTrue(hud.IsRowTrainable("cooking"), "cooking has recipes");
            Assert.IsFalse(hud.IsRowTrainable("fishing"), "nothing in the game trains fishing yet");
            Assert.IsFalse(hud.IsRowTrainable("mining"), "nothing in the game trains mining yet");
            Assert.AreEqual("woodcutting", hud.SelectedKey);
            Assert.AreEqual("gathering", hud.DetailKind);
        }
    }
}
