using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Pins the shipped mineral set and the extraction curve it produces.
    ///
    /// <para>The curve is the feature. Sixty-four minerals are only worth having if the
    /// common ones pour out and the extraordinary ones almost never do, and that spread comes
    /// from ONE mechanism: every entry in the pool is authored at weight 0, which makes
    /// <see cref="LootTable"/> derive the weight from the item's own rarity. So the tests here
    /// assert the SHAPE of the distribution rather than any single number — a designer must be
    /// able to retune the ladder without a red suite, and must not be able to flatten it by
    /// accident.</para>
    /// </summary>
    [TestFixture]
    public class MineralCatalogTests
    {
        private const string MineralItemFolder =
            "Assets/_Project/Data/Catalogs/Items/Material/Minerals";
        private const string PoolPath =
            "Assets/_Project/Data/Catalogs/Destruction/LT_mine_iron_yield.asset";
        private const string MineProfilePath =
            "Assets/_Project/Data/Catalogs/Destruction/DP_mine_iron.asset";

        private static List<ItemDefinition> LoadMinerals()
        {
            var found = new List<ItemDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition",
                         new[] { MineralItemFolder }))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (item != null) found.Add(item);
            }
            Assert.That(found, Is.Not.Empty, $"No minerals under {MineralItemFolder}.");
            return found;
        }

        // The items ----------------------------------------------------------------------

        [Test]
        public void EveryMineral_IsComplete()
        {
            foreach (var m in LoadMinerals())
            {
                string where = AssetDatabase.GetAssetPath(m);
                Assert.That(m.itemId, Is.Not.Empty, where);
                Assert.That(m.displayName, Is.Not.Empty, where);

                // An icon is not decoration for these: an inventory of sixty-four grey rocks
                // that differ only by name is unreadable, and the art is the only thing that
                // makes a mineral recognisable at a glance.
                Assert.That(m.icon, Is.Not.Null, $"{where} has no icon.");

                Assert.That(m.stackable, Is.True, $"{where} must stack; a seam pays out dozens.");
                Assert.That(m.maxStack, Is.GreaterThan(1), where);
            }
        }

        [Test]
        public void EveryMineral_IsReachableThroughTheCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(
                "Assets/_Project/Data/Catalogs/Items/ItemCatalog.asset");
            Assert.That(catalog, Is.Not.Null);

            foreach (var m in LoadMinerals())
                Assert.That(catalog.GetById(m.itemId), Is.SameAs(m),
                    $"'{m.itemId}' exists as an asset but the catalog cannot resolve it, so " +
                    "every drop of it would log a warning and spawn nothing.");
        }

        [Test]
        public void MineralIds_AreUnique()
        {
            var seen = new Dictionary<string, string>();
            foreach (var m in LoadMinerals())
            {
                Assert.That(seen.ContainsKey(m.itemId), Is.False,
                    $"'{m.itemId}' is used by both {seen.GetValueOrDefault(m.itemId)} and " +
                    $"{AssetDatabase.GetAssetPath(m)}. ItemCatalog.GetById returns the first, " +
                    "so the second is unreachable and its drops silently become the first.");
                seen[m.itemId] = AssetDatabase.GetAssetPath(m);
            }
        }

        [Test]
        public void TheLadder_CoversEveryTier()
        {
            var tiers = new HashSet<ItemRarity>();
            foreach (var m in LoadMinerals()) tiers.Add(m.rarity);

            // A set with no Legendary has nothing to hope for and a set with no Common has
            // nothing to stand on. Both ends have to exist for the curve to mean anything.
            Assert.That(tiers, Contains.Item(ItemRarity.Common));
            Assert.That(tiers, Contains.Item(ItemRarity.Legendary));
            Assert.That(tiers.Count, Is.GreaterThanOrEqualTo(4),
                "A ladder needs rungs; two tiers is a coin flip with extra steps.");
        }

        // The pool -----------------------------------------------------------------------

        [Test]
        public void ThePool_HoldsEveryMineralAndNothingElse()
        {
            var pool = AssetDatabase.LoadAssetAtPath<LootTable>(PoolPath);
            Assert.That(pool, Is.Not.Null, $"Missing {PoolPath}.");

            var inPool = new HashSet<string>();
            foreach (var e in pool.Entries)
            {
                Assert.That(e, Is.Not.Null);
                Assert.That(e.item, Is.Not.Null, "An empty line silently eats its own weight.");
                inPool.Add(e.item.itemId);
            }

            foreach (var m in LoadMinerals())
                Assert.That(inPool, Contains.Item(m.itemId),
                    $"'{m.itemId}' was authored and can never be mined — it is in no pool.");
        }

        [Test]
        public void ThePool_DerivesItsWeightsFromRarity()
        {
            var pool = AssetDatabase.LoadAssetAtPath<LootTable>(PoolPath);

            // Weight 0 means "ask the item's rarity". Hand-authoring the sixty-four weights
            // instead would work today and would have to be re-balanced in full every time a
            // mineral is added, which is exactly the tax this pool exists to avoid.
            foreach (var e in pool.Entries)
                Assert.That(e.weight, Is.Zero,
                    $"'{e.item.itemId}' overrides its rarity with an explicit weight. That is " +
                    "legal, but it takes that mineral out of the ladder — do it deliberately " +
                    "or not at all.");
        }

        [Test]
        public void TheSeam_DrawsFromThePool()
        {
            var mine = AssetDatabase.LoadAssetAtPath<DestructionProfile>(MineProfilePath);
            var pool = AssetDatabase.LoadAssetAtPath<LootTable>(PoolPath);

            Assert.That(mine.yieldPool, Is.SameAs(pool),
                "The seam is not wired to the pool, so sixty-four minerals exist and none of " +
                "them can come out of the ground.");
        }

        // The curve ----------------------------------------------------------------------

        [Test]
        public void TheCurve_FavoursTheCommonAndHoardsTheLegendary()
        {
            var pool = AssetDatabase.LoadAssetAtPath<LootTable>(PoolPath);

            // A fixed seed: LootTable.Roll is deterministic given one, which is the property
            // that lets this assert a distribution at all rather than hoping.
            var rng = new System.Random(20260903);
            var byTier = new Dictionary<ItemRarity, int>();
            var byItem = new Dictionary<string, int>();
            const int rolls = 60000;

            for (int i = 0; i < rolls; i++)
            {
                var item = pool.Roll(rng);
                Assert.That(item, Is.Not.Null, "A pool at drop chance 1000 must always yield.");
                byTier[item.rarity] = byTier.GetValueOrDefault(item.rarity) + 1;
                byItem[item.itemId] = byItem.GetValueOrDefault(item.itemId) + 1;
            }

            float Share(ItemRarity r) => 100f * byTier.GetValueOrDefault(r) / rolls;

            // Ordered, not pinned. The exact percentages are a designer's business; the ORDER
            // is the feature, and it is what breaks silently if someone gives a legendary a
            // common's rarity by mistake.
            Assert.That(Share(ItemRarity.Common), Is.GreaterThan(Share(ItemRarity.Uncommon)));
            Assert.That(Share(ItemRarity.Uncommon), Is.GreaterThan(Share(ItemRarity.Rare)));
            Assert.That(Share(ItemRarity.Rare), Is.GreaterThan(Share(ItemRarity.Epic)));
            Assert.That(Share(ItemRarity.Epic), Is.GreaterThan(Share(ItemRarity.Legendary)));

            Assert.That(Share(ItemRarity.Common), Is.GreaterThan(35f),
                "Ordinary rock has to be the bulk of a shift or the extraordinary stops reading " +
                "as extraordinary.");
            Assert.That(Share(ItemRarity.Legendary), Is.LessThan(3f).And.GreaterThan(0f),
                "A legendary must be rare AND reachable. Zero is a mineral nobody will ever " +
                "see; common is a mineral nobody will value.");

            // Every mineral has to be findable. One that never comes up is authored art, an
            // authored icon and an authored name that no player can ever meet.
            Assert.That(byItem.Count, Is.EqualTo(pool.Entries.Count),
                "Some minerals never came up in 60,000 rolls.");
        }
    }
}
