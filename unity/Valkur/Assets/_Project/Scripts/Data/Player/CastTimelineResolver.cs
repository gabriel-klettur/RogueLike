using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>One resolved step: the frame to draw and the real seconds it stays there.</summary>
    public struct ResolvedTimelineStep
    {
        public int Frame;
        public float Seconds;
    }

    /// <summary>
    /// Turns an authored <see cref="AnimationTimeline"/> plus a spell's phase durations into the
    /// flat list of frames and seconds the animator plays.
    ///
    /// <para>Pure and static on purpose. Every decision about how an animation meets a cast —
    /// stretching a wind-up over a 1.2 s summon, looping six frames across three seconds,
    /// holding a landing pose — is arithmetic, and arithmetic that decides when damage happens
    /// belongs somewhere a test can reach without a scene, an animator or a Play Mode.</para>
    ///
    /// <para>There is NO clamp on the scale factor. That is deliberate and was asked for: a
    /// 0.05 s wind-up over six frames really does resolve to 8 ms a frame, and the panel shows
    /// the resulting rate rather than quietly refusing to honour it.</para>
    /// </summary>
    public static class CastTimelineResolver
    {
        /// <summary>Floor for one resolved step. Zero-length steps would spin the player's
        /// cursor without ever advancing the clock.</summary>
        private const float MIN_STEP = 0.0005f;

        /// <summary>Cap on how many times a Loop segment may repeat, so a phase that is long
        /// against a very short cycle cannot allocate without bound. 2000 steps is a 0.01 s
        /// cycle held for twenty seconds.</summary>
        private const int MAX_STEPS = 2000;

        /// <summary>
        /// Resolves the whole plan. <paramref name="prepare"/> and <paramref name="channel"/>
        /// are the spell's own phase durations; a segment with no phase to fill falls back to
        /// its authored length, which is what makes the timeline usable on a spell that authors
        /// neither.
        /// </summary>
        public static List<ResolvedTimelineStep> Resolve(AnimationTimeline timeline,
                                                         float prepare, float channel)
        {
            var resolved = new List<ResolvedTimelineStep>();
            if (timeline == null || !timeline.HasSteps) return resolved;

            int release = timeline.ClampedRelease;
            int recover = timeline.ClampedRecover;

            AppendSegment(resolved, timeline, 0, release, prepare, timeline.prepareMode);
            AppendSegment(resolved, timeline, release, recover, channel, timeline.channelMode);
            // The recovery has no phase of its own: the cooldown is when the spell may be cast
            // AGAIN, not time the caster spends posing, so holding a war cry's twenty seconds
            // would freeze the character out of their own turn.
            AppendSegment(resolved, timeline, recover, timeline.steps.Count, 0f, timeline.recoverMode);
            return resolved;
        }

        /// <summary>The instant the executor fires, measured from the start of the animation —
        /// which is the same number as the spell's own prepareDuration whenever there is one,
        /// and the authored wind-up when there is not.</summary>
        public static float ReleaseTime(AnimationTimeline timeline, float prepare)
        {
            if (timeline == null || !timeline.HasSteps) return Mathf.Max(0f, prepare);
            if (timeline.ClampedRelease <= 0) return 0f;
            return prepare > 0f ? prepare : timeline.AuthoredPrepareDuration;
        }

        private static void AppendSegment(List<ResolvedTimelineStep> into, AnimationTimeline timeline,
                                          int from, int to, float phase, TimelineSegmentMode mode)
        {
            if (from >= to || from >= timeline.steps.Count) return;

            float authored = 0f;
            for (int i = from; i < to && i < timeline.steps.Count; i++)
                if (timeline.steps[i] != null) authored += Mathf.Max(MIN_STEP, timeline.steps[i].duration);
            if (authored <= 0f) return;

            // No phase authored on the spell: every mode degrades to "play it as drawn", which
            // is what makes a timeline safe to author on a spell that fires instantly.
            if (phase <= 0f)
            {
                AppendOnce(into, timeline, from, to, 1f);
                return;
            }

            switch (mode)
            {
                case TimelineSegmentMode.Stretch:
                    AppendOnce(into, timeline, from, to, phase / authored);
                    break;

                case TimelineSegmentMode.Loop:
                    AppendLooped(into, timeline, from, to, phase);
                    break;

                default: // Hold
                    AppendOnce(into, timeline, from, to, 1f);
                    if (phase > authored)
                    {
                        // The last frame carries the remainder. Appended as its own step rather
                        // than lengthened in place so the panel can show the hold for what it is.
                        int last = Mathf.Clamp(to - 1, from, timeline.steps.Count - 1);
                        var step = timeline.steps[last];
                        if (step != null)
                            into.Add(new ResolvedTimelineStep
                            {
                                Frame = step.frame,
                                Seconds = Mathf.Max(MIN_STEP, phase - authored)
                            });
                    }
                    break;
            }
        }

        private static void AppendOnce(List<ResolvedTimelineStep> into, AnimationTimeline timeline,
                                       int from, int to, float scale)
        {
            for (int i = from; i < to && i < timeline.steps.Count; i++)
            {
                var step = timeline.steps[i];
                if (step == null) continue;
                into.Add(new ResolvedTimelineStep
                {
                    Frame = step.frame,
                    Seconds = Mathf.Max(MIN_STEP, Mathf.Max(MIN_STEP, step.duration) * scale)
                });
            }
        }

        /// <summary>
        /// Repeats the segment until the phase is full, CUTTING the final step short rather than
        /// overrunning. Overrunning would push the release past the moment the spell fires, which
        /// is the one thing this whole mechanism exists to pin.
        /// </summary>
        private static void AppendLooped(List<ResolvedTimelineStep> into, AnimationTimeline timeline,
                                         int from, int to, float phase)
        {
            float remaining = phase;
            int guard = 0;

            while (remaining > MIN_STEP && guard < MAX_STEPS)
            {
                for (int i = from; i < to && i < timeline.steps.Count; i++)
                {
                    var step = timeline.steps[i];
                    if (step == null) continue;

                    float seconds = Mathf.Min(Mathf.Max(MIN_STEP, step.duration), remaining);
                    into.Add(new ResolvedTimelineStep { Frame = step.frame, Seconds = seconds });
                    remaining -= seconds;
                    guard++;

                    if (remaining <= MIN_STEP || guard >= MAX_STEPS) break;
                }
            }
        }
    }
}
