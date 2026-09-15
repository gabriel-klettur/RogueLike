using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.HUD;
using Valkur.Tests.Support;

namespace Valkur.Tests.EditMode.Gameplay.Player
{
    /// <summary>
    /// Reads the five shipped talent trees off disk and holds them to what the BOARD now needs.
    ///
    /// <para>The old panel was a sorted list, so it could survive data a board cannot: two nodes
    /// on the same grid cell drew one under the other in a list and draw ON TOP OF each other on a
    /// board. A prerequisite pointing outside its own tree was invisible in a list and is an edge
    /// to nowhere on a board. Neither fails loudly — they are silent, exactly like the coordinate
    /// drift and the spawner roster this project already records — so they are pinned against the
    /// shipped bytes rather than against a fixture's own synthetic tree.</para>
    ///
    /// <para><b>The vacuity guard is the load-bearing line.</b> <c>FindAssets("t:SkillTree")</c>
    /// returned ZERO for a whole day of this project's life while every asset sat correct on disk,
    /// because the script-to-class binding had come loose (see CLAUDE.md). A fixture that walks an
    /// empty list is green and proves nothing, so the count is asserted first and the search falls
    /// back to the folder when the type lookup comes up empty.</para>
    /// </summary>
    [TestFixture]
    [Category(TestCategories.ShippedData)]
    public class ShippedSkillTreeDataTests
    {
        private const string TreeFolder = "Assets/_Project/Data/Progression/SkillTrees";
        private const int ExpectedTrees = 5;

        private static List<SkillTree> LoadShippedTrees()
        {
            var trees = new List<SkillTree>();

            foreach (var guid in AssetDatabase.FindAssets("t:SkillTree", new[] { TreeFolder }))
            {
                var tree = AssetDatabase.LoadAssetAtPath<SkillTree>(AssetDatabase.GUIDToAssetPath(guid));
                if (tree != null) trees.Add(tree);
            }
            if (trees.Count > 0) return trees;

            // The type lookup found nothing. Either the binding is broken or the filter is wrong;
            // walk the folder so the test reports WHICH, instead of passing on an empty list.
            string abs = Path.GetFullPath(TreeFolder);
            if (!Directory.Exists(abs)) return trees;
            foreach (var file in Directory.GetFiles(abs, "*_skill_tree.asset", SearchOption.AllDirectories))
            {
                string rel = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                var tree = AssetDatabase.LoadAssetAtPath<SkillTree>(rel);
                if (tree != null) trees.Add(tree);
            }
            return trees;
        }

        [Test]
        public void TheFiveTrees_AreAllLoadable()
        {
            var trees = LoadShippedTrees();
            Assert.AreEqual(ExpectedTrees, trees.Count,
                "One tree per playable class. A tree that loads as null is the script-binding " +
                "failure CLAUDE.md records, not a missing file — check GetClass() before the YAML.");
            foreach (var t in trees)
                Assert.Greater(t.Count, 0, t.name + " has no nodes.");
        }

        /// <summary>
        /// The invariant the board introduced. A list could not care; a board draws two nodes that
        /// share a cell one on top of the other, and the one underneath is unclickable.
        /// </summary>
        [Test]
        public void NoTwoNodes_ShareAGridCell()
        {
            var trees = LoadShippedTrees();
            Assert.Greater(trees.Count, 0);

            int inspected = 0;
            foreach (var tree in trees)
            {
                var taken = new Dictionary<Vector2Int, string>();
                foreach (var node in tree.Nodes)
                {
                    if (node == null) continue;
                    inspected++;
                    var cell = new Vector2Int(node.column, node.row);
                    Assert.IsFalse(taken.ContainsKey(cell),
                        $"{tree.name}: '{node.displayName}' and '{(taken.ContainsKey(cell) ? taken[cell] : "?")}' " +
                        $"both sit at row {node.row}, column {node.column}.");
                    taken[cell] = node.displayName;
                }
            }
            Assert.Greater(inspected, 0, "No node was inspected — the fixture is vacuous.");
        }

        /// <summary>
        /// An edge is drawn between two placements, so a prerequisite in another tree has no
        /// placement to reach and the node it gates is unreachable with nothing on screen saying so.
        /// </summary>
        [Test]
        public void EveryPrerequisite_LivesInItsOwnTree()
        {
            var trees = LoadShippedTrees();
            int checked_ = 0;

            foreach (var tree in trees)
            {
                var members = new HashSet<SkillNode>();
                foreach (var n in tree.Nodes) if (n != null) members.Add(n);

                foreach (var node in tree.Nodes)
                {
                    if (node == null || node.prerequisites == null) continue;
                    foreach (var prereq in node.prerequisites)
                    {
                        if (prereq == null) continue;
                        checked_++;
                        Assert.IsTrue(members.Contains(prereq),
                            $"{tree.name}: '{node.displayName}' requires '{prereq.displayName}', " +
                            "which is not in this tree.");
                    }
                }
            }
            Assert.Greater(checked_, 0, "No prerequisite was inspected — the fixture is vacuous.");
        }

