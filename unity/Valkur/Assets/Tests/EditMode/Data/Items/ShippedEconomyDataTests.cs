using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.Items
{
    /// <summary>
    /// The economy as it actually SHIPS, not as it is implemented.
    ///
    /// <para>Every fixture here asserts on the assets in the project, because the audit that
    /// produced them found nothing wrong with the code and a great deal wrong with the data:
    /// 66 of 236 items priced at zero, five of five vendors with no economy group, one of 25
    /// monsters with a loot table and none with a coin reward. Not one of those is visible to
    /// a unit test over the pipeline — every one of them resolves to a plausible number.</para>
    ///
    /// <para>This is the same argument <c>SPAWNER_COORDINATE_SPACE_DRIFT</c> makes: assert on
    /// the composition and on the shipped bytes, because both halves can be internally
    /// consistent while the product is wrong.</para>
    /// </summary>
    public class ShippedEconomyDataTests
    {
        private static List<ItemDefinition> LoadItems() => Load<ItemDefinition>("t:ItemDefinition");
        private static List<T> Load<T>(string filter) where T : Object =>
            AssetDatabase.FindAssets(filter)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .ToList();

        /// <summary>
        /// Items whose price is legitimately zero because they are not merchandise. Kept as an
        /// explicit list rather than a rule, so adding one is a decision somebody made rather
        /// than a gap a heuristic happened to cover.
        /// </summary>
        private static readonly HashSet<string> NotMerchandise = new HashSet<string>
        {
            "experience_orb",
        };

        /// <summary>
        /// A zero buy price is not "free" — it falls through to the fallback heuristic and
        /// resolves to ONE COIN for anything stackable. That is how the entire output of the
        /// mining profession (64 ores and gems, up to rarity 4) came to be worth the same as
        /// a pebble, with every number in the pipeline behaving correctly.
        /// </summary>
        [Test]
        public void EveryTradeableItem_CarriesAPrice()
        {
            var items = LoadItems();
            Assert.That(items.Count, Is.GreaterThan(100), "item catalogue did not load; this test would be vacuous");

            var unpriced = items
                .Where(i => !NotMerchandise.Contains(i.itemId))
                .Where(i => i.buyPrice <= 0)
                .Select(i => i.itemId)
                .ToList();

            Assert.IsEmpty(unpriced,
                $"{unpriced.Count} item(s) have no buy price and silently resolve to the 1-coin " +
                $"fallback: {string.Join(", ", unpriced.Take(15))}");
        }

        /// <summary>
        /// A vendor must never pay more for a thing than it charges. One item where that holds
        /// is a coin press: buy, sell back, repeat. Checked at the DATA level because a margin
        /// or a cycle can only scale a spread that is already the right way round.
        /// </summary>
        [Test]
        public void NoItem_SellsForMoreThanItCosts()
        {
            var arbitrage = LoadItems()
                .Where(i => i.buyPrice > 0 && i.sellPrice > i.buyPrice)
                .Select(i => $"{i.itemId} (buy {i.buyPrice} / sell {i.sellPrice})")
                .ToList();

            Assert.IsEmpty(arbitrage, "these items can be bought and sold back at a profit: " +
                                      string.Join(", ", arbitrage));
        }

        /// <summary>
        /// Rarity has to mean something in coins, or the ladder is decoration. Compares the
        /// mean price of the mining outputs per rarity tier, since those are the one family
        /// priced as a set — comparing across families would measure what a thing IS, not how
        /// rare it is.
        /// </summary>
        [Test]
        public void MineralPrices_RiseWithRarity()
        {
            var byRarity = LoadItems()
                .Where(i => i.itemType == "mineral" && i.buyPrice > 0)
                .GroupBy(i => (int)i.rarity)
                .ToDictionary(g => g.Key, g => g.Average(i => i.buyPrice));

            Assert.That(byRarity.Count, Is.GreaterThan(2), "not enough mineral tiers to compare");

            var tiers = byRarity.Keys.OrderBy(k => k).ToList();
            for (int i = 1; i < tiers.Count; i++)
            {
                Assert.That(byRarity[tiers[i]], Is.GreaterThan(byRarity[tiers[i - 1]]),
                    $"rarity {tiers[i]} minerals are not worth more than rarity {tiers[i - 1]}");
            }
        }

        /// <summary>
        /// Every vendor must reference an economy group.
        ///
        /// <para>This is the one that shipped broken and the one hardest to notice: with
        /// <c>economyGroup</c> null, <c>GetMargin</c> is never consulted and both margin steps
        /// resolve to exactly 1 — so the shop works, the prices look right, and every counter
        /// in the world charges identically. Nothing distinguishes "no group" from "a group
        /// whose margins are 1".</para>
        /// </summary>
        [Test]
        public void EveryVendor_HasAnEconomyGroup()
        {
            var configs = Load<VendorConfigDefinition>("t:VendorConfigDefinition");
            Assert.That(configs.Count, Is.GreaterThan(0), "no vendor configs found; this test would be vacuous");

            var orphans = configs.Where(c => c.economyGroup == null).Select(c => c.vendorKey).ToList();
            Assert.IsEmpty(orphans, "vendors with no economy group (their margins are silently 1): " +
                                    string.Join(", ", orphans));
        }

        /// <summary>
        /// A group has to be a spread, not a setting: buying above the reference and selling
        /// below it is the only shape in which a shop is a shop. A group inverted by a typo
        /// would let the player farm that vendor, and every individual number in it would
        /// still look reasonable.
        /// </summary>
        [Test]
        public void EveryEconomyGroup_BuysDearerThanItSells()
        {
            var groups = Load<EconomyGroupDefinition>("t:EconomyGroupDefinition");
            Assert.That(groups.Count, Is.GreaterThan(0), "no economy groups found; this test would be vacuous");

            foreach (var g in groups)
            {
                Assert.That(g.defaultMargin.buyMultiplier,
                    Is.GreaterThanOrEqualTo(g.defaultMargin.sellMultiplier),
                    $"{g.groupKey}: default margin pays more than it charges");

                foreach (var tm in g.typeMargins)
                {
                    Assert.That(tm.margin.buyMultiplier, Is.GreaterThanOrEqualTo(tm.margin.sellMultiplier),
                        $"{g.groupKey}/{tm.itemType}: type margin pays more than it charges");
                }
            }
        }

        /// <summary>
        /// The 64 minerals are the whole product of the mining profession and had no buyer at
        /// all — which is most of why nobody noticed they were priced at zero. A profession
        /// whose output no counter accepts at a fair rate is a profession with no economy.
        /// </summary>
        [Test]
        public void SomeVendor_DealsFairlyInMinerals()
        {
            var buyers = Load<EconomyGroupDefinition>("t:EconomyGroupDefinition")
                .Where(g => g.typeMargins.Any(t => t.itemType == "mineral"))
                .Select(g => g.groupKey)
                .ToList();

            Assert.IsNotEmpty(buyers,
                "no economy group names 'mineral' as a specialty; every ore in the game sells " +
                "at the outsider spread and mining has no destination");
        }

        /// <summary>
        /// A fresh character must be able to buy SOMETHING. Starting at zero coins in a game
        /// whose faucet is selling firewood at a coin a swing is not a difficulty curve, it is
        /// a shop the player can only look at — and it was the shipped state, because
        /// CurrencyWallet's own startingCoins field is unreachable from any inspector.
        /// </summary>
        [Test]
        public void EveryPlayableClass_StartsWithAPurse()
        {
            var players = Load<PlayerDefinition>("t:PlayerDefinition");
            Assert.That(players.Count, Is.GreaterThan(0), "no player definitions found; this test would be vacuous");

            var broke = players.Where(p => p.startingCoins <= 0).Select(p => p.playerKey).ToList();
            Assert.IsEmpty(broke, "classes that start with nothing to spend: " + string.Join(", ", broke));
        }
    }
}
