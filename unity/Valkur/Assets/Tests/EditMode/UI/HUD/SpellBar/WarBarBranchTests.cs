using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.SpellBar
{
    /// <summary>
    /// The "Barra de Guerra" talent branch: the War action bar's size is EARNED.
    ///
    /// <para>Three halves have to agree for a size bought on the talents board to appear on the
    /// bar, and each can be right alone: the branch has to be a tree every class is handed
    /// (<see cref="LearnedSkills.SetBranches"/>), its ranks have to reach the stat layer through
    /// the SAME lookup the class path uses (<see cref="LearnedSkills.TryFindNode"/>), and the bar
    /// has to size itself from the stats (<see cref="SpellBarModel.Fit"/>). These pin the
    /// composition and the shipped numbers.</para>
    /// </summary>
    [TestFixture]
    public class WarBarBranchTests
    {
        private readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private SkillNode Node(string id, StatKind stat, params SkillNode[] prereqs)
        {
            var n = ScriptableObject.CreateInstance<SkillNode>();
            n.skillId = id; n.displayName = id; n.pointCost = 1; n.maxRank = 1;
            n.prerequisites = prereqs;
            n.modifiersPerRank = new[] { StatModifier.Flat(stat, 1f) };
            _made.Add(n);
            return n;
        }

        private SkillTree Tree(params SkillNode[] nodes)
        {
            var t = ScriptableObject.CreateInstance<SkillTree>();
            t.EditorSetNodes(nodes);
            _made.Add(t);
            return t;
        }

        // ── Composition ─────────────────────────────────────────────────────────

        [Test]
        public void ABranchTalent_IsBuyable_AndReachesTheStatLayer_BesideTheClassTree()
        {
            var classNode = Node("class_hp", StatKind.MaxHp);
            var col1 = Node("cols_1", StatKind.WarBarColumns);
            var col2 = Node("cols_2", StatKind.WarBarColumns, col1);

            var go = new GameObject("Learner");
            _made.Add(go);
            var skills = go.AddComponent<LearnedSkills>();
            skills.SetTree(Tree(classNode));
            skills.SetBranches(new[] { Tree(col1, col2) });
            skills.AddPoints(3);

            Assert.IsTrue(skills.TryFindNode("cols_2", out _), "A branch node must be found by the shared lookup.");
            Assert.IsTrue(skills.TryLearn(col1, 1, out var r1), r1);
            Assert.IsTrue(skills.TryLearn(col2, 1, out var r2), r2);

            var mods = new List<StatModifier>();
            skills.CollectModifiers(mods);
            Assert.AreEqual(2f, mods.Where(m => m.stat == StatKind.WarBarColumns).Sum(m => m.value),
                "Both ranks of the branch must reach the Skill layer, or the bar never grows.");
        }

        [Test]
        public void ASave_KeepsBranchRanks()
        {
            var col1 = Node("cols_1", StatKind.WarBarColumns);
            var go = new GameObject("Learner");
            _made.Add(go);
            var skills = go.AddComponent<LearnedSkills>();
            skills.SetTree(Tree(Node("class_hp", StatKind.MaxHp)));
            skills.SetBranches(new[] { Tree(col1) });
            skills.AddPoints(1);
            Assert.IsTrue(skills.TryLearn(col1, 1, out _));

            var data = new ProgressionSaveData();
            skills.WriteTo(data);
            skills.Respec();
            skills.ReadFrom(data);

            Assert.AreEqual(1, skills.RankOf("cols_1"),
                "A branch rank read back from a save must not be pruned as an unknown id.");
        }

        // ── The bar ─────────────────────────────────────────────────────────────

        [Test]
        public void Fit_FillsTheEarnedSockets_CountsTheOverflow_AndKeepsTheSwitchLast()
        {
            var page = new List<SpellBarEntry>
            {
                new SpellBarEntry(SpellBarEntryKind.Spell, "a", "", 0),
                new SpellBarEntry(SpellBarEntryKind.Spell, "b", "", 0),
                new SpellBarEntry(SpellBarEntryKind.Stance, SpellBarModel.StanceKey, "", SpellBarModel.StanceGroup),
            };

            var roomy = SpellBarModel.Fit(page, 5, 5, out int none);
            Assert.AreEqual(0, none);
            Assert.AreEqual(6, roomy.Count, "Five sockets and the switch.");
            Assert.AreEqual(3, roomy.Count(e => e.Kind == SpellBarEntryKind.Empty),
                "Room the talents bought and nothing fills is DRAWN — a size nobody can see reads as a talent that did nothing.");
            Assert.AreEqual(SpellBarEntryKind.Stance, roomy[roomy.Count - 1].Kind);

            var tight = SpellBarModel.Fit(page, 1, 5, out int over);
            Assert.AreEqual(1, over);
            Assert.AreEqual(new[] { "a", SpellBarModel.StanceKey }, tight.Select(e => e.Key).ToArray(),
                "The switch never counts against the capacity: it must stay in reach.");
        }

        // ── The shipped branch ──────────────────────────────────────────────────

        private static ProgressionCatalog Catalog() =>
            Resources.Load<ProgressionCatalog>(ProgressionCatalog.ResourcePath);

        private static SkillTree ShippedBranch()
        {
            var catalog = Catalog();
            Assert.IsNotNull(catalog);
            Assert.IsNotNull(catalog.sharedSkillTrees);
            var branch = catalog.sharedSkillTrees.FirstOrDefault(t => t != null && t.displayName == "Barra de Guerra");
            Assert.IsNotNull(branch, "ProgressionCatalog.sharedSkillTrees must carry the 'Barra de Guerra' branch.");
            return branch;
        }

        [Test]
        public void TheShippedBranch_IsTwoChainsOfFive_ThatGrowTheBarToItsMaximum()
        {
            var branch = ShippedBranch();
            float columns = StatCatalog.WarBarBaseColumns, rows = StatCatalog.WarBarBaseRows;
            int columnNodes = 0, rowNodes = 0;

            foreach (var node in branch.Nodes)
            {
                Assert.IsNotNull(node);
                foreach (var m in node.ModifiersAtRank(node.maxRank))
                {
                    if (m.stat == StatKind.WarBarColumns) { columns += m.value; columnNodes++; }
                    if (m.stat == StatKind.WarBarRows) { rows += m.value; rowNodes++; }
                }
            }

            Assert.AreEqual(5, columnNodes, "Five sizes for the columns tree.");
            Assert.AreEqual(5, rowNodes, "Five sizes for the rows tree.");
            Assert.AreEqual(StatCatalog.WarBarMaxColumns, columns,
                "The whole columns tree must land exactly on the bar's widest row.");
            Assert.AreEqual(StatCatalog.WarBarMaxRows, rows,
                "The whole rows tree must land exactly on the bar's tallest size.");
        }

        [Test]
        public void EachTree_IsAChain_WhoseRanksOpenInOrder()
        {
            var branch = ShippedBranch();
            foreach (var stat in new[] { StatKind.WarBarColumns, StatKind.WarBarRows })
            {
                var chain = branch.Nodes.Where(n => n.modifiersPerRank.Any(m => m.stat == stat))
                                        .OrderBy(n => n.column).ToList();
                for (int i = 0; i < chain.Count; i++)
                {
                    int expected = i == 0 ? 0 : 1;
                    Assert.AreEqual(expected, chain[i].prerequisites.Length, chain[i].skillId);
                    if (i > 0) Assert.AreSame(chain[i - 1], chain[i].prerequisites[0],
                        $"{chain[i].skillId} must open after {chain[i - 1].skillId}.");
                    if (i > 0) Assert.GreaterOrEqual(chain[i].levelRequirement, chain[i - 1].levelRequirement);
                }
            }
        }

        [Test]
        public void TheBranchBoard_FitsTheTalentsWindow_At1080p()
        {
            // 1080p draws the HUD at three screen pixels a texel: 640 x 360 texels for the whole
            // window. A branch shaped as a crown over two chains needed six rows and did not fit.
            var style = SkillsHudStyle.Active;
            var size = SkillTreeLayout.Build(ShippedBranch(), null, null);
            int width = style.paddingTexels * 2 + Mathf.Max(288, size.x) + style.cardGapTexels + style.cardWidthTexels;
            int height = style.paddingTexels * 2 + style.titleBarTexels + style.flavourTexels +
                         Mathf.Max(150, size.y) + style.footerTexels;
            Assert.LessOrEqual(width, 640, "Too wide for 1080p.");
            Assert.LessOrEqual(height, 360, "Too tall for 1080p.");
        }

        [Test]
        public void ATrack_DrawsOneHorizontalLineBetweenNeighbours()
        {
            var a = Node("a", StatKind.WarBarColumns);
            var b = Node("b", StatKind.WarBarColumns, a);
            a.row = 0; a.column = 0; b.row = 0; b.column = 1;

            var placements = new List<SkillNodePlacement>();
            var edges = new List<SkillEdgePlacement>();
            SkillTreeLayout.Build(Tree(a, b), placements, edges);

            Assert.AreEqual(1, edges.Count, "Two neighbours on one row are joined, not skipped.");
            Assert.Greater(edges[0].Cross.width, 0);
            Assert.AreEqual(0, edges[0].Riser.height);
            Assert.AreEqual(0, edges[0].Header.height);
        }
    }
}
