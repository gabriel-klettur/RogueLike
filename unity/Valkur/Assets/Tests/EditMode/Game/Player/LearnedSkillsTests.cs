using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Pins <see cref="LearnedSkills"/>: gating rules (points / level /
    /// prerequisites), idempotent learning, save/load roundtrip,
    /// and the unknown-id pruning that lets a save survive a tree edit.
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

        // ── Behaviours ──────────────────────────────────────────────────────────

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
        public void TryLearn_AlreadyLearned_FailsAndDoesNotChargeAgain()
        {
            var n = MakeNode("once", cost: 1);
            var tree = MakeTree(n);
            var (go, skills) = MakeLearner(tree, points: 5);
            try
            {
                Assert.IsTrue(skills.TryLearn(n, 1, out _));
                Assert.AreEqual(4, skills.AvailablePoints);

                Assert.IsFalse(skills.TryLearn(n, 1, out string reason),
                    "Re-learning the same node must fail.");
                StringAssert.Contains("learned", reason);
                Assert.AreEqual(4, skills.AvailablePoints,
                    "Re-learn attempt must NOT double-charge.");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(tree); Object.DestroyImmediate(n); }
        }

        [Test]
        public void Snapshot_Roundtrip_PreservesLearnedAndPoints()
        {
            var a = MakeNode("a");
            var b = MakeNode("b");
            var tree = MakeTree(a, b);
            var (go, skills) = MakeLearner(tree, points: 5);
            try
            {
                skills.TryLearn(a, 1, out _);
                skills.TryLearn(b, 1, out _);

                var snap = skills.ToSnapshot();
                Assert.AreEqual(2, snap.learned.Count);
                Assert.AreEqual(3, snap.availablePoints);

                // Reset state and reload.
                var (go2, skills2) = MakeLearner(tree, points: 0);
                try
                {
                    skills2.FromSnapshot(snap);
                    Assert.IsTrue(skills2.IsLearned("a"));
                    Assert.IsTrue(skills2.IsLearned("b"));
                    Assert.AreEqual(3, skills2.AvailablePoints);
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
        public void FromSnapshot_DropsUnknownIds_DoesNotCrash()
        {
            // Simulate a tree edit: the save mentions a node that no longer
            // exists. LearnedSkills must skip it with a warning instead of
            // crashing the load.
            var existing = MakeNode("exists");
            var tree = MakeTree(existing);
            var (go, skills) = MakeLearner(tree, points: 0);
            try
            {
                var snap = new LearnedSkills.Snapshot
                {
                    availablePoints = 1,
                    learned = new System.Collections.Generic.List<string>
                        { "exists", "ghost_pruned_skill" },
                };
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex("ghost_pruned_skill"));
                skills.FromSnapshot(snap);

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
