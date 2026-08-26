using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Cat = Valkur.Gameplay.Buildings.BuildingCategory.Category;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Guards the Buildings Editor (F10) template category tabs.
    ///
    /// The classification is a prefix table over <c>assetPath</c>, so it rots quietly:
    /// rename a Resources folder or import a sprite into a new one, and those templates
    /// slide into the Structures fallback with no error anywhere. Every tab would still
    /// render — just with the wrong contents. These tests make that loud.
    /// </summary>
    [TestFixture]
    public class BuildingCategoryTests
    {
        private const string CATALOG_PATH =
            "Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset";

        /// <summary>
        /// Longest label the 3-column strip can render at font size 10 inside the 256 px
        /// Buildings panel before TextMeshPro wraps it mid-word.
        /// </summary>
        private const int MAX_LABEL_CHARS = 10;

        private static BuildingCatalog LoadCatalog()
        {
            var cat = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(CATALOG_PATH);
            Assert.IsNotNull(cat, $"BuildingCatalog not found at {CATALOG_PATH}.");
            return cat;
        }

        // ── Contract ──────────────────────────────────────────────────────────────

        [Test]
        public void Of_NullOrEmpty_FallsBackToStructures_WithoutThrowing()
        {
            Assert.AreEqual(Cat.Structures, BuildingCategory.Of((string)null));
            Assert.AreEqual(Cat.Structures, BuildingCategory.Of(""));
            Assert.AreEqual(Cat.Structures, BuildingCategory.Of((BuildingTemplateData)null));
        }

        [Test]
        public void TryMatch_ReportsFalse_ForAPathNoRuleCovers()
        {
            Assert.IsFalse(BuildingCategory.TryMatch("Buildings/not_a_real_folder/thing", out Cat c));
            Assert.AreEqual(Cat.Structures, c, "The out value must still be usable.");
        }

        [TestCase("Buildings/lights/lamp_post_classic",      Cat.Lights)]
        [TestCase("Buildings/lights/brazier_iron_cage",      Cat.Lights)]
        [TestCase("Buildings/signs/sign_blacksmith_anvil",   Cat.Signs)]
        [TestCase("Buildings/signs/banner_dragon_green",     Cat.Signs)]
        [TestCase("Buildings/market/stall_awning_red_produce", Cat.Market)]
        [TestCase("Buildings/market/crate_apples_mixed",     Cat.Market)]
        [TestCase("Buildings/props/well_stone_tiled_roof",   Cat.Props)]
        [TestCase("Buildings/props/fence_picket_wood",       Cat.Props)]
        [TestCase("Buildings/nature/bush_round_plain_green", Cat.Flora)]
        [TestCase("Buildings/nature/flowers_white_daisies",  Cat.Flora)]
        [TestCase("Buildings/nature/mushrooms_red_brown_mix", Cat.Flora)]
        // The tree sprites inside nature/ belong with the other trees, not with the grass.
        [TestCase("Buildings/nature/tree_oak_broad_green",   Cat.Trees)]
        [TestCase("Buildings/nature/tree_pine_conifer",      Cat.Trees)]
        [TestCase("Buildings/vegetation/tree_7",             Cat.Trees)]
        [TestCase("Buildings/vegetation/tree_azul",          Cat.Trees)]
        [TestCase("Buildings/gardens/flowers_3",             Cat.Flora)]
        [TestCase("Buildings/forest_decoration/natural/seta_blanca", Cat.Flora)]
        [TestCase("Buildings/forest_decoration/corrupto/raiz_retorcida", Cat.Flora)]
        [TestCase("Buildings/houses/orden_house_2",          Cat.Structures)]
        [TestCase("Buildings/shops/blacksmith",              Cat.Structures)]
        [TestCase("Buildings/temples/catholic",              Cat.Structures)]
        [TestCase("Buildings/castles/castle_1",              Cat.Structures)]
        [TestCase("Buildings/combat/coliseo",                Cat.Structures)]
        [TestCase("Buildings/backgrounds/background_lobby",  Cat.Structures)]
        [TestCase("Buildings/mine",                          Cat.Structures)]
        [TestCase("Buildings/dummy",                         Cat.Structures)]
        [TestCase("Buildings/portals/portal_stone_arch",     Cat.Arcane)]
        [TestCase("Buildings/totems/totem_forest",           Cat.Arcane)]
        // statues/ moved out of Arcane when the second prop wave added 32 civic monuments:
        // a crowned king on a plinth and a summoning circle are never browsed together.
        [TestCase("Buildings/statues/statue_dwarf_warrior",  Cat.Monuments)]
        // ── Second prop wave (2026-08), one tab per themed sheet ──────────────
        [TestCase("Buildings/military/watchtower_wooden",    Cat.Military)]
        [TestCase("Buildings/military/training_dummy_target", Cat.Military)]
        [TestCase("Buildings/graveyard/headstone_gothic_arch", Cat.Graveyard)]
        [TestCase("Buildings/graveyard/mausoleum_small",     Cat.Graveyard)]
        [TestCase("Buildings/arcane/portal_arch_arcane",     Cat.Arcane)]
        [TestCase("Buildings/arcane/summoning_circle_blood", Cat.Arcane)]
        [TestCase("Buildings/blacksmith/anvil_stump",        Cat.Forge)]
        [TestCase("Buildings/blacksmith/forge_full_workshop", Cat.Forge)]
        [TestCase("Buildings/bandit/wanted_poster_board",    Cat.Bandit)]
        [TestCase("Buildings/bandit/campfire_lit",           Cat.Bandit)]
        [TestCase("Buildings/water/well_roofed_red",         Cat.Water)]
        [TestCase("Buildings/water/manhole_cover_stone",     Cat.Water)]
        [TestCase("Buildings/quest/quest_board_blue_roof",   Cat.Quest)]
        [TestCase("Buildings/quest/chest_guild_open_gold",   Cat.Quest)]
        [TestCase("Buildings/statues/statue_king_crowned",   Cat.Monuments)]
        [TestCase("Buildings/statues/sundial_stone_round",   Cat.Monuments)]
        // Household clutter is what Props already means, so domestic/ joins it
        // rather than earning a sixteenth tab.
        [TestCase("Buildings/domestic/rocking_horse",        Cat.Props)]
        [TestCase("Buildings/domestic/clothesline_sheets",   Cat.Props)]
        // The village sheet's own rows land in the first wave's categories.
        [TestCase("Buildings/houses/house_cottage_thatched", Cat.Structures)]
        [TestCase("Buildings/shops/tavern_three_storey_a",   Cat.Structures)]
        [TestCase("Buildings/nature/tree_pine_slim",         Cat.Trees)]
        [TestCase("Buildings/nature/hedge_tall_green",       Cat.Flora)]
        // others/ holds three unrelated assets; the file rules must beat the folder rule.
        [TestCase("Buildings/others/Portal_wow",             Cat.Arcane)]
        [TestCase("Buildings/others/fuente",                 Cat.Props)]
        [TestCase("Buildings/others/guillotina",             Cat.Props)]
        public void Of_ClassifiesRepresentativePaths(string assetPath, Cat expected)
            => Assert.AreEqual(expected, BuildingCategory.Of(assetPath), assetPath);

        [Test]
        public void Of_IsCaseInsensitive()
        {
            // Legacy paths are not consistently cased: forest_decoration/natural ships a
            // "Flor_silvestre_azul" next to lowercase siblings.
            Assert.AreEqual(Cat.Flora,
                BuildingCategory.Of("Buildings/forest_decoration/natural/Flor_silvestre_azul"));
            Assert.AreEqual(Cat.Lights, BuildingCategory.Of("BUILDINGS/LIGHTS/LAMP_POST_CLASSIC"));
        }

        // ── Tab strip integrity ───────────────────────────────────────────────────

        [Test]
        public void TabOrder_ListsEveryCategoryExactlyOnce()
        {
            var all = (Cat[])Enum.GetValues(typeof(Cat));
            CollectionAssert.AreEquivalent(all, BuildingCategory.TabOrder,
                "TabOrder must list every Category once — a missing entry hides a whole tab.");
            Assert.AreEqual(BuildingCategory.TabOrder.Length,
                BuildingCategory.TabOrder.Distinct().Count(), "TabOrder has a duplicate.");
        }

        [Test]
        public void EveryLabel_FitsTheStrip()
        {
            var tooLong = BuildingCategory.TabOrder
                .Select(c => BuildingCategory.Label(c))
                .Where(l => string.IsNullOrEmpty(l) || l.Length > MAX_LABEL_CHARS)
                .ToList();

            Assert.That(tooLong, Is.Empty,
                $"Labels over {MAX_LABEL_CHARS} characters wrap mid-word in the 3-column strip: " +
                string.Join(", ", tooLong));
        }

        // ── Catalog coverage ──────────────────────────────────────────────────────

        [Test]
        public void EveryCatalogTemplate_MatchesAnExplicitRule()
        {
            var unmatched = LoadCatalog().Templates
                .Where(t => t != null && !BuildingCategory.TryMatch(t.assetPath, out Cat _))
                .Select(t => $"#{t.templateId} '{t.assetPath}'")
                .Distinct()
                .ToList();

            Assert.That(unmatched, Is.Empty,
                "These templates fall through to the Structures fallback — add a rule in " +
                "BuildingCategory so they land in the right tab: " + string.Join(", ", unmatched.Take(10)));
        }

        [Test]
        public void EveryTab_HasAtLeastOneTemplate()
        {
            var counts = CountByCategory();
            var empty = BuildingCategory.TabOrder.Where(c => counts[c] == 0).ToList();

            Assert.That(empty, Is.Empty,
                "A tab with nothing in it is a dead click: " +
                string.Join(", ", empty.Select(BuildingCategory.Label)));
        }

        [Test]
        public void TheTabs_PartitionTheWholeCatalog()
        {
            var catalog = LoadCatalog();
            int total = catalog.Templates.Count(t => t != null);
            int sum = CountByCategory().Values.Sum();

            Assert.AreEqual(total, sum,
                "Every template must be reachable from exactly one tab; 'All' plus the tabs " +
                "must add up to the catalog.");
        }

        private static Dictionary<Cat, int> CountByCategory()
        {
            var counts = ((Cat[])Enum.GetValues(typeof(Cat))).ToDictionary(c => c, _ => 0);
            foreach (BuildingTemplateData t in LoadCatalog().Templates)
            {
                if (t == null) continue;
                counts[BuildingCategory.Of(t)]++;
            }
            return counts;
        }
    }
}
