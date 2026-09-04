using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Pins <see cref="LearnedSkills"/>: gating rules (points / level / prerequisites),
    /// ranks, respec, the save roundtrip, and the unknown-id pruning that lets a save
    /// survive a tree edit.
    /// </summary>
    [TestFixture]
    public class LearnedSkillsTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        private static SkillNode MakeNode(string id, int cost = 1, int levelReq = 0,
                                           params SkillNode[] prereqs)
        {
            var n = ScriptableObject.CreateInstance<SkillNode>();
            n.skillId = id;
            n.displayName = id;
            n.pointCost = cost;
            n.maxRank = 1;
            n.levelRequirement = levelReq;
            n.prerequisites = prereqs ?? System.Array.Empty<SkillNode>();
            return n;
        }

        private static SkillTree MakeTree(params SkillNode[] nodes)
        {
            var t = ScriptableObject.CreateInstance<SkillTree>();
            t.EditorSetNodes(nodes);
            return t;
        }

        private static (GameObject go, LearnedSkills skills) MakeLearner(SkillTree tree, int points)
        {
            var go = new GameObject("Player");
            var skills = go.AddComponent<LearnedSkills>();
            skills.SetTree(tree);
            skills.AddPoints(points);
            return (go, skills);
        }

        // ── Gating ──────────────────────────────────────────────────────────────

        [Test]
        public void CanLearn_RootNode_WithEnoughPoints_Succeeds()
        {
            var root = MakeNode("strength_1", cost: 1);
            var tree = MakeTree(root);
            var (go, skills) = MakeLearner(tree, points: 1);
            try
            {
                bool ok = skills.TryLearn(root, playerLevel: 1, out string reason);
                Assert.IsTrue(ok, $"TryLearn must succeed for root with sufficient points; reason='{reason}'.");
                Assert.IsTrue(skills.IsLearned("strength_1"));
                Assert.AreEqual(0, skills.AvailablePoints, "Cost must be deducted on success.");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(root); }
        }

        [Test]
        public void CanLearn_InsufficientPoints_FailsWithReason()
        {
            var n = MakeNode("expensive", cost: 5);
            var tree = MakeTree(n);
            var (go, skills) = MakeLearner(tree, points: 2);
            try
            {
                bool ok = skills.TryLearn(n, playerLevel: 1, out string reason);
                Assert.IsFalse(ok);
                StringAssert.Contains("skill point", reason);
                Assert.AreEqual(2, skills.AvailablePoints,
                    "Failed TryLearn must NOT deduct points.");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(n); }
        }

        [Test]
        public void CanLearn_LevelGate_BlocksUntilLevelMet()
        {
            var n = MakeNode("ultimate", cost: 1, levelReq: 10);
            var tree = MakeTree(n);
            var (go, skills) = MakeLearner(tree, points: 1);
            try
            {
                Assert.IsFalse(skills.TryLearn(n, playerLevel: 5, out string r5));
                StringAssert.Contains("level 10", r5);

                Assert.IsTrue(skills.TryLearn(n, playerLevel: 10, out _),
                    "Level gate must clear at exact threshold.");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(n); }
        }

        [Test]
        public void CanLearn_LevelPerRank_PacesLaterRanksAcrossTheCurve()
        {
            // A capstone that can be maxed the moment it opens is a capstone with no pacing.
            var n = MakeNode("paced", cost: 1, levelReq: 10);
            n.maxRank = 3;
            n.levelPerRank = 5;
            var tree = MakeTree(n);
            var (go, skills) = MakeLearner(tree, points: 5);
            try
            {
                Assert.IsTrue(skills.TryLearn(n, 10, out _), "Rank 1 opens at the base level.");
                Assert.IsFalse(skills.CanLearn(n, 10, out string blocked), "Rank 2 must not.");
                StringAssert.Contains("level 15", blocked);
                Assert.IsTrue(skills.TryLearn(n, 15, out _));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(n); }
        }

        [Test]
        public void CanLearn_Prerequisite_BlockedUntilParentLearned()
        {
            var parent = MakeNode("parent");
            var child  = MakeNode("child", cost: 1, levelReq: 0, parent);
            var tree = MakeTree(parent, child);
            var (go, skills) = MakeLearner(tree, points: 2);
            try
            {
                Assert.IsFalse(skills.TryLearn(child, 1, out string blocked));
                StringAssert.Contains("Requires", blocked);

                Assert.IsTrue(skills.TryLearn(parent, 1, out _));
                Assert.IsTrue(skills.TryLearn(child, 1, out _),
                    "Once parent is learned, child must unlock.");
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree);
                Object.DestroyImmediate(parent); Object.DestroyImmediate(child);
            }
        }

        [Test]
        public void CanLearn_RequiresThePrerequisiteAtFullRank()
        {
            // A partial prerequisite would let a player reach a capstone with one point in
            // every node on the way to it, which makes the tree's shape decorative.
            var root = MakeNode("root");
            root.maxRank = 3;
            var capstone = MakeNode("capstone", cost: 1, levelReq: 0, root);
            var tree = MakeTree(root, capstone);
            var (go, skills) = MakeLearner(tree, points: 10);
            try
            {
                skills.TryLearn(root, 1, out _);
                Assert.IsFalse(skills.CanLearn(capstone, 1, out string reason),
                    "One rank of a three-rank prerequisite must not open the node behind it.");
                StringAssert.Contains("max rank", reason);

                skills.TryLearn(root, 1, out _);
                skills.TryLearn(root, 1, out _);
                Assert.IsTrue(skills.CanLearn(capstone, 1, out _));
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree);
                Object.DestroyImmediate(root); Object.DestroyImmediate(capstone);
            }
        }

        [Test]
        public void TryLearn_AtMaxRank_FailsAndDoesNotChargeAgain()
        {
            var n = MakeNode("once", cost: 1);
            var tree = MakeTree(n);
            var (go, skills) = MakeLearner(tree, points: 5);
            try
            {
                Assert.IsTrue(skills.TryLearn(n, 1, out _));
                Assert.AreEqual(4, skills.AvailablePoints);

                Assert.IsFalse(skills.TryLearn(n, 1, out string reason),
                    "Re-learning a single-rank node must fail.");
                StringAssert.Contains("max rank", reason);
                Assert.AreEqual(4, skills.AvailablePoints,
                    "Re-learn attempt must NOT double-charge.");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(n); }
        }

        // ── Ranks ───────────────────────────────────────────────────────────────

        [Test]
        public void ModifiersAtRank_ScaleWithTheRankHeld()
        {
            var node = MakeNode("hp");
            node.maxRank = 5;
            node.modifiersPerRank = new[] { StatModifier.Flat(StatKind.MaxHp, 12f) };
            try
            {
                Assert.AreEqual(0, node.ModifiersAtRank(0).Length,
                    "Rank 0 contributes nothing at all, not a row of zeroes.");
                Assert.AreEqual(12f, node.ModifiersAtRank(1)[0].value, 0.001f);
                Assert.AreEqual(60f, node.ModifiersAtRank(5)[0].value, 0.001f,
                    "One authored row has to describe every step of the node.");
            }
            finally { Object.DestroyImmediate(node); }
        }

        // ── Respec ──────────────────────────────────────────────────────────────

        [Test]
        public void Respec_RefundsExactlyWhatWasSpent()
        {
            var a = MakeNode("a", cost: 2);
            var tree = MakeTree(a);
            var (go, skills) = MakeLearner(tree, points: 5);
            try
            {
                skills.TryLearn(a, 1, out _);
                Assert.AreEqual(3, skills.AvailablePoints);

                skills.Respec();

                Assert.AreEqual(5, skills.AvailablePoints, "Every spent point comes back.");
                Assert.AreEqual(0, skills.SpentPoints);
                Assert.AreEqual(0, skills.RankOf("a"), "And the rank is forgotten.");
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(a);
            }
        }

        [Test]
        public void Respec_RefundsPointsSunkIntoANodeThatNoLongerExists()
        {
            // The spent total is tracked rather than recomputed from the tree precisely so
            // a pruned node cannot eat the points a live save put into it.
            var ghost = MakeNode("ghost", cost: 3);
            var tree = MakeTree(ghost);
            var (go, skills) = MakeLearner(tree, points: 3);
            try
            {
                skills.TryLearn(ghost, 1, out _);
                Assert.AreEqual(0, skills.AvailablePoints);

                tree.EditorSetNodes(System.Array.Empty<SkillNode>());

                skills.Respec();
                Assert.AreEqual(3, skills.AvailablePoints,
                    "Points sunk into a pruned node must still be refundable.");
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(ghost);
            }
        }

        // ── Persistence ─────────────────────────────────────────────────────────

        [Test]
        public void SaveRoundtrip_PreservesRanksAndBothPointBalances()
        {
            var a = MakeNode("a");
            var b = MakeNode("b");
            b.maxRank = 3;
            var tree = MakeTree(a, b);
            var (go, skills) = MakeLearner(tree, points: 5);
            try
            {
                skills.TryLearn(a, 1, out _);
                skills.TryLearn(b, 1, out _);
                skills.TryLearn(b, 1, out _);   // b to rank 2

                var data = new ProgressionSaveData();
                skills.WriteTo(data);
                Assert.AreEqual(2, data.skillIds.Count, "Two distinct nodes held.");
                Assert.AreEqual(2, data.skillPoints, "Three of five points spent.");
                Assert.AreEqual(3, data.skillPointsSpent);

                var (go2, skills2) = MakeLearner(tree, points: 0);
                try
                {
                    skills2.ReadFrom(data);
                    Assert.AreEqual(1, skills2.RankOf("a"));
                    Assert.AreEqual(2, skills2.RankOf("b"),
                        "A rank is state a set of ids cannot carry — it must survive the save.");
                    Assert.AreEqual(2, skills2.AvailablePoints);
                    Assert.AreEqual(3, skills2.SpentPoints);
                }
                finally { Object.DestroyImmediate(go2); }
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree);
                Object.DestroyImmediate(a); Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void ReadFrom_SaveWithoutRankList_RestoresEveryIdAtRankOne()
        {
            // A save written before ranks existed carries ids and no ranks. Reading those
            // as rank 0 would silently delete the player's whole build on the first load
            // after the update, which is the one outcome that cannot be recovered from.
            var a = MakeNode("a");
            var tree = MakeTree(a);
            var (go, skills) = MakeLearner(tree, points: 0);
            try
            {
                skills.ReadFrom(new ProgressionSaveData
                {
                    skillIds = new System.Collections.Generic.List<string> { "a" },
                    skillRanks = null,
                    skillPoints = 2,
                });

                Assert.AreEqual(1, skills.RankOf("a"));
                Assert.AreEqual(2, skills.AvailablePoints);
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(a);
            }
        }

        [Test]
        public void ReadFrom_ClampsRankToTheTreesCurrentMaximum()
        {
            var a = MakeNode("a");
            a.maxRank = 2;
            var tree = MakeTree(a);
            var (go, skills) = MakeLearner(tree, points: 0);
            try
            {
                skills.ReadFrom(new ProgressionSaveData
                {
                    skillIds = new System.Collections.Generic.List<string> { "a" },
                    skillRanks = new System.Collections.Generic.List<int> { 5 },
                });

                Assert.AreEqual(2, skills.RankOf("a"),
                    "A designer who lowers maxRank must not leave live saves above it.");
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(a);
            }
        }

        [Test]
        public void ReadFrom_DropsUnknownIds_DoesNotCrash()
        {
            var existing = MakeNode("exists");
            var tree = MakeTree(existing);
            var (go, skills) = MakeLearner(tree, points: 0);
            try
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex("ghost_pruned_skill"));

                skills.ReadFrom(new ProgressionSaveData
                {
                    skillPoints = 1,
                    skillIds = new System.Collections.Generic.List<string>
                        { "exists", "ghost_pruned_skill" },
                    skillRanks = new System.Collections.Generic.List<int> { 1, 1 },
                });

                Assert.IsTrue(skills.IsLearned("exists"));
                Assert.IsFalse(skills.IsLearned("ghost_pruned_skill"),
                    "Unknown ids must be dropped — saves outliving tree edits.");
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(existing);
            }
        }
    }
}
