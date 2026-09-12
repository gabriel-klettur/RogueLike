using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// What the board says about a node, decided without a canvas.
    ///
    /// <para>This is the half of the grimoire that CAN be pinned. uGUI performs no layout in
    /// Edit Mode, so a fixture that measured the drawn board would be measuring the numbers it
    /// wrote; the state machine is pure, so it can be measured for real.</para>
    /// </summary>
    [TestFixture]
    public class GrimoireNodeStateTests
    {
        private readonly List<SpellLock> _locks = new List<SpellLock>();

        [SetUp]
        public void SetUp() => _locks.Clear();

        private static SpellNode Node(string id) =>
            ScriptableObject.CreateInstance<SpellNode>();

        // ── Precedence ──────────────────────────────────────────────────────────

        [Test]
        public void Learned_BeatsEverything()
        {
            _locks.Add(SpellLock.Level(15, 1));
            Assert.AreEqual(GrimoireNodeState.Learned,
                GrimoireNodeStatus.Resolve(learned: true, _locks));
        }

        [Test]
        public void NoLocks_IsAvailable()
        {
            Assert.AreEqual(GrimoireNodeState.Available,
                GrimoireNodeStatus.Resolve(learned: false, _locks));
        }

        [Test]
        public void TheStateFollowsTheModelsOwnOrder_LevelFirst()
        {
            // The model collects level, then prerequisites, then points. The board must show
            // the FIRST, so the socket and the card's first phrase agree: picking a different
            // one here would make the two disagree about why a node is shut.
            _locks.Add(SpellLock.Level(15, 1));
            _locks.Add(SpellLock.Prerequisite(Node("p")));
            _locks.Add(SpellLock.Points(2, 1, false, null));

            Assert.AreEqual(GrimoireNodeState.NeedsLevel,
                GrimoireNodeStatus.Resolve(false, _locks));
        }

        [Test]
        public void PrerequisiteBeatsPoints()
        {
            _locks.Add(SpellLock.Prerequisite(Node("p")));
            _locks.Add(SpellLock.Points(2, 1, false, null));
            Assert.AreEqual(GrimoireNodeState.NeedsPrerequisite,
                GrimoireNodeStatus.Resolve(false, _locks));
        }

        [Test]
        public void PointsAlone_IsNeedsPoints()
        {
            _locks.Add(SpellLock.Points(2, 1, false, null));
            Assert.AreEqual(GrimoireNodeState.NeedsPoints,
                GrimoireNodeStatus.Resolve(false, _locks));
        }

        [Test]
        public void AMalformedNode_SaysSo_RatherThanReadingAsAvailable()
        {
            _locks.Add(SpellLock.Malformed());
            Assert.AreEqual(GrimoireNodeState.Malformed,
                GrimoireNodeStatus.Resolve(false, _locks));
        }

        [Test]
        public void AnAlreadyKnownLock_ResolvesToLearned()
        {
            _locks.Add(SpellLock.AlreadyKnown());
            Assert.AreEqual(GrimoireNodeState.Learned,
                GrimoireNodeStatus.Resolve(false, _locks));
        }

        [Test]
        public void ANullLockList_IsAvailable_NeverAnException()
        {
            Assert.AreEqual(GrimoireNodeState.Available,
                GrimoireNodeStatus.Resolve(false, null));
        }

        // ── What the drawing reads off the state ────────────────────────────────

        [Test]
        public void OnlyAnAvailableNode_IsBuyable()
        {
            foreach (GrimoireNodeState s in System.Enum.GetValues(typeof(GrimoireNodeState)))
                Assert.AreEqual(s == GrimoireNodeState.Available,
                    GrimoireNodeStatus.IsBuyable(s), s.ToString());
        }

        [Test]
        public void OnlyALearnedNode_IsFilled()
        {
            // An available node is an EMPTY socket with a light behind it. That is what makes
            // the board read as something being filled in rather than as a menu with rows
            // greyed out.
            foreach (GrimoireNodeState s in System.Enum.GetValues(typeof(GrimoireNodeState)))
                Assert.AreEqual(s == GrimoireNodeState.Learned,
                    GrimoireNodeStatus.IsFilled(s), s.ToString());
        }

        [Test]
        public void OnlyReachableNodes_KeepTheirIconsColour()
        {
            foreach (GrimoireNodeState s in System.Enum.GetValues(typeof(GrimoireNodeState)))
            {
                bool expected = s == GrimoireNodeState.Learned || s == GrimoireNodeState.Available;
                Assert.AreEqual(expected, GrimoireNodeStatus.IsIconLit(s), s.ToString());
            }
        }

        [Test]
        public void AChainIsOpen_OnlyWhenItsParentIsBought()
        {
            foreach (GrimoireNodeState s in System.Enum.GetValues(typeof(GrimoireNodeState)))
                Assert.AreEqual(s == GrimoireNodeState.Learned,
                    GrimoireNodeStatus.LinkIsOpen(s), s.ToString());
        }

        // ── The redundancy R6 asks for ──────────────────────────────────────────

        [Test]
        public void TheThreeLockedStates_AreDistinguishedWithoutColour()
        {
            // Colour cannot separate them: all three are the same dim socket. What tells them
            // apart is the caption, and the caption comes from the lock's KIND — so the three
            // must resolve to three different states or the board cannot say which is which.
            var seen = new HashSet<GrimoireNodeState>();

            _locks.Clear(); _locks.Add(SpellLock.Level(9, 1));
            seen.Add(GrimoireNodeStatus.Resolve(false, _locks));

            _locks.Clear(); _locks.Add(SpellLock.Prerequisite(Node("p")));
            seen.Add(GrimoireNodeStatus.Resolve(false, _locks));

            _locks.Clear(); _locks.Add(SpellLock.Points(3, 0, false, null));
            seen.Add(GrimoireNodeStatus.Resolve(false, _locks));

            Assert.AreEqual(3, seen.Count,
                "three reasons a node is shut must be three states, or the caption cannot differ");
        }
    }
}
