using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="SkillTreeHUD"/>: ComputeListText reflects the
    /// bound LearnedSkills + tree state with Available / Locked /
    /// Learned status per node, Open/Close toggle is honoured, and
    /// rebinding to a different LearnedSkills swaps the displayed tree.
    /// </summary>
    [TestFixture]
    public class SkillTreeHUDTests
    {
        private GameObject _hudGo;
        private SkillTreeHUD _hud;

        [SetUp]
        public void SetUp()
        {
            _hudGo = new GameObject("SkillTreeHUD");
            _hud = _hudGo.AddComponent<SkillTreeHUD>();
            _hud.EnsureBuilt();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
        }

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
        public void NoBoundSkills_ProducesEmptyText()
        {
            Assert.AreEqual(string.Empty, _hud.ComputeListText());
        }

        [Test]
        public void Open_TogglesIsOpenFlag()
        {
            Assert.IsFalse(_hud.IsOpen);
            _hud.Open();
            Assert.IsTrue(_hud.IsOpen);
            _hud.Close();
            Assert.IsFalse(_hud.IsOpen);
        }

        [Test]
        public void ListText_StatusReflectsAvailability()
        {
            var avail = MakeNode("affordable", cost: 1);
            var locked = MakeNode("requires_5", cost: 1, levelReq: 5);
            var tree = MakeTree(avail, locked);
            var (go, skills) = MakeLearner(tree, points: 1);
            try
            {
                _hud.BindLearnedSkills(skills, level: 1);

                string text = _hud.ComputeListText();
                StringAssert.Contains("affordable", text);
                StringAssert.Contains("Available", text,
                    "Affordable + level-met node must show 'Available'.");
                StringAssert.Contains("requires_5", text);
                StringAssert.Contains("Locked", text,
                    "Level-gated node must show 'Locked: ...' status.");
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree);
                Object.DestroyImmediate(avail); Object.DestroyImmediate(locked);
            }
        }

        [Test]
        public void ListText_LearnedNode_ShowsCheckmark()
        {
            var node = MakeNode("strength", cost: 1);
            var tree = MakeTree(node);
            var (go, skills) = MakeLearner(tree, points: 5);
            try
            {
                skills.TryLearn(node, 1, out _);
                _hud.BindLearnedSkills(skills, level: 1);

                string text = _hud.ComputeListText();
                StringAssert.Contains("Learned", text,
                    "Already-learned node must read as 'Learned ✓' in the list.");
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(tree);
                Object.DestroyImmediate(node);
            }
        }

        [Test]
        public void Rebind_ClearsPreviousTree()
        {
            var nodeA = MakeNode("alpha");
            var treeA = MakeTree(nodeA);
            var (goA, skillsA) = MakeLearner(treeA, points: 1);

            var nodeB = MakeNode("beta");
            var treeB = MakeTree(nodeB);
            var (goB, skillsB) = MakeLearner(treeB, points: 1);
            try
            {
                _hud.BindLearnedSkills(skillsA, level: 1);
                StringAssert.Contains("alpha", _hud.ComputeListText());

                _hud.BindLearnedSkills(skillsB, level: 1);
                string textB = _hud.ComputeListText();
                StringAssert.Contains("beta", textB);
                Assert.IsFalse(textB.Contains("alpha"),
                    "Rebinding must swap the displayed tree, not stack both.");
            }
            finally
            {
                Object.DestroyImmediate(goA); Object.DestroyImmediate(goB);
                Object.DestroyImmediate(treeA); Object.DestroyImmediate(treeB);
                Object.DestroyImmediate(nodeA); Object.DestroyImmediate(nodeB);
            }
        }

        [Test]
        public void EnsureBuilt_Idempotent()
        {
            var canvasesBefore = _hudGo.GetComponentsInChildren<Canvas>(true).Length;
            _hud.EnsureBuilt();
            _hud.EnsureBuilt();
            var canvasesAfter = _hudGo.GetComponentsInChildren<Canvas>(true).Length;
            Assert.AreEqual(canvasesBefore, canvasesAfter,
                "Repeat EnsureBuilt must not stack multiple Canvases.");
        }
    }
}
