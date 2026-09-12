using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// The ORDER the grimoire names a node's locks in, and that it names all of them.
    ///
    /// <para>This shipped backwards: <c>CanLearn</c> tested COST first and returned the first
    /// failure, so a node gated on level 15 behind three prerequisites answered "Need 2 arcane
    /// point(s), have 1". The player was told to save up for a spell they could not reach for
    /// fifteen levels. Level is the only refusal they cannot fix by spending or by walking, so
    /// it is named first — the same order <c>CraftingService</c> uses, and for the same
    /// reason.</para>
    /// </summary>
    [TestFixture]
    public class SpellLockReasonTests
    {
        private GameObject _go;
        private KnownSpells _spells;
        private readonly List<SpellLock> _locks = new List<SpellLock>();

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Player");
            _spells = _go.AddComponent<KnownSpells>();
            _locks.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static SpellNode MakeNode(string id, int cost = 1, int levelReq = 0,
                                          params SpellNode[] prereqs)
        {
            var n = ScriptableObject.CreateInstance<SpellNode>();
            n.nodeId = id;
            n.displayNameOverride = id;
            n.pointCost = cost;
            n.levelRequirement = levelReq;
            n.prerequisites = prereqs ?? System.Array.Empty<SpellNode>();
            return n;
        }

        private static SpellTree MakeTree(string classKey, params SpellNode[] nodes)
        {
            var t = ScriptableObject.CreateInstance<SpellTree>();
            t.displayName = "Escuela";
            t.schoolKey = "test";
            t.classAffinities = new[] { classKey };
            t.offAffinityCostMultiplier = 2f;
            t.EditorSetNodes(nodes);
            return t;
        }

        private SpellTree Bind(int points, string classKey, params SpellNode[] nodes)
        {
            var tree = MakeTree(classKey, nodes);
            _spells.Configure(new[] { tree }, classKey, null);
            _spells.AddPoints(points);
            return tree;
        }

        private static bool Has(List<SpellLock> locks, SpellLockKind kind)
        {
            foreach (var l in locks) if (l.Kind == kind) return true;
            return false;
        }

        private static int IndexOf(List<SpellLock> locks, SpellLockKind kind)
        {
            for (int i = 0; i < locks.Count; i++) if (locks[i].Kind == kind) return i;
            return -1;
        }

        // ── The order ───────────────────────────────────────────────────────────

        [Test]
        public void Level_IsNamedBefore_Prerequisite_AndBefore_Points()
        {
            var root = MakeNode("root");
            var gated = MakeNode("gated", cost: 2, levelReq: 15, prereqs: root);
            var tree = Bind(points: 1, classKey: "dwarf", root, gated);

            Assert.IsFalse(_spells.CollectLockReasons(tree, gated, playerLevel: 1, _locks));

            int level = IndexOf(_locks, SpellLockKind.Level);
            int prereq = IndexOf(_locks, SpellLockKind.Prerequisite);
            int points = IndexOf(_locks, SpellLockKind.Points);

            Assert.Greater(level, -1, "the level gate must be reported");
            Assert.Greater(prereq, -1, "the missing prerequisite must be reported");
            Assert.Greater(points, -1, "the shortfall in points must be reported");

            Assert.Less(level, prereq, "level is the refusal the player cannot act on");
            Assert.Less(prereq, points, "a prerequisite is a plan; points are the easy part");
        }

        [Test]
        public void EveryShortfall_IsReported_NotJustTheFirst()
        {
            var root = MakeNode("root");
            var gated = MakeNode("gated", cost: 5, levelReq: 20, prereqs: root);
            var tree = Bind(points: 0, classKey: "dwarf", root, gated);

            _spells.CollectLockReasons(tree, gated, playerLevel: 1, _locks);
            Assert.AreEqual(3, _locks.Count,
                "three things stand in the way; naming one makes the player come back for the next");
        }

        [Test]
        public void EveryMissingPrerequisite_IsNamed()
        {
            var a = MakeNode("a");
            var b = MakeNode("b");
            var target = MakeNode("target", prereqs: new[] { a, b });
            var tree = Bind(points: 9, classKey: "dwarf", a, b, target);

            _spells.CollectLockReasons(tree, target, playerLevel: 99, _locks);
            Assert.AreEqual(2, _locks.Count);
            Assert.AreEqual(a, _locks[0].Missing);
            Assert.AreEqual(b, _locks[1].Missing);
        }

        // ── The numbers a view has to draw ──────────────────────────────────────

        [Test]
        public void ALevelLock_CarriesTheNumbers_SoAViewNeedNotParseASentence()
        {
            var node = MakeNode("n", levelReq: 12);
            var tree = Bind(points: 9, classKey: "dwarf", node);

            _spells.CollectLockReasons(tree, node, playerLevel: 4, _locks);
            var level = _locks[IndexOf(_locks, SpellLockKind.Level)];
            Assert.AreEqual(12, level.Required);
            Assert.AreEqual(4, level.Have);
        }

        [Test]
        public void APointsLock_SaysWhenTheCostIsTheOffAffinitySurcharge()
        {
            // The school belongs to the mague; the character is a dwarf, so it costs double.
            var node = MakeNode("n", cost: 2);
            var tree = MakeTree("mague", node);
            _spells.Configure(new[] { tree }, "dwarf", null);
            _spells.AddPoints(1);

            _spells.CollectLockReasons(tree, node, playerLevel: 99, _locks);
            var points = _locks[IndexOf(_locks, SpellLockKind.Points)];
            Assert.AreEqual(SpellLockKind.Points, points.Kind);
            Assert.AreEqual(4, points.Required, "2 points at the x2 off-affinity surcharge");
            Assert.IsTrue(points.OffAffinity,
                "a node costing 2 where its neighbours cost 1 reads as a bug unless the reason says why");
            Assert.AreEqual(tree, points.School);
        }

        // ── The legacy single-reason API ────────────────────────────────────────

        [Test]
        public void CanLearn_StillAnswersOneSentence_AndItIsTheMostStructuralOne()
        {
            var root = MakeNode("root");
            var gated = MakeNode("gated", cost: 2, levelReq: 15, prereqs: root);
            var tree = Bind(points: 0, classKey: "dwarf", root, gated);

            Assert.IsFalse(_spells.CanLearn(tree, gated, 1, out string reason));
            StringAssert.Contains("level 15", reason,
                "the old order answered with the points, which is the one the player can fix");
        }

        [Test]
        public void ANodeWithNothingInTheWay_ReportsNoLocks()
        {
            var node = MakeNode("n");
            var tree = Bind(points: 3, classKey: "dwarf", node);

            Assert.IsTrue(_spells.CollectLockReasons(tree, node, 99, _locks));
            Assert.AreEqual(0, _locks.Count);
            Assert.IsTrue(_spells.CanLearn(tree, node, 99, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void AKnownNode_ReportsAlreadyKnown_AndNothingElse()
        {
            var node = MakeNode("n");
            var tree = Bind(points: 3, classKey: "dwarf", node);
            Assert.IsTrue(_spells.TryLearn(tree, node, 99, out _));

            _spells.CollectLockReasons(tree, node, 99, _locks);
            Assert.AreEqual(1, _locks.Count);
            Assert.IsTrue(Has(_locks, SpellLockKind.AlreadyKnown));
        }
    }
}
