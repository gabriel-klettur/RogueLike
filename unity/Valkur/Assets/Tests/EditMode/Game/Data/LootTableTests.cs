using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data
{
    /// <summary>
    /// Pins <see cref="LootTable"/>: weighted selection, rarity fallback
    /// when weight is zero, drop-chance gate, and determinism with a
    /// supplied RNG (Phase-4 networking parity even for loot).
    /// </summary>
    [TestFixture]
    public class LootTableTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        private static ItemDefinition MakeItem(string id, ItemRarity rarity = ItemRarity.Common)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = id;
            item.rarity = rarity;
            return item;
        }

        private static LootTable MakeTable(int dropChancePerMille,
                                           params (ItemDefinition item, int weight)[] entries)
        {
            var table = ScriptableObject.CreateInstance<LootTable>();
            var arr = new LootTable.Entry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                arr[i] = new LootTable.Entry { item = entries[i].item, weight = entries[i].weight };
            table.EditorSetEntries(arr);
            table.EditorSetDropChance(dropChancePerMille);
            return table;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void Roll_EmptyTable_ReturnsNull()
        {
            var table = MakeTable(1000);
            Assert.IsNull(table.Roll(new System.Random(0)));
            Object.DestroyImmediate(table);
        }

        [Test]
        public void Roll_NullRng_ReturnsNull()
        {
            var item = MakeItem("a");
            var table = MakeTable(1000, (item, 100));
            try
            {
                Assert.IsNull(table.Roll(null),
                    "Null RNG must produce null — never throw, never crash a death-drop event.");
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void Roll_DropChanceZero_NeverDrops()
        {
            var item = MakeItem("a");
            var table = MakeTable(0, (item, 1000));
            try
            {
                for (int seed = 0; seed < 50; seed++)
                    Assert.IsNull(table.Roll(new System.Random(seed)),
                        $"Drop chance 0 must NEVER produce an item; seed {seed} did.");
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void Roll_DropChanceFull_AlwaysDrops()
        {
            var item = MakeItem("a");
            var table = MakeTable(1000, (item, 1));
            try
            {
                for (int seed = 0; seed < 50; seed++)
                    Assert.AreSame(item, table.Roll(new System.Random(seed)),
                        $"Single-entry table at 100% drop chance must always return the entry; seed {seed} did not.");
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void Roll_WeightZero_FallsBackToRarityWeight()
        {
            // Two items, weight=0 each. The table must use RarityPalette
            // fallback weights (Common 60, Legendary 1) so the Common drops
            // ~98% of the time across many rolls.
            var common = MakeItem("common", ItemRarity.Common);
            var legend = MakeItem("legend", ItemRarity.Legendary);
            var table = MakeTable(1000, (common, 0), (legend, 0));
            try
            {
                int commonHits = 0;
                int legendHits = 0;
                for (int seed = 0; seed < 1000; seed++)
                {
                    var picked = table.Roll(new System.Random(seed));
                    if (picked == common) commonHits++;
                    else if (picked == legend) legendHits++;
                }
                Assert.Greater(commonHits, 800,
                    $"With Common(60) vs Legendary(1) rarity weights, Common must " +
                    $"dominate in 1000 rolls. Got {commonHits} commons, {legendHits} legendaries.");
                Assert.Greater(legendHits, 0,
                    "Legendary weight is 1/61 ≈ 1.6%; over 1000 rolls at least one " +
                    "legendary must have dropped, otherwise the rarity fallback isn't firing.");
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(common);
                Object.DestroyImmediate(legend);
            }
        }

        [Test]
        public void Roll_ExplicitWeights_OverrideRarityFallback()
        {
            // Designer wants Legendary to be COMMON in this table (boss
            // chest). Explicit weight 1000 vs Common's weight 1 should
            // produce ~99.9% legendaries.
            var common = MakeItem("c", ItemRarity.Common);
            var legend = MakeItem("L", ItemRarity.Legendary);
            var table = MakeTable(1000, (common, 1), (legend, 1000));
            try
            {
                int legendHits = 0;
                for (int seed = 0; seed < 100; seed++)
                {
                    if (table.Roll(new System.Random(seed)) == legend)
                        legendHits++;
                }
                Assert.Greater(legendHits, 90,
                    $"Explicit weight 1000 vs 1 should produce >90% legendary; got {legendHits}/100.");
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(common);
                Object.DestroyImmediate(legend);
            }
        }

        [Test]
        public void Roll_Deterministic_SameRngStateYieldsSameItem()
        {
            // Phase-4 networking parity: two machines with the same RNG
            // produce the same drop sequence.
            var a = MakeItem("a");
            var b = MakeItem("b");
            var c = MakeItem("c");
            var table = MakeTable(1000, (a, 100), (b, 100), (c, 100));
            try
            {
                var rngA = new System.Random(1234);
                var rngB = new System.Random(1234);
                for (int i = 0; i < 20; i++)
                {
                    Assert.AreSame(table.Roll(rngA), table.Roll(rngB),
                        $"Roll {i}: RNGs at the same state must produce the same item.");
                }
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(c);
            }
        }

        [Test]
        public void ComputeTotalWeight_SkipsNullsAndZeroWeights()
        {
            var item = MakeItem("a");
            var table = MakeTable(1000,
                (item, 100),
                (null, 50)); // null item — entry is invalid
            try
            {
                Assert.AreEqual(100, table.ComputeTotalWeight(),
                    "Null-item entry must contribute zero, not 50, to the total.");
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(item);
            }
        }
    }
}
