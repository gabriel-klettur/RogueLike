using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Gameplay.Player
{
    /// <summary>
    /// Pins the order and the completeness of <see cref="LearnedSkills.CollectLockReasons"/>.
    ///
    /// <para>The old body tested AFFORDABILITY first and returned one reason, so a character with
    /// no points was told "Need 2 skill point(s)" about every node in the tree — including the
    /// ones whose real gate was a prerequisite five ranks deep. Measured on the shipped dwarf,
    /// Bulwark reported a 2-point shortfall when what actually closes it is Stoneflesh at rank 5,
    /// which is eight points of commitment away. Both statements were true; only one was the
    /// answer.</para>
    ///
    /// <para>This is the twin of <c>SpellLockReasonTests</c>, deliberately: the two tabs of the
    /// character sheet must refuse a purchase for the same reasons in the same order.</para>
    /// </summary>
    [TestFixture]
    public class SkillLockReasonTests
    {
        private readonly List<Object> _spawned = new List<Object>();
        private readonly List<SkillLock> _locks = new List<SkillLock>();
        private GameObject _playerGo;
        private LearnedSkills _skills;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player");
            _skills = _playerGo.AddComponent<LearnedSkills>();
            _locks.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        private SkillNode Node(string id, int cost = 1, int maxRank = 1, int levelReq = 0,
                               int levelPerRank = 0, params SkillNode[] prereqs)
        {
            var n = ScriptableObject.CreateInstance<SkillNode>();
            n.skillId = id;
            n.displayName = id;
            n.pointCost = cost;
            n.maxRank = maxRank;
            n.levelRequirement = levelReq;
            n.levelPerRank = levelPerRank;
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

        private bool Collect(SkillNode node, int level)
        {
            _locks.Clear();
            return _skills.CollectLockReasons(node, level, _locks);
        }

        private bool Has(SkillLockKind kind)
        {
            foreach (var l in _locks) if (l.Kind == kind) return true;
            return false;
        }

        // ── Order ───────────────────────────────────────────────────────────────

        /// <summary>
        /// The measured defect, reproduced: a node the player can neither reach NOR afford must
        /// name the prerequisite, and must name it BEFORE the cost.
        /// </summary>
        [Test]
        public void ThePrerequisite_IsNamedBeforeTheCost()
        {
            var root = Node("root", maxRank: 5);
            var behind = Node("behind", cost: 2, prereqs: root);
            _skills.SetTree(Tree(root, behind));
            _skills.AddPoints(0);

            Assert.IsFalse(Collect(behind, 1));
            Assert.IsTrue(Has(SkillLockKind.Prerequisite), "The real gate must be reported.");
            Assert.IsTrue(Has(SkillLockKind.Points), "The shortfall is still true and still said.");

            int prereqAt = _locks.FindIndex(l => l.Kind == SkillLockKind.Prerequisite);
            int pointsAt = _locks.FindIndex(l => l.Kind == SkillLockKind.Points);
            Assert.Less(prereqAt, pointsAt,
                "Cost is the reason the player can fix by walking away and coming back; the " +
                "prerequisite is the one that decides whether to.");
        }

        [Test]
        public void TheLevel_IsNamedBeforeEverything()
        {
            var root = Node("root", maxRank: 2);
            var gated = Node("gated", cost: 3, levelReq: 9, prereqs: root);
            _skills.SetTree(Tree(root, gated));

            Assert.IsFalse(Collect(gated, 1));
            Assert.AreEqual(SkillLockKind.Level, _locks[0].Kind,
                "A level is the only refusal the player cannot act on at all.");
            Assert.AreEqual(9, _locks[0].Required);
            Assert.AreEqual(1, _locks[0].Have);
        }

        // ── Completeness ────────────────────────────────────────────────────────

        [Test]
        public void EveryUnfinishedPrerequisite_IsReported()
        {
            var a = Node("a", maxRank: 2);
            var b = Node("b", maxRank: 2);
            var child = Node("child", prereqs: new[] { a, b });
            _skills.SetTree(Tree(a, b, child));
            _skills.AddPoints(10);

            Assert.IsFalse(Collect(child, 1));
            int prereqs = _locks.FindAll(l => l.Kind == SkillLockKind.Prerequisite).Count;
            Assert.AreEqual(2, prereqs,
                "Naming one gate at a time makes the player clear it and come back to another.");
        }

        /// <summary>
        /// A prerequisite must be at FULL rank. The ranks still owed ride on the lock because
        /// "finish that one" and "finish that one, three ranks to go" are different amounts of
        /// commitment and the player is deciding whether to make it.
        /// </summary>
        [Test]
        public void APartialPrerequisite_StillBlocks_AndSaysHowMuchIsLeft()
        {
            var root = Node("root", maxRank: 5);
            var child = Node("child", prereqs: root);
            _skills.SetTree(Tree(root, child));
            _skills.AddPoints(10);

            _skills.TryLearn(root, 1, out _);
            _skills.TryLearn(root, 1, out _);

            Assert.IsFalse(Collect(child, 1));
            var prereq = _locks.Find(l => l.Kind == SkillLockKind.Prerequisite);
            Assert.AreEqual(SkillLockKind.Prerequisite, prereq.Kind);
            Assert.AreEqual(3, prereq.MissingRanksLeft, "Five ranks, two held.");
            Assert.AreSame(root, prereq.Missing);
        }

        [Test]
        public void AFinishedPrerequisite_Opens()
        {
            var root = Node("root", maxRank: 2);
            var child = Node("child", prereqs: root);
            _skills.SetTree(Tree(root, child));
            _skills.AddPoints(10);

            _skills.TryLearn(root, 1, out _);
            _skills.TryLearn(root, 1, out _);

            Assert.IsTrue(Collect(child, 1));
            Assert.AreEqual(0, _locks.Count);
        }

        [Test]
        public void AMaxedNode_ReportsMaxed_AndNothingElse()
        {
            var node = Node("node", maxRank: 1);
            _skills.SetTree(Tree(node));
            _skills.AddPoints(5);
            _skills.TryLearn(node, 1, out _);

            Assert.IsFalse(Collect(node, 1));
            Assert.AreEqual(1, _locks.Count);
            Assert.AreEqual(SkillLockKind.Maxed, _locks[0].Kind);
        }

        [Test]
        public void LevelPerRank_GatesTheNEXTRank_NotTheFirst()
        {
            var node = Node("paced", maxRank: 3, levelReq: 1, levelPerRank: 5);
            _skills.SetTree(Tree(node));
            _skills.AddPoints(9);

            Assert.IsTrue(Collect(node, 1), "Rank 1 needs level 1.");
            _skills.TryLearn(node, 1, out _);

            Assert.IsFalse(Collect(node, 1), "Rank 2 needs level 6.");
            Assert.AreEqual(6, _locks.Find(l => l.Kind == SkillLockKind.Level).Required);
            Assert.IsTrue(Collect(node, 6));
        }

        [Test]
        public void AMalformedNode_ReportsMalformed()
        {
            var node = Node("", maxRank: 1);
            node.skillId = string.Empty;
            _skills.SetTree(Tree(node));

            Assert.IsFalse(Collect(node, 1));
            Assert.AreEqual(SkillLockKind.Malformed, _locks[0].Kind);
            Assert.IsFalse(Collect(null, 1));
        }

        // ── The legacy single-reason API ────────────────────────────────────────

        /// <summary>
        /// <c>CanLearn</c> keeps its shape for the console and the older callers, and its single
        /// reason is the FIRST one — which, now the order is right, is the useful one.
        /// </summary>
        [Test]
        public void CanLearn_StillAnswersOneReason_AndItIsTheMostBlocking()
        {
            var root = Node("root", maxRank: 5);
            var behind = Node("behind", cost: 2, prereqs: root);
            _skills.SetTree(Tree(root, behind));

            Assert.IsFalse(_skills.CanLearn(behind, 1, out string reason));
            StringAssert.Contains("root", reason);
            Assert.IsTrue(_skills.CanLearn(root, 1, out string free) == false || free == string.Empty);
        }

        // ── The event ───────────────────────────────────────────────────────────

        /// <summary>
        /// The defect that made the whole panel blind: receiving a point changes WHAT CAN BE
        /// BOUGHT, so it has to raise the event every view of this tree listens to.
        /// </summary>
        [Test]
        public void AddPoints_RaisesTheLoadoutEvent()
        {
            var node = Node("node");
            _skills.SetTree(Tree(node));

            int loadout = 0, points = 0;
            _skills.OnLoadoutChanged += () => loadout++;
            _skills.OnPointsChanged += _ => points++;

            _skills.AddPoints(1);

            Assert.AreEqual(1, points);
            Assert.AreEqual(1, loadout,
                "A panel subscribed only to OnLoadoutChanged went on saying '0 points' after a " +
                "level-up until it was closed and reopened.");
        }

        [Test]
        public void AddPointsOfZero_RaisesNothing()
        {
            var node = Node("node");
            _skills.SetTree(Tree(node));

            int loadout = 0;
            _skills.OnLoadoutChanged += () => loadout++;
            _skills.AddPoints(0);
            _skills.AddPoints(-3);

            Assert.AreEqual(0, loadout);
        }
    }
}
