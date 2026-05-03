using NUnit.Framework;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data
{
    /// <summary>
    /// Pins the canonical rarity palette so a future "let me just tweak the
    /// purple" edit doesn't silently re-skin every tooltip in the game.
    /// Also pins the default drop-weight ladder used by loot tables.
    /// </summary>
    [TestFixture]
    public class RarityPaletteTests
    {
        [Test]
        public void Color_DistinctPerRarity()
        {
            // Each tier must produce a different colour — otherwise UI can't
            // visually distinguish rarities and the whole palette is useless.
            var colors = new[]
            {
                RarityPalette.Color(ItemRarity.Common),
                RarityPalette.Color(ItemRarity.Uncommon),
                RarityPalette.Color(ItemRarity.Rare),
                RarityPalette.Color(ItemRarity.Epic),
                RarityPalette.Color(ItemRarity.Legendary),
            };
            for (int i = 0; i < colors.Length; i++)
                for (int j = i + 1; j < colors.Length; j++)
                    Assert.AreNotEqual(colors[i], colors[j],
                        $"Colors at indices {i} and {j} match — tiers must be visually distinct.");
        }

        [Test]
        public void DropWeights_DescendByRarity()
        {
            // Common must be the most likely drop, Legendary the least —
            // monotonic drop in weight is the contract for any loot table.
            int common    = RarityPalette.DefaultDropWeight(ItemRarity.Common);
            int uncommon  = RarityPalette.DefaultDropWeight(ItemRarity.Uncommon);
            int rare      = RarityPalette.DefaultDropWeight(ItemRarity.Rare);
            int epic      = RarityPalette.DefaultDropWeight(ItemRarity.Epic);
            int legendary = RarityPalette.DefaultDropWeight(ItemRarity.Legendary);

            Assert.Greater(common, uncommon);
            Assert.Greater(uncommon, rare);
            Assert.Greater(rare, epic);
            Assert.Greater(epic, legendary);
            Assert.Greater(legendary, 0,
                "Legendary weight must be positive — a 0 makes legendaries impossible to drop.");
        }

        [Test]
        public void DropWeights_SumTo100ByDefault()
        {
            // 60+25+10+4+1 = 100. Future edits must keep the sum at 100 so
            // designers don't have to renormalise when building loot tables.
            int sum = RarityPalette.DefaultDropWeight(ItemRarity.Common)
                    + RarityPalette.DefaultDropWeight(ItemRarity.Uncommon)
                    + RarityPalette.DefaultDropWeight(ItemRarity.Rare)
                    + RarityPalette.DefaultDropWeight(ItemRarity.Epic)
                    + RarityPalette.DefaultDropWeight(ItemRarity.Legendary);
            Assert.AreEqual(100, sum,
                "Default rarity weights must sum to 100 so designers can read " +
                "them as percentages without renormalising.");
        }

        [Test]
        public void DisplayName_ReadsHuman()
        {
            Assert.AreEqual("Common",    RarityPalette.DisplayName(ItemRarity.Common));
            Assert.AreEqual("Uncommon",  RarityPalette.DisplayName(ItemRarity.Uncommon));
            Assert.AreEqual("Rare",      RarityPalette.DisplayName(ItemRarity.Rare));
            Assert.AreEqual("Epic",      RarityPalette.DisplayName(ItemRarity.Epic));
            Assert.AreEqual("Legendary", RarityPalette.DisplayName(ItemRarity.Legendary));
        }

        [Test]
        public void EnumOrder_IsStable()
        {
            // Pin the underlying integer values — ItemDefinition assets serialize
            // ItemRarity by int in YAML, so reordering the enum silently rewrites
            // every authored item to a different rarity.
            Assert.AreEqual(0, (int)ItemRarity.Common);
            Assert.AreEqual(1, (int)ItemRarity.Uncommon);
            Assert.AreEqual(2, (int)ItemRarity.Rare);
            Assert.AreEqual(3, (int)ItemRarity.Epic);
            Assert.AreEqual(4, (int)ItemRarity.Legendary);
        }
    }
}
