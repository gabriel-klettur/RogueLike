using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="SkillTreeLayout"/>, which is the half of the talents board that CAN be
    /// proven in Edit Mode: uGUI lays nothing out there, so the geometry has to be pure or it is
    /// untestable — and the shipped panel's whole defect was that it threw this geometry away.
    /// </summary>
    [TestFixture]
    public class SkillTreeLayoutTests
    {
        private readonly List<SkillNodePlacement> _placements = new List<SkillNodePlacement>();
        private readonly List<SkillEdgePlacement> _edges = new List<SkillEdgePlacement>();
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        private SkillNode Node(string id, int row, int column, params SkillNode[] prereqs)
        {
            var n = ScriptableObject.CreateInstance<SkillNode>();
            n.skillId = id;
            n.displayName = id;
            n.row = row;
            n.column = column;
            n.prerequisites = prereqs ?? System.Array.Empty<SkillNode>();
            _spawned.Add(n);
            return n;
        }

        private SkillTree Tree(params SkillNode[] nodes)
        {
            var t = ScriptableObject.CreateInstance<SkillTree>();
            t.EditorSetNodes(nodes);
            _spawned.Add(t);
            return t;
        }

        /// <summary>The shape all five shipped trees have: three roots, three middles, a capstone.</summary>
        private SkillTree ShippedShape()
        {
            var r0 = Node("r0", 0, 0);
            var r1 = Node("r1", 0, 1);
            var r2 = Node("r2", 0, 2);
            var m0 = Node("m0", 1, 0, r0);
            var m1 = Node("m1", 1, 1, r1);
            var m2 = Node("m2", 1, 2, r2);
            var cap = Node("cap", 2, 1, m1);
            return Tree(r0, r1, r2, m0, m1, m2, cap);
        }

        // ── Placement ───────────────────────────────────────────────────────────

        [Test]
        public void EveryNode_IsPlacedExactlyOnce()
        {
            SkillTreeLayout.Build(ShippedShape(), _placements, _edges);
            Assert.AreEqual(7, _placements.Count);

            var seen = new HashSet<SkillNode>();
            foreach (var p in _placements)
                Assert.IsTrue(seen.Add(p.Node), "A node placed twice would draw two sockets.");
        }

        /// <summary>
        /// <c>SkillNode.row</c> documents itself as "lower is nearer the root", and a talent tree
        /// is read downward — so row 0 has to be at the TOP. Texel space is bottom-up, which is
        /// exactly the conversion that is easy to get backwards and impossible to see in code.
        /// </summary>
        [Test]
        public void RowZero_IsAtTheTop()
        {
            SkillTreeLayout.Build(ShippedShape(), _placements, _edges);

            int root = YOf("r1"), mid = YOf("m1"), cap = YOf("cap");
            Assert.Greater(root, mid, "A root must sit above the node it opens.");
            Assert.Greater(mid, cap, "The capstone must sit at the bottom of its branch.");
        }

        [Test]
        public void ColumnsAreOrdered_LeftToRight()
        {
            SkillTreeLayout.Build(ShippedShape(), _placements, _edges);
            Assert.Less(XOf("r0"), XOf("r1"));
            Assert.Less(XOf("r1"), XOf("r2"));
        }

        /// <summary>
        /// Two sockets that overlap is the failure the old list could not have — and the one a
        /// board invites. Checked on the real 3x3, where the columns are closest together.
        /// </summary>
        [Test]
        public void NoTwoSockets_Overlap()
        {
            SkillTreeLayout.Build(ShippedShape(), _placements, _edges);
            int size = SkillTreeLayout.NodeSize;

            for (int i = 0; i < _placements.Count; i++)
                for (int j = i + 1; j < _placements.Count; j++)
                {
                    var a = _placements[i];
                    var b = _placements[j];
                    bool apart = a.X + size <= b.X || b.X + size <= a.X ||
                                 a.Y + size <= b.Y || b.Y + size <= a.Y;
                    Assert.IsTrue(apart,
                        $"{a.Node.skillId} and {b.Node.skillId} overlap at ({a.X},{a.Y}) / ({b.X},{b.Y}).");
                }
        }

        /// <summary>
        /// The board sizes itself from the tree, so a class with a fourth column gets a wider
        /// window rather than a clipped one. The list this replaced could hold exactly seven
        /// rows and every shipped tree had exactly seven — one more and it fell off in silence.
        /// </summary>
        [Test]
        public void TheBoard_GrowsWithTheTree()
        {
            var narrow = SkillTreeLayout.Build(ShippedShape(), _placements, _edges);

            var wide = Tree(Node("a", 0, 0), Node("b", 0, 1), Node("c", 0, 2), Node("d", 0, 3));
            var wideSize = SkillTreeLayout.Build(wide, _placements, _edges);

            Assert.Greater(wideSize.x, narrow.x, "A fourth column must widen the board.");
        }

        [Test]
        public void EveryPlacement_IsOnAWholeTexel()
        {
            var size = SkillTreeLayout.Build(ShippedShape(), _placements, _edges);
            Assert.AreEqual(size.x, Mathf.RoundToInt(size.x));
            foreach (var p in _placements)
            {
                Assert.AreEqual(p.X, Mathf.RoundToInt(p.X));
                Assert.AreEqual(p.Y, Mathf.RoundToInt(p.Y));
            }
        }

        // ── Edges ───────────────────────────────────────────────────────────────

        [Test]
        public void EveryPrerequisite_GetsAnEdge()
        {
            SkillTreeLayout.Build(ShippedShape(), _placements, _edges);
            Assert.AreEqual(4, _edges.Count,
                "Three middles with one prerequisite each, plus the capstone.");
        }

        /// <summary>
        /// An edge runs from the node UP to its prerequisite. It is the one drawing in the window
        /// that says what a capstone actually costs, which is the question the old list could not
        /// answer at all.
        /// </summary>
        [Test]
        public void AnEdge_RunsUpwardFromTheChild()
        {
            SkillTreeLayout.Build(ShippedShape(), _placements, _edges);
            foreach (var e in _edges)
            {
                Assert.Greater(e.Header.yMax, e.Riser.yMin,
                    $"Edge {e.To.skillId} -> {e.From.skillId} must climb.");
                Assert.Greater(e.Riser.height, 0);
            }
        }

        [Test]
        public void AVerticalEdge_HasNoSidewaysRun()
        {
            // m1 sits directly under r1, so the elbow is a straight line.
            SkillTreeLayout.Build(ShippedShape(), _placements, _edges);
            foreach (var e in _edges)
            {
                if (e.To.skillId != "m1") continue;
                Assert.AreEqual(SkillTreeLayout.EdgeThickness, e.Cross.width,
                    "Two nodes in the same column must not draw a crossbar.");
                return;
            }
            Assert.Fail("Expected an edge into m1.");
        }

        [Test]
        public void AnEmptyTree_PlacesNothing()
        {
            var size = SkillTreeLayout.Build(Tree(), _placements, _edges);
            Assert.AreEqual(Vector2Int.zero, size);
            Assert.AreEqual(0, _placements.Count);
            Assert.AreEqual(0, _edges.Count);
        }

        [Test]
        public void ANullTree_IsSafe()
        {
            var size = SkillTreeLayout.Build(null, _placements, _edges);
            Assert.AreEqual(Vector2Int.zero, size);
        }

        private int XOf(string id)
        {
            foreach (var p in _placements) if (p.Node.skillId == id) return p.X;
            Assert.Fail("No placement for " + id);
            return 0;
        }

        private int YOf(string id)
        {
            foreach (var p in _placements) if (p.Node.skillId == id) return p.Y;
            Assert.Fail("No placement for " + id);
            return 0;
        }
    }
}
