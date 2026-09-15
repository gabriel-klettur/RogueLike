using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Gameplay.Player.Animations
{
    /// <summary>
    /// Timeline playback on <see cref="DirectionalAnimator"/>: installing a plan, what it does to
    /// the reported state length, and when it is torn down.
    ///
    /// <para>What this fixture does NOT cover, stated rather than implied: the CLOCK. Unity calls
    /// no Update in Edit Mode, so no plan advances on its own here. The arithmetic that decides
    /// how long each step lasts is pinned by <c>CastTimelineResolverTests</c>, which is pure; what
    /// is pinned here is everything around it — install, report, scrub, clear.</para>
    /// </summary>
    [TestFixture]
    public class DirectionalAnimatorTimelineTests
    {
        private readonly List<Object> _created = new List<Object>();
        private GameObject _go;
        private DirectionalAnimator _animator;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("TimelineRig");
            _created.Add(_go);
            _go.AddComponent<SpriteRenderer>();
            _animator = _go.AddComponent<DirectionalAnimator>();

            var set = DirectionalAnimator.CreateSetFromLinearFrames(Frames(24));
            _animator.SetSpriteSets(set, set, set, set, set, set, set, false);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private List<Sprite> Frames(int count)
        {
            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var tex = new Texture2D(2, 2);
                _created.Add(tex);
                var sprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0f), 16f);
                sprite.name = $"f{i}";
                _created.Add(sprite);
                frames.Add(sprite);
            }
            return frames;
        }

        private static List<ResolvedTimelineStep> Plan(params (int frame, float seconds)[] steps)
        {
            var list = new List<ResolvedTimelineStep>(steps.Length);
            foreach (var (frame, seconds) in steps)
                list.Add(new ResolvedTimelineStep { Frame = frame, Seconds = seconds });
            return list;
        }

        // ── Install ──────────────────────────────────────────────────────────────

        [Test]
        public void PlayTimeline_InstallsThePlan_AndPosesTheAnimator()
        {
            _animator.PlayTimeline(DirectionalAnimator.AnimState.Cast,
                                   DirectionalAnimator.Direction.South, -1,
                                   Plan((0, 0.3f), (1, 0.3f), (2, 0.4f)));

            Assert.That(_animator.HasTimeline, Is.True);
            Assert.That(_animator.TimelineStepCount, Is.EqualTo(3));
            Assert.That(_animator.TimelineDuration, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(_animator.CurrentState, Is.EqualTo(DirectionalAnimator.AnimState.Cast));
            Assert.That(_animator.TimelineStepIndex, Is.EqualTo(0), "a fresh plan starts at its first step");
        }

        [Test]
        public void GetStateLength_ReportsThePlan_NotTheFrameCount()
        {
            // Three frames per direction at the default 0.15 s would be 0.45 s.
            float natural = _animator.GetStateLength(DirectionalAnimator.AnimState.Cast);

            _animator.PlayTimeline(DirectionalAnimator.AnimState.Cast,
                                   DirectionalAnimator.Direction.South, -1,
                                   Plan((0, 0.6f), (1, 0.6f)));

            Assert.That(natural, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(_animator.GetStateLength(DirectionalAnimator.AnimState.Cast),
                Is.EqualTo(1.2f).Within(0.0001f),
                "the cast window is sized from this number: reporting the frame count would " +
                "close it in the middle of a stretched wind-up and hand locomotion back mid-cast");
        }

        [Test]
        public void AnEmptyPlan_LeavesTheOrdinaryClockInCharge()
        {
            _animator.PlayTimeline(DirectionalAnimator.AnimState.Cast,
                                   DirectionalAnimator.Direction.South, -1,
                                   new List<ResolvedTimelineStep>());

            Assert.That(_animator.HasTimeline, Is.False);
            Assert.That(_animator.GetStateLength(DirectionalAnimator.AnimState.Cast),
                Is.EqualTo(0.45f).Within(0.0001f));
        }

        // ── Scrub ────────────────────────────────────────────────────────────────

        [Test]
        public void ShowFrame_MovesTheSTEP_WhileAPlanIsInstalled()
        {
            // The same frame twice with different durations: a frame index could not tell the
            // two apart, which is why the strip and the transport work in steps.
            _animator.PlayTimeline(DirectionalAnimator.AnimState.Cast,
                                   DirectionalAnimator.Direction.South, -1,
                                   Plan((1, 0.2f), (1, 0.8f), (2, 0.2f)));

            _animator.ShowFrame(1);
            Assert.That(_animator.TimelineStepIndex, Is.EqualTo(1));

            _animator.StepFrame(1);
            Assert.That(_animator.TimelineStepIndex, Is.EqualTo(2));

            _animator.StepFrame(1);
            Assert.That(_animator.TimelineStepIndex, Is.EqualTo(0), "stepping past the end wraps");
        }

        [Test]
        public void TimelineStepAt_ReportsWhatWasInstalled()
        {
            _animator.PlayTimeline(DirectionalAnimator.AnimState.Cast,
                                   DirectionalAnimator.Direction.South, -1,
                                   Plan((2, 0.25f), (0, 0.75f)));

            Assert.That(_animator.TimelineStepAt(0).Frame, Is.EqualTo(2));
            Assert.That(_animator.TimelineStepAt(1).Seconds, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(_animator.TimelineStepAt(9).Seconds, Is.EqualTo(0f),
                "an out-of-range step answers the default rather than throwing in a draw path");
        }

        // ── Teardown ─────────────────────────────────────────────────────────────

        [Test]
        public void AStateChange_EndsThePlan()
        {
            _animator.PlayTimeline(DirectionalAnimator.AnimState.Cast,
                                   DirectionalAnimator.Direction.South, -1,
                                   Plan((0, 0.5f), (1, 0.5f)));

            _animator.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.South);

            Assert.That(_animator.HasTimeline, Is.False,
                "a plan left running across a state change would keep drawing the previous " +
                "spell's frame indices out of the new state's set");
        }

        [Test]
        public void ATurn_DoesNOTEndThePlan()
        {
            _animator.PlayTimeline(DirectionalAnimator.AnimState.Cast,
                                   DirectionalAnimator.Direction.South, -1,
                                   Plan((0, 0.5f), (1, 0.5f)));
            _animator.ShowFrame(1);

            _animator.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.East);

            Assert.That(_animator.HasTimeline, Is.True,
                "the plan's indices are per direction, so a character who turns mid-cast keeps " +
                "their place and simply faces the other way");
            Assert.That(_animator.TimelineStepIndex, Is.EqualTo(1));
        }

        [Test]
        public void ClearTimeline_HandsTheFramesBack()
        {
            _animator.PlayTimeline(DirectionalAnimator.AnimState.Cast,
                                   DirectionalAnimator.Direction.South, -1,
                                   Plan((0, 0.5f)));

            _animator.ClearTimeline();

            Assert.That(_animator.HasTimeline, Is.False);
            Assert.That(_animator.TimelineDuration, Is.EqualTo(0f));
            Assert.That(_animator.GetStateLength(DirectionalAnimator.AnimState.Cast),
                Is.EqualTo(0.45f).Within(0.0001f));
        }

        [Test]
        public void NothingInGameplayInstallsAPlanByDefault()
        {
            Assert.That(_animator.HasTimeline, Is.False,
                "every animation in the game plays exactly as it did until an author draws one");
        }
    }
}