        /// <summary>A prerequisite must be ABOVE the node it gates, or the elbow is drawn upside down.</summary>
        [Test]
        public void EveryPrerequisite_SitsOnAnEarlierRow()
        {
            foreach (var tree in LoadShippedTrees())
                foreach (var node in tree.Nodes)
                {
                    if (node == null || node.prerequisites == null) continue;
                    foreach (var prereq in node.prerequisites)
                    {
                        if (prereq == null) continue;
                        Assert.Less(prereq.row, node.row,
                            $"{tree.name}: '{prereq.displayName}' (row {prereq.row}) gates " +
                            $"'{node.displayName}' (row {node.row}) but is not above it.");
                    }
                }
        }

        /// <summary>
        /// Every node must DO something. A node with no modifier and no aura is a point the player
        /// spends on nothing, and the card would show it an empty effect line.
        /// </summary>
        [Test]
        public void EveryNode_HasAnEffect()
        {
            int inspected = 0;
            foreach (var tree in LoadShippedTrees())
                foreach (var node in tree.Nodes)
                {
                    if (node == null) continue;
                    inspected++;
                    bool mods = node.modifiersPerRank != null && node.modifiersPerRank.Length > 0;
                    bool auras = node.passiveAuras != null && node.passiveAuras.Length > 0;
                    Assert.IsTrue(mods || auras,
                        $"{tree.name}: '{node.displayName}' costs points and changes nothing.");
                }
            Assert.AreEqual(35, inspected, "Seven nodes per class, five classes.");
        }

        [Test]
        public void EveryNode_HasAStableId_UniqueInItsTree()
        {
            foreach (var tree in LoadShippedTrees())
            {
                var ids = new HashSet<string>();
                foreach (var node in tree.Nodes)
                {
                    if (node == null) continue;
                    Assert.IsFalse(string.IsNullOrWhiteSpace(node.skillId),
                        tree.name + ": a node with no id cannot be saved.");
                    Assert.IsTrue(ids.Add(node.skillId),
                        $"{tree.name}: duplicate skillId '{node.skillId}' — one save slot, two nodes.");
                }
            }
        }

        /// <summary>
        /// The whole tree has to be reachable from a root. A node whose prerequisite chain never
        /// bottoms out is content nobody can buy, and the board would draw it greyed forever.
        /// </summary>
        [Test]
        public void EveryNode_IsReachableFromARoot()
        {
            foreach (var tree in LoadShippedTrees())
                foreach (var node in tree.Nodes)
                {
                    if (node == null) continue;
                    var seen = new HashSet<SkillNode>();
                    Assert.IsTrue(ReachesRoot(node, seen),
                        $"{tree.name}: '{node.displayName}' has no path to a root node.");
                }
        }

        private static bool ReachesRoot(SkillNode node, HashSet<SkillNode> seen)
        {
            if (node == null || !seen.Add(node)) return false;
            if (node.prerequisites == null || node.prerequisites.Length == 0) return true;
            foreach (var p in node.prerequisites)
                if (p != null && ReachesRoot(p, seen)) return true;
            return false;
        }

        /// <summary>
        /// The board sizes its window from the tree, so a shipped tree has to produce a board that
        /// fits the screen the game runs at. Asserted through the real layout, not through a copy
        /// of its arithmetic.
        /// </summary>
        [Test]
        public void EveryTree_FitsTheWindowAtTheReferenceResolution()
        {
            var placements = new List<SkillNodePlacement>();
            var edges = new List<SkillEdgePlacement>();
            var style = SkillsHudStyle.Active;

            foreach (var tree in LoadShippedTrees())
            {
                var board = SkillTreeLayout.Build(tree, placements, edges);
                int width = style.paddingTexels * 2 + board.x + style.cardGapTexels + style.cardWidthTexels;
                int height = style.paddingTexels * 2 + style.titleBarTexels + style.flavourTexels +
                             board.y + style.footerTexels;

                // At the 1600x800 reference the grid is 2 screen pixels per texel.
                Assert.LessOrEqual(width * 2, 1600, tree.name + " is wider than the screen.");
                Assert.LessOrEqual(height * 2, 800, tree.name + " is taller than the screen.");
                Assert.AreEqual(tree.Count, placements.Count,
                    tree.name + ": every node must reach the board.");
            }
        }
    }
}
