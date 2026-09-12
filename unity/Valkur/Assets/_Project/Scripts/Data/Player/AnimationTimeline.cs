using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// How one segment of a timeline reconciles its authored duration with the duration of the
    /// spell phase it covers.
    /// </summary>
    public enum TimelineSegmentMode
    {
        /// <summary>Scale the segment so it lasts exactly as long as the phase. The authored
        /// durations become RATIOS — the internal rhythm survives, the total is the phase's.
        /// Right for a wind-up whose frames should spread over whatever the spell asks for.</summary>
        Stretch = 0,

        /// <summary>Play at the authored speed and repeat until the phase ends, cutting mid-way
        /// if the phase ends inside a step. Right for a long charge drawn as a short cycle —
        /// three seconds of wind-up out of six frames.</summary>
        Loop = 1,

        /// <summary>Play once at the authored speed and hold the last frame for the remainder.
        /// Right for a gesture that ARRIVES somewhere: repeating it reads as a stutter.</summary>
        Hold = 2,
    }

    /// <summary>One step of a timeline: which frame, and how long it is authored to last.</summary>
    [Serializable]
    public class AnimationTimelineStep
    {
        [Tooltip("Index INSIDE the direction bucket — frame 2 of this direction, not of the " +
                 "linear sheet. Repeating an index costs no sprite and no re-import, which is " +
                 "what makes a semi-loop authorable: 0,1,2,1,2,1,2,3.")]
        public int frame;

        [Tooltip("Seconds this step is authored to last. Under Stretch it is read as a RATIO " +
                 "against its segment's other steps; under Loop and Hold it is the real time.")]
        [Min(0.001f)] public float duration = 0.15f;
    }

    /// <summary>
    /// The authored plan for ONE animation: which frames play, in what order, for how long, and
    /// where the spell's phases cut it.
    ///
    /// <para>It exists because an animation and a cast were two clocks that never met. The spell
    /// owns WHEN it fires (<c>prepareDuration</c> then <c>channelDuration</c>); the timeline owns
    /// WHICH frame is on screen at each of those moments. <see cref="releaseStep"/> is the
    /// contract point between them: the boundary between the wind-up and the rest IS the instant
    /// the executor runs.</para>
    ///
    /// <para>It lives on the VARIANT rather than on the spell, for the reason
    /// <c>castMuzzle</c> lives on the creature: a <c>SpellDefinition</c> is shared by everything
    /// that casts it and knows nothing about anyone's art. <c>CastVariant.spellKeys</c> already
    /// names one animation for one spell on one character, which is exactly the scope a
    /// coordinated cast needs.</para>
    /// </summary>
    [Serializable]
    public class AnimationTimeline
    {
        [Tooltip("The frames to play, in order. Empty means no timeline — the animation plays " +
                 "its natural frames at the entity's frame rate, exactly as before.")]
        public List<AnimationTimelineStep> steps = new List<AnimationTimelineStep>();

        [Tooltip("First step of the LAUNCH segment: everything before it is the wind-up that " +
                 "covers prepareDuration, and this step is the one on screen when the spell " +
                 "actually fires. 0 = no wind-up.")]
        public int releaseStep;

        [Tooltip("First step of the RECOVERY segment: everything between release and this " +
                 "covers channelDuration. Equal to the step count = no recovery.")]
        public int recoverStep = int.MaxValue;

        [Tooltip("How the wind-up fills prepareDuration.")]
        public TimelineSegmentMode prepareMode = TimelineSegmentMode.Stretch;

        [Tooltip("How the launch segment fills channelDuration.")]
        public TimelineSegmentMode channelMode = TimelineSegmentMode.Stretch;

        [Tooltip("How the recovery segment plays. It has no phase to fill — it runs at its " +
                 "authored speed — so only Hold and Loop differ here.")]
        public TimelineSegmentMode recoverMode = TimelineSegmentMode.Hold;

        public bool HasSteps => steps != null && steps.Count > 0;

        /// <summary>Total authored seconds, ignoring any phase it may be fitted to.</summary>
        public float AuthoredDuration
        {
            get
            {
                if (!HasSteps) return 0f;
                float total = 0f;
                for (int i = 0; i < steps.Count; i++)
                    if (steps[i] != null) total += Mathf.Max(0.001f, steps[i].duration);
                return total;
            }
        }

        /// <summary>Authored seconds of the wind-up — what the "write these times onto the
        /// spell" action offers as <c>prepareDuration</c>.</summary>
        public float AuthoredPrepareDuration => AuthoredSpan(0, ClampedRelease);

        /// <summary>Authored seconds between the release and the recovery — what that same
        /// action offers as <c>channelDuration</c>.</summary>
        public float AuthoredChannelDuration => AuthoredSpan(ClampedRelease, ClampedRecover);

        /// <summary>Release step, clamped into the list. Out-of-range authoring resolves to a
        /// timeline with no wind-up rather than throwing in the render path.</summary>
        public int ClampedRelease
            => !HasSteps ? 0 : Mathf.Clamp(releaseStep, 0, steps.Count);

        /// <summary>Recovery step, clamped to the list and never before the release.</summary>
        public int ClampedRecover
            => !HasSteps ? 0 : Mathf.Clamp(recoverStep, ClampedRelease, steps.Count);

        private float AuthoredSpan(int from, int to)
        {
            if (!HasSteps) return 0f;
            float total = 0f;
            for (int i = from; i < to && i < steps.Count; i++)
                if (steps[i] != null) total += Mathf.Max(0.001f, steps[i].duration);
            return total;
        }
    }
}
