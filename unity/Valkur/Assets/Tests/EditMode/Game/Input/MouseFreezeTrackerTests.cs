using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// The InputSystem pointer loses its priority once it has demonstrably stopped moving
    /// while the legacy pointer has not.
    ///
    /// Measured live on 2026-09-05: with the Unity 2022.3 event-drop bug active, the
    /// InputSystem mouse froze at the screen centre and at the last delivered position —
    /// both finite, both in view, both preferred over the correct legacy reading by a selector
    /// whose only guard was "stale zero". The cursor resolved to the player's own feet and every
    /// aimed spell flew straight down. These tests pin the tracker that turns that from a
    /// per-frame guess into a verdict built from evidence.
    /// </summary>
    [TestFixture]
    public class MouseFreezeTrackerTests
    {
        private static readonly Vector2 Frozen = new Vector2(800f, 400f);

        private MouseFreezeTracker _tracker;

        [SetUp]
        public void SetUp() => _tracker = new MouseFreezeTracker();

        /// <summary>Legacy walks one pixel-plus per frame; the InputSystem sits still.</summary>
        private bool FeedLegacyMotion(int frames, int startFrame = 1)
        {
            bool frozen = false;
            for (int i = 0; i < frames; i++)
            {
                var legacy = new Vector2(100f + i * 5f, 100f);
                frozen = _tracker.Observe(startFrame + i, Frozen, true, legacy, true);
            }
            return frozen;
        }

        [Test]
        public void StartsTrustingTheInputSystem()
        {
            Assert.IsFalse(_tracker.InputSystemFrozen);
            Assert.IsFalse(_tracker.Observe(1, Frozen, true, new Vector2(100f, 100f), true));
        }

        [Test]
        public void DeclaresFrozenAfterEnoughFramesOfLegacyMotionWithAStillInputSystem()
        {
            // First observation is the baseline; each of the next N is one frame of evidence.
            bool frozen = FeedLegacyMotion(MouseFreezeTracker.FramesToDeclareFrozen + 1);

            Assert.IsTrue(frozen);
            Assert.IsTrue(_tracker.InputSystemFrozen);
        }

        [Test]
        public void DoesNotDeclareFrozenOnOneFrameOfMotion()
        {
            // A single frame cannot separate a frozen device from a hand that just started
            // moving after the InputSystem's last event — that is exactly the frame in
            // which the two backends legitimately disagree by one delta.
            bool frozen = FeedLegacyMotion(2);

            Assert.IsFalse(frozen);
        }

        [Test]
        public void RepeatedObservationsWithinOneFrameAreNotEvidence()
        {
            // Every reader in the project calls TryGetScreenMousePosition, several times a
            // frame. Counting each call would declare a freeze from one frame of motion.
            _tracker.Observe(1, Frozen, true, new Vector2(100f, 100f), true);
            for (int i = 0; i < 20; i++)
                _tracker.Observe(2, Frozen, true, new Vector2(150f, 100f), true);

            Assert.IsFalse(_tracker.InputSystemFrozen);
        }

        [Test]
        public void BothBackendsMovingTogetherIsNotAFreeze()
        {
            for (int i = 0; i < 10; i++)
            {
                var p = new Vector2(100f + i * 5f, 100f);
                Assert.IsFalse(_tracker.Observe(1 + i, p, true, p, true));
            }
        }

        [Test]
        public void TheVerdictClearsTheMomentTheInputSystemMovesAgain()
        {
            FeedLegacyMotion(MouseFreezeTracker.FramesToDeclareFrozen + 1);
            Assert.IsTrue(_tracker.InputSystemFrozen, "precondition");

            int next = MouseFreezeTracker.FramesToDeclareFrozen + 2;
            bool frozen = _tracker.Observe(next, Frozen + new Vector2(3f, 0f), true, new Vector2(500f, 100f), true);

            Assert.IsFalse(frozen);
            Assert.IsFalse(_tracker.InputSystemFrozen);
        }

        [Test]
        public void AStillHandKeepsWhateverVerdictStands()
        {
            // Frozen, then nobody touches the mouse: no evidence either way, verdict stays.
            FeedLegacyMotion(MouseFreezeTracker.FramesToDeclareFrozen + 1);
            int next = MouseFreezeTracker.FramesToDeclareFrozen + 2;
            var still = new Vector2(500f, 100f);
            _tracker.Observe(next, Frozen, true, still, true);
            for (int i = 1; i <= 30; i++)
                Assert.IsTrue(_tracker.Observe(next + i, Frozen, true, still, true));
        }

        [Test]
        public void SubPixelLegacyJitterIsNotMotion()
        {
            _tracker.Observe(1, Frozen, true, new Vector2(100f, 100f), true);
            for (int i = 1; i <= 10; i++)
                _tracker.Observe(1 + i, Frozen, true, new Vector2(100f + (i % 2) * 0.4f, 100f), true);

            Assert.IsFalse(_tracker.InputSystemFrozen,
                "Legacy motion under LegacyMotionThreshold is a still hand, not evidence.");
        }

        [Test]
        public void WithoutBothBackendsThereIsNoVerdict()
        {
            FeedLegacyMotion(MouseFreezeTracker.FramesToDeclareFrozen + 1);
            Assert.IsTrue(_tracker.InputSystemFrozen, "precondition");

            // Legacy disappears (Active Input Handling = new only, say): nothing to prefer.
            Assert.IsFalse(_tracker.Observe(50, Frozen, true, Vector2.zero, false));
            Assert.IsFalse(_tracker.InputSystemFrozen);

            // InputSystem device gone: same answer.
            Assert.IsFalse(_tracker.Observe(51, Vector2.zero, false, new Vector2(10f, 10f), true));
        }

        [Test]
        public void ResetForgetsTheVerdictAndTheBaseline()
        {
            FeedLegacyMotion(MouseFreezeTracker.FramesToDeclareFrozen + 1);
            _tracker.Reset();

            Assert.IsFalse(_tracker.InputSystemFrozen);
            // Needs a fresh baseline plus the full evidence count again.
            Assert.IsFalse(FeedLegacyMotion(MouseFreezeTracker.FramesToDeclareFrozen, startFrame: 100));
        }
    }
}
