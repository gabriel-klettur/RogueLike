using System.Collections.Generic;
using NUnit.Framework;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.Player
{
    /// <summary>
    /// <see cref="CastTimelineResolver"/> — the arithmetic that makes an animation and a cast
    /// share one clock.
    ///
    /// <para>It is a pure function on purpose, and this fixture is why: the number it produces
    /// decides WHEN a spell's frames are on screen relative to the moment it fires, and through
    /// <c>GetStateLength</c> it also sizes the window that holds locomotion off. All of that is
    /// testable here without a scene, an animator or a Play Mode.</para>
    /// </summary>
    [TestFixture]
    public class CastTimelineResolverTests
    {
        private static AnimationTimeline Timeline(int release, int recover,
                                                  TimelineSegmentMode prepareMode,
                                                  TimelineSegmentMode channelMode,
                                                  params (int frame, float seconds)[] steps)
        {
            var timeline = new AnimationTimeline
            {
                steps = new List<AnimationTimelineStep>(),
                releaseStep = release,
                recoverStep = recover,
                prepareMode = prepareMode,
                channelMode = channelMode,
            };
            foreach (var (frame, seconds) in steps)
                timeline.steps.Add(new AnimationTimelineStep { frame = frame, duration = seconds });
            return timeline;
        }

        private static float Total(IReadOnlyList<ResolvedTimelineStep> steps)
        {
            float total = 0f;
            for (int i = 0; i < steps.Count; i++) total += steps[i].Seconds;
            return total;
        }

        // ── Nothing authored ─────────────────────────────────────────────────────

        [Test]
        public void NoTimeline_ResolvesToNothing_SoTheOrdinaryClockKeepsThePose()
        {
            Assert.That(CastTimelineResolver.Resolve(null, 1f, 0f), Is.Empty);
            Assert.That(CastTimelineResolver.Resolve(new AnimationTimeline(), 1f, 0f), Is.Empty);
        }

        // ── Stretch ──────────────────────────────────────────────────────────────

        [Test]
        public void Stretch_MakesTheWindUpLastExactlyTheSpellsPrepare()
        {
            // Three frames drawn at 0.1 s each — 0.3 s authored — against a 0.9 s wind-up.
            var timeline = Timeline(3, 3, TimelineSegmentMode.Stretch, TimelineSegmentMode.Stretch,
                                    (0, 0.1f), (1, 0.1f), (2, 0.1f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 0.9f, channel: 0f);

            Assert.That(steps.Count, Is.EqualTo(3));
            Assert.That(Total(steps), Is.EqualTo(0.9f).Within(0.0001f),
                "the whole point: the wind-up ends when the spell fires, not before or after");
            foreach (var step in steps)
                Assert.That(step.Seconds, Is.EqualTo(0.3f).Within(0.0001f));
        }

        [Test]
        public void Stretch_KeepsTheAuthoredRhythm_AsRatios()
        {
            // 1 : 3 : 1 — the middle frame is the one the artist wanted to linger on.
            var timeline = Timeline(3, 3, TimelineSegmentMode.Stretch, TimelineSegmentMode.Stretch,
                                    (0, 0.1f), (1, 0.3f), (2, 0.1f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 1f, channel: 0f);

            Assert.That(Total(steps), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(steps[1].Seconds / steps[0].Seconds, Is.EqualTo(3f).Within(0.0001f),
                "stretching must scale the segment, never flatten it to equal steps");
        }

        [Test]
        public void Stretch_HasNoClamp_BecauseNoneWasWanted()
        {
            var timeline = Timeline(6, 6, TimelineSegmentMode.Stretch, TimelineSegmentMode.Stretch,
                                    (0, 0.15f), (1, 0.15f), (2, 0.15f),
                                    (3, 0.15f), (4, 0.15f), (5, 0.15f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 0.05f, channel: 0f);

            Assert.That(Total(steps), Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(steps[0].Seconds, Is.LessThan(0.01f),
                "a 0.05 s wind-up over six frames really is 8 ms a frame — the panel reports " +
                "the rate rather than the resolver refusing it");
        }

        // ── Loop ─────────────────────────────────────────────────────────────────

        [Test]
        public void Loop_RepeatsTheCycle_AndCutsTheLastStepShort()
        {
            // Two frames of 0.3 s over a 1.0 s wind-up: 0.3 + 0.3 + 0.3 + 0.1.
            var timeline = Timeline(2, 2, TimelineSegmentMode.Loop, TimelineSegmentMode.Stretch,
                                    (0, 0.3f), (1, 0.3f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 1f, channel: 0f);

            Assert.That(Total(steps), Is.EqualTo(1f).Within(0.0001f),
                "overrunning would push the release past the moment the spell fires, which is " +
                "the one thing the whole mechanism exists to pin");
            Assert.That(steps.Count, Is.EqualTo(4));
            Assert.That(steps[3].Seconds, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(steps[0].Frame, Is.EqualTo(0));
            Assert.That(steps[1].Frame, Is.EqualTo(1));
            Assert.That(steps[2].Frame, Is.EqualTo(0), "the cycle repeats from its first frame");
        }

        [Test]
        public void Loop_KeepsTheAuthoredSpeed_UnlikeStretch()
        {
            var timeline = Timeline(2, 2, TimelineSegmentMode.Loop, TimelineSegmentMode.Stretch,
                                    (0, 0.2f), (1, 0.2f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 3f, channel: 0f);

            Assert.That(steps[0].Seconds, Is.EqualTo(0.2f).Within(0.0001f),
                "a three-second charge drawn as a short cycle must still read at drawing speed");
            Assert.That(Total(steps), Is.EqualTo(3f).Within(0.0001f));
        }

        // ── Hold ─────────────────────────────────────────────────────────────────

        [Test]
        public void Hold_PlaysOnce_ThenTheLastFrameCarriesTheRemainder()
        {
            var timeline = Timeline(2, 2, TimelineSegmentMode.Hold, TimelineSegmentMode.Stretch,
                                    (0, 0.2f), (7, 0.2f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 1f, channel: 0f);

            Assert.That(Total(steps), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(steps.Count, Is.EqualTo(3), "the hold is its own step, so a reader can see it");
            Assert.That(steps[2].Frame, Is.EqualTo(7), "the LAST frame is the one that holds");
            Assert.That(steps[2].Seconds, Is.EqualTo(0.6f).Within(0.0001f));
        }

        // ── Segments ─────────────────────────────────────────────────────────────

        [Test]
        public void TheReleaseStep_SplitsPrepareFromChannel()
        {
            // Steps 0-1 are the wind-up, 2-3 the channel.
            var timeline = Timeline(2, 4, TimelineSegmentMode.Stretch, TimelineSegmentMode.Stretch,
                                    (0, 0.1f), (1, 0.1f), (2, 0.1f), (3, 0.1f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 0.8f, channel: 0.4f);

            Assert.That(steps.Count, Is.EqualTo(4));
            Assert.That(steps[0].Seconds + steps[1].Seconds, Is.EqualTo(0.8f).Within(0.0001f),
                "everything before the release step covers prepareDuration");
            Assert.That(steps[2].Seconds + steps[3].Seconds, Is.EqualTo(0.4f).Within(0.0001f),
                "and the launch segment covers channelDuration");
        }

        [Test]
        public void TheRecoverySegment_PlaysAsDrawn_BecauseACooldownIsNotAPose()
        {
            var timeline = Timeline(1, 2, TimelineSegmentMode.Stretch, TimelineSegmentMode.Stretch,
                                    (0, 0.1f), (1, 0.1f), (2, 0.25f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 0.5f, channel: 0.5f);

            Assert.That(Total(steps), Is.EqualTo(0.5f + 0.5f + 0.25f).Within(0.0001f),
                "holding the casting pose for war_cry's twenty-second cooldown would freeze the " +
                "character out of their own turn");
        }

        [Test]
        public void ASpellWithNoPhases_PlaysTheTimelineExactlyAsDrawn()
        {
            var timeline = Timeline(2, 3, TimelineSegmentMode.Stretch, TimelineSegmentMode.Loop,
                                    (0, 0.12f), (1, 0.12f), (2, 0.12f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 0f, channel: 0f);

            Assert.That(Total(steps), Is.EqualTo(0.36f).Within(0.0001f),
                "a timeline has to be safe to author on the many spells that fire instantly");
            Assert.That(steps.Count, Is.EqualTo(3));
        }

        // ── Frames ───────────────────────────────────────────────────────────────

        [Test]
        public void FrameOrderIsAuthored_AndRepeatsCostNoSprite()
        {
            var timeline = Timeline(5, 5, TimelineSegmentMode.Stretch, TimelineSegmentMode.Stretch,
                                    (0, 0.1f), (1, 0.1f), (2, 0.1f), (1, 0.1f), (2, 0.1f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 1f, channel: 0f);

            var frames = new List<int>();
            foreach (var step in steps) frames.Add(step.Frame);
            Assert.That(frames, Is.EqualTo(new[] { 0, 1, 2, 1, 2 }),
                "the semi-loop is written in the plan, not baked as duplicated sprites");
        }

        [Test]
        public void ReleaseTime_IsTheSpellsPrepare_WhenThereIsOne()
        {
            var timeline = Timeline(2, 3, TimelineSegmentMode.Stretch, TimelineSegmentMode.Stretch,
                                    (0, 0.1f), (1, 0.1f), (2, 0.1f));

            Assert.That(CastTimelineResolver.ReleaseTime(timeline, 0.9f), Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(CastTimelineResolver.ReleaseTime(timeline, 0f), Is.EqualTo(0.2f).Within(0.0001f),
                "with no authored wind-up the timeline's own is what the editor offers to write");
        }

        [Test]
        public void AnOutOfRangeReleaseStep_ResolvesRatherThanThrowing()
        {
            var timeline = Timeline(99, 99, TimelineSegmentMode.Stretch, TimelineSegmentMode.Stretch,
                                    (0, 0.1f), (1, 0.1f));

            var steps = CastTimelineResolver.Resolve(timeline, prepare: 0.4f, channel: 0.4f);

            Assert.That(Total(steps), Is.EqualTo(0.4f).Within(0.0001f),
                "a clamped release makes the whole plan the wind-up — wrong-looking, but it " +
                "resolves; throwing here would throw inside the render path");
        }
    }
}
