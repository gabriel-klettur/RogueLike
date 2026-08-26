using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Valkur.Gameplay.Buildings;
using Cat = Valkur.Gameplay.Buildings.BuildingCategory.Category;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Structural guards on the F10 picker's taxonomy, independent of what the catalog
    /// happens to contain.
    ///
    /// <see cref="BuildingCategoryTests"/> pins the CLASSIFICATION — which folder lands in
    /// which tab. These pin the taxonomy's own shape, and every one of them fails silently
    /// rather than loudly: a category missing from <c>TabOrder</c> is one no author can ever
    /// select even though templates keep routing into it; a missing <c>Label</c> arm falls
    /// through to <c>c.ToString()</c>, which is almost right and therefore easy to miss; a
    /// rule ordered after the folder rule it was meant to override simply never fires.
    ///
    /// The taxonomy grew from 8 categories to 15 in one change, which is exactly when this
    /// class of mistake gets made.
    /// </summary>
    public class BuildingCategoryTaxonomyTests
    {
        private static IEnumerable<Cat> AllCategories()
            => (Cat[])Enum.GetValues(typeof(Cat));

        [Test]
        public void TabOrder_ContainsEveryCategoryExactlyOnce()
        {
            var declared = AllCategories().ToList();
            var tabs = BuildingCategory.TabOrder.ToList();

            var missing = declared.Where(c => !tabs.Contains(c)).ToList();
            Assert.That(missing, Is.Empty,
                "Categories with no tab — templates still classify into them, so the picker " +
                "hides them from every author: " + string.Join(", ", missing));

            var duplicated = tabs.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.That(duplicated, Is.Empty,
                "Categories listed twice in TabOrder: " + string.Join(", ", duplicated));

            Assert.AreEqual(declared.Count, tabs.Count);
        }

        [Test]
        public void EveryCategory_HasItsOwnExplicitLabel()
        {
            var byLabel = new Dictionary<string, Cat>(StringComparer.Ordinal);
            var problems = new List<string>();

            foreach (Cat c in AllCategories())
            {
                string label = BuildingCategory.Label(c);

                if (string.IsNullOrWhiteSpace(label))
                {
                    problems.Add($"{c}: empty label");
                    continue;
                }

                if (byLabel.TryGetValue(label, out Cat clash))
                    problems.Add($"{c} and {clash} share the label '{label}'");
                else
                    byLabel[label] = c;
            }

            Assert.That(problems, Is.Empty, string.Join("; ", problems));
        }

        [Test]
        public void EveryLabel_FitsTheTabItIsDrawnIn()
        {
            // The panel gives its content 368 px and the strip splits that four to a row,
            // so a label has ~92 px at font size 10. "Structures", the longest name in the
            // taxonomy, is the working budget; anything longer wraps and clips.
            const int maxChars = 12;

            var tooLong = AllCategories()
                .Select(c => new { c, label = BuildingCategory.Label(c) })
                .Where(x => x.label.Length > maxChars)
                .Select(x => $"{x.c} = '{x.label}' ({x.label.Length} chars)")
                .ToList();

            Assert.That(tooLong, Is.Empty,
                $"Labels longer than {maxChars} characters wrap inside their tab: " +
                string.Join(", ", tooLong));
        }

        [Test]
        public void Of_NeverThrows_AndAlwaysReturnsADeclaredCategory()
        {
            string[] hostile =
            {
                null, "", "   ", "Buildings/", "buildings", "/", "Buildings//double",
                "NotBuildings/whatever", "Buildings/unknown_folder/thing",
                "BUILDINGS/LIGHTS/SHOUTING", "Buildings/lights/",
            };

            foreach (string path in hostile)
            {
                Cat c = Cat.Structures;
                Assert.DoesNotThrow(() => c = BuildingCategory.Of(path), $"Of('{path}') threw");
                CollectionAssert.Contains(AllCategories().ToList(), c,
                    $"Of('{path}') returned a value outside the enum");
            }
        }

        [Test]
        public void Of_IsCaseInsensitiveAcrossEveryRule()
        {
            // TryMatch lowercases the path and compares against lowercase prefixes; a rule
            // typed with a capital would never match anything and drain into the fallback.
            string[] samples =
            {
                "Buildings/lights/lamp_post_classic",
                "Buildings/military/watchtower_wooden",
                "Buildings/graveyard/mausoleum_small",
                "Buildings/arcane/portal_arch_arcane",
                "Buildings/blacksmith/anvil_stump",
                "Buildings/domestic/rocking_horse",
                "Buildings/bandit/campfire_lit",
                "Buildings/water/well_roofed_red",
                "Buildings/quest/quest_board_blue_roof",
                "Buildings/statues/statue_king_crowned",
            };

            foreach (string path in samples)
            {
                Assert.IsTrue(BuildingCategory.TryMatch(path, out Cat lower),
                    $"'{path.ToLowerInvariant()}' matched no rule");
                Assert.IsTrue(BuildingCategory.TryMatch(path.ToUpperInvariant(), out Cat upper));
                Assert.AreEqual(lower, upper, $"'{path}' classifies differently by case");
            }
        }

        [Test]
        public void FileLevelRules_BeatTheFolderRuleTheyOverride()
        {
            // The three assets dumped into others/ have nothing in common, so each is
            // routed by a rule that must sit BEFORE the folder rule. Reordering them is a
            // one-line mistake that silently sends the portal to Props.
            Assert.AreEqual(Cat.Arcane, BuildingCategory.Of("Buildings/others/portal_wow"));
            Assert.AreEqual(Cat.Props, BuildingCategory.Of("Buildings/others/guillotina"));

            // Same shape in nature/: the tree_ prefix must beat the folder.
            Assert.AreEqual(Cat.Trees, BuildingCategory.Of("Buildings/nature/tree_oak_broad_green"));
            Assert.AreEqual(Cat.Flora, BuildingCategory.Of("Buildings/nature/bush_round_plain_green"));
        }
    }
}
