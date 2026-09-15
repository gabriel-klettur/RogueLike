using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.World.Gathering
{
    /// <summary>
    /// Pins the SHIPPED woodcutting content and the pure skill maths: that 100 % takes real time
    /// but is reachable, that skill finds better wood, that every wood is findable somewhere, and
    /// that every tree template is wired to its family's profile.
    ///
    /// <para>The time test SIMULATES the definition's own gain chance rather than asserting its
    /// fields, so a retune that makes mastery an afternoon — or a lifetime — fails here with the
    /// number of hours in the message, instead of being discovered by a player.</para>
    /// </summary>
    [TestFixture]
    public class WoodcuttingDataTests
    {
        private const string SkillPath = "Assets/_Project/Data/Catalogs/Skills/GS_woodcutting.asset";
        private const float SecondsPerBlow = 0.6f;

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, $"Missing {path}.");
            return asset;
        }

        private static SkillDefinition Skill => Load<SkillDefinition>(SkillPath);

        private static List<DestructionProfile> TreeProfiles()
        {
            var list = new List<DestructionProfile>();
            foreach (var guid in AssetDatabase.FindAssets("t:DestructionProfile"))
            {
                var p = AssetDatabase.LoadAssetAtPath<DestructionProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && p.name.StartsWith("DP_tree_")) list.Add(p);
            }
            return list;
        }

        private static double Hours(double blows) => blows * SecondsPerBlow / 3600d;

        // ── The curve ──────────────────────────────────────────────────────────────

        [Test]
        public void Mastery_TakesHours_OnSuitableTrees_AndIsReachable()
        {
            double blows = Skill.ExpectedBlowsToMax(0, s => Mathf.Clamp(Mathf.RoundToInt(s), 0, 80));
            Assert.That(double.IsInfinity(blows), Is.False, "100 % must be reachable.");
            Assert.That(Hours(blows), Is.InRange(3d, 12d),
                $"Ideal progression reaches 100 % in {Hours(blows):0.0} h of chopping.");
        }

        [Test]
        public void TheFirstHalf_IsQuick_TheSecondHalf_IsTheLongClimb()
        {
            var skill = Skill;
            double firstHalf = 0d, secondHalf = 0d;
            for (int t = 0; t < 1000; t++)
            {
                float s = t / 10f;
                double cost = 1d / skill.GainChance(t, Mathf.Clamp(Mathf.RoundToInt(s), 0, 80), false);
                if (t < 500) firstHalf += cost; else secondHalf += cost;
            }
            Assert.That(Hours(firstHalf), Is.LessThan(1d), "Early progress must be felt within the first session.");
            Assert.That(secondHalf, Is.GreaterThan(firstHalf * 4d), "The late climb must dominate.");
        }

        [Test]
        public void ChoppingOnlyTrivialTrees_StillProgresses_ButFarSlower()
        {
            double ideal = Skill.ExpectedBlowsToMax(0, s => Mathf.Clamp(Mathf.RoundToInt(s), 0, 80));
            double trivial = Skill.ExpectedBlowsToMax(0, s => 15);

            Assert.That(double.IsInfinity(trivial), Is.False,
                "A world with no harder trees nearby must slow the climb, not end it.");
            Assert.That(trivial, Is.GreaterThan(ideal * 2d), "Harder trees must be worth walking to.");
        }

        [Test]
        public void GainChance_FallsWithSkill_AndWithTheWrongTool()
        {
            var skill = Skill;
            Assert.That(skill.GainChance(0, 15, false), Is.GreaterThan(skill.GainChance(500, 50, false)));
            Assert.That(skill.GainChance(500, 50, false), Is.GreaterThan(skill.GainChance(900, 80, false)));
            Assert.That(skill.GainChance(200, 20, true), Is.LessThan(skill.GainChance(200, 20, false)));
            Assert.That(skill.GainChance(SkillDefinition.MaxTenths, 100, false), Is.Zero);
        }

        [Test]
        public void Efficiency_RisesWithSkill_AndFallsWithDifficulty()
        {
            var skill = Skill;
            Assert.That(skill.EfficiencyMultiplier(800, 40), Is.GreaterThan(skill.EfficiencyMultiplier(200, 40)));
            Assert.That(skill.EfficiencyMultiplier(400, 10), Is.GreaterThan(skill.EfficiencyMultiplier(400, 80)));
            Assert.That(skill.EfficiencyMultiplier(0, 100), Is.GreaterThan(0f), "No tree is unchoppable by skill alone.");
        }

        // ── The wood table ─────────────────────────────────────────────────────────

        [Test]
        public void EveryWoodItem_IsInExactlyOneTier()
        {
            var table = Skill.yieldTable;
            Assert.That(table, Is.Not.Null);

            var seen = new Dictionary<string, int>();
            foreach (var tier in table.tiers)
                foreach (var item in tier.items)
                {
                    Assert.That(item, Is.Not.Null, $"Null item in tier '{tier.key}'.");
                    seen[item.itemId] = seen.TryGetValue(item.itemId, out int n) ? n + 1 : 1;
                }

            for (int i = 1; i <= 64; i++)
            {
                string id = $"wood_{i:00}";
                Assert.That(seen.ContainsKey(id), Is.True, $"{id} can never be found.");
                Assert.That(seen[id], Is.EqualTo(1), $"{id} is in {seen[id]} tiers.");
            }
        }

        [Test]
        public void EveryTier_IsReachableFromSomeTreeFamily()
        {
            var tags = new HashSet<string>(TreeProfiles().SelectMany(p => p.yieldTags ?? new string[0]));
            foreach (var tier in Skill.yieldTable.tiers)
            {
                if (string.IsNullOrEmpty(tier.requiredTag)) continue;
                Assert.That(tags.Contains(tier.requiredTag), Is.True,
                    $"'{tier.displayName}' needs tag '{tier.requiredTag}' and no tree carries it.");
            }
        }

        [Test]
        public void SkillFindsBetterWood_OnTheSameTree()
        {
            var table = Skill.yieldTable;
            var tags = new[] { "moss" };
            float Value(float skill)
            {
                var shares = new List<float>();
                table.Shares(skill, tags, shares);
                float v = 0f;
                for (int i = 0; i < shares.Count; i++)
                    v += shares[i] * (float)table.tiers[i].items.Average(it => it.buyPrice);
                return v;
            }

            Assert.That(Value(40f), Is.GreaterThan(Value(0f)));
            Assert.That(Value(80f), Is.GreaterThan(Value(40f)));
        }

        [Test]
        public void ATagGatedTier_NeverDropsFromTheWrongTree()
        {
            var table = Skill.yieldTable;
            var ember = table.tiers.First(t => t.requiredTag == "volcanic");
            Assert.That(GatheringYieldTable.WeightOf(ember, 100f, new[] { "moss" }), Is.Zero);
            Assert.That(GatheringYieldTable.WeightOf(ember, 100f, new[] { "volcanic" }), Is.GreaterThan(0f));
        }

        [Test]
        public void ALockedTier_HasNoWeight_UntilItsSkill()
        {
            var table = Skill.yieldTable;
            var tier = table.tiers.First(t => t.minSkill >= 30f && string.IsNullOrEmpty(t.requiredTag));
            Assert.That(GatheringYieldTable.WeightOf(tier, tier.minSkill - 0.1f, null), Is.Zero);
            Assert.That(GatheringYieldTable.WeightOf(tier, tier.minSkill, null), Is.GreaterThan(0f));
        }

        [Test]
        public void BetterWood_IsWorthMore()
        {
            var tiers = Skill.yieldTable.tiers.OrderBy(t => t.minSkill).ToList();
            float firstPrice = (float)tiers.First().items.Average(i => i.buyPrice);
            float lastPrice = (float)tiers.Last().items.Average(i => i.buyPrice);
            Assert.That(lastPrice, Is.GreaterThan(firstPrice * 20f));
        }

        // ── The trees ──────────────────────────────────────────────────────────────

        [Test]
        public void EveryTreeTemplate_IsWiredToItsFamilyProfile()
        {
            int checkedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingTemplateData"))
            {
                var t = AssetDatabase.LoadAssetAtPath<BuildingTemplateData>(AssetDatabase.GUIDToAssetPath(guid));
                if (t == null || t.destruction == null || !t.destruction.name.StartsWith("DP_tree_")) continue;

                var family = TreeFamilyClassifier.Classify(t.assetPath);
                Assert.That(family, Is.Not.EqualTo(TreeFamily.None), $"'{t.assetPath}' is wired as a tree and is not one.");
                Assert.That(t.destruction.name, Is.EqualTo(TreeFamilyClassifier.ProfileName(family)),
                    $"'{t.assetPath}' is a {family} tree wired to {t.destruction.name}.");
                checkedCount++;
            }
            Assert.That(checkedCount, Is.GreaterThan(500), "The tree wiring vanished.");
        }

        [Test]
        public void EveryTreeProfile_TeachesTheSkill_Regrows_AndFalls()
        {
            var profiles = TreeProfiles();
            Assert.That(profiles.Count, Is.EqualTo(10));

            foreach (var p in profiles)
            {
                Assert.That(p.UsesSkillYield, Is.True, $"{p.name} pays no skill yield.");
                Assert.That(p.regrowSeconds, Is.GreaterThan(0f), $"{p.name} never regrows: the forest would be finite.");
                Assert.That(p.kind, Is.EqualTo(DestructionKind.Fell), p.name);
                Assert.That(p.drops, Is.Null, $"{p.name} still pays a flat drop table beside the skill yield.");
                Assert.That(p.workPerYield, Is.GreaterThan(0), p.name);
            }
        }

        [Test]
        public void HarderFamilies_AreTougherToFell()
        {
            var profiles = TreeProfiles().OrderBy(p => p.skillDifficulty).ToList();
            Assert.That(profiles.Last().durability, Is.GreaterThan(profiles.First().durability * 3));
        }

        [Test]
        public void TheClassifier_KnowsTheStrongerStatement()
        {
            Assert.That(TreeFamilyClassifier.Classify("Buildings/nature/tree_ancient_swamp_guardian"), Is.EqualTo(TreeFamily.Ancient));
            Assert.That(TreeFamilyClassifier.Classify("Buildings/nature/tree_corrupted_enchanted_3"), Is.EqualTo(TreeFamily.Corrupted));
            Assert.That(TreeFamilyClassifier.Classify("Buildings/nature/tree_snowy_conifer_2"), Is.EqualTo(TreeFamily.Winter));
            Assert.That(TreeFamilyClassifier.Classify("Buildings/vegetation/tree_3"), Is.EqualTo(TreeFamily.Common));
            Assert.That(TreeFamilyClassifier.Classify("Buildings/nature/tree_stump_cut_rings"), Is.EqualTo(TreeFamily.None));
            Assert.That(TreeFamilyClassifier.Classify("Buildings/nature/log_fallen_timber"), Is.EqualTo(TreeFamily.None));
        }
    }
}
