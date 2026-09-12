using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Timeline playback: an authored sequence of frames with their own durations, installed for
    /// one cast and torn down when it ends.
    ///
    /// <para>This is the half that makes an animation and a spell share a clock. The arithmetic —
    /// which frames, in what order, stretched or looped or held over which phase — belongs to
    /// <see cref="CastTimelineResolver"/>, which is pure and testable; what lives here is only
    /// the playing of the list it hands back.</para>
    ///
    /// <para>A timeline REPLACES the frame clock while it is installed, which is why
    /// <see cref="GetStateLength"/> has to answer with its total: that number is what sizes the
    /// cast window, and a window measured from the natural frame count would close in the middle
    /// of a stretched wind-up.</para>
    /// </summary>
    public partial class DirectionalAnimator
    {
        private ResolvedTimelineStep[] _timeline;
        private int   _timelineIndex;
        private float _timelineTimer;
        private bool  _timelineFinished;
        private AnimState _timelineState;
        private int       _timelineVariant = -1;

        /// <summary>True while an authored timeline owns the frames.</summary>
        public bool HasTimeline => _timeline != null && _timeline.Length > 0;

        /// <summary>Total seconds of the installed timeline; 0 when none is.</summary>
        public float TimelineDuration { get; private set; }

        /// <summary>Which step is on screen. Exists for the editor's timeline view, which draws
        /// a playhead, and for tests that step the clock by hand.</summary>
        public int TimelineStepIndex => _timelineIndex;

        /// <summary>True once the last step has been reached; the final frame then holds.</summary>
        public bool TimelineFinished => _timelineFinished;

        /// <summary>How many steps the installed plan has; 0 when none is.</summary>
        public int TimelineStepCount => _timeline?.Length ?? 0;

        /// <summary>One step's frame index and its resolved seconds — what the editor's
        /// timeline view draws, and the only way to see a stretch or a loop as numbers.</summary>
        public ResolvedTimelineStep TimelineStepAt(int index)
            => HasTimeline && index >= 0 && index < _timeline.Length
                ? _timeline[index]
                : default;

        /// <summary>
        /// Parks the plan on one step. Used by the editor's transport: while a timeline owns the
        /// frames, the unit the author scrubs is a STEP — the same frame can appear three times
        /// with three different durations, and a frame index could not tell them apart.
        /// </summary>
        public void ShowTimelineStep(int index)
        {
            if (!HasTimeline) return;
            _timelineIndex    = Mathf.Clamp(index, 0, _timeline.Length - 1);
            _timelineTimer    = 0f;
            _timelineFinished = false;
            ApplyTimelineFrame();
        }

        /// <summary>
        /// Poses the animator and installs <paramref name="steps"/> as its playback.
        ///
        /// <para>The state is set FIRST and the timeline installed after, because
        /// <see cref="SetState"/> clears any timeline it finds: a state change is what ends a
        /// cast, and a plan left running across one would draw the previous spell's frames out
        /// of the new state's set.</para>
        /// </summary>
        public void PlayTimeline(AnimState state, Direction direction, int variant,
                                 IReadOnlyList<ResolvedTimelineStep> steps, bool reversed = false)
        {
            SetState(state, direction, variant, reversed);
            RestartCurrentState();
            InstallTimeline(state, variant, steps);
        }

        private void InstallTimeline(AnimState state, int variant,
                                     IReadOnlyList<ResolvedTimelineStep> steps)
        {
            if (steps == null || steps.Count == 0) { ClearTimeline(); return; }

            _timeline = new ResolvedTimelineStep[steps.Count];
            float total = 0f;
            for (int i = 0; i < steps.Count; i++)
            {
                _timeline[i] = steps[i];
                total += Mathf.Max(0f, steps[i].Seconds);
            }

            TimelineDuration  = total;
            _timelineIndex    = 0;
            _timelineTimer    = 0f;
            _timelineFinished = false;
            _timelineState    = state;
            _timelineVariant  = variant;
            ApplyTimelineFrame();
        }

        /// <summary>Hands the frames back to the ordinary clock.</summary>
        public void ClearTimeline()
        {
            _timeline         = null;
            _timelineIndex    = 0;
            _timelineTimer    = 0f;
            _timelineFinished = false;
            _timelineVariant  = -1;
            TimelineDuration  = 0f;
        }

        /// <summary>
        /// Advances the plan. Steps are consumed in a loop rather than one per tick so a step
        /// shorter than a frame — an eight-millisecond wind-up frame under a 0.05 s prepare, which
        /// the resolver will happily produce because no clamp was wanted — is not silently
        /// stretched to the frame rate.
        /// </summary>
        private void TickTimeline(float deltaTime)
        {
            if (!HasTimeline || _timelineFinished) return;

            _timelineTimer += deltaTime;
            while (_timelineIndex < _timeline.Length &&
                   _timelineTimer >= _timeline[_timelineIndex].Seconds)
            {
                _timelineTimer -= _timeline[_timelineIndex].Seconds;
                _timelineIndex++;

                if (_timelineIndex >= _timeline.Length)
                {
                    // The plan is spent. The last frame HOLDS rather than looping: a timeline
                    // is a gesture with an end, and whoever owns the cast decides what follows.
                    _timelineIndex    = _timeline.Length - 1;
                    _timelineTimer    = 0f;
                    _timelineFinished = true;
                    break;
                }
            }

            ApplyTimelineFrame();
        }

        /// <summary>
        /// Draws the step's frame out of the CURRENT direction's bucket — so a character who
        /// turns mid-cast keeps their place in the plan and simply faces the other way, which is
        /// the same rule <c>RefreshCurrentFrame</c> follows for the ordinary clock.
        /// </summary>
        private void ApplyTimelineFrame()
        {
            if (!HasTimeline) return;

            Sprite[] frames = ResolveFrames(_timelineState, _currentDirection, _timelineVariant);
            if (frames == null || frames.Length == 0) return;

            int index = Mathf.Clamp(_timeline[_timelineIndex].Frame, 0, frames.Length - 1);
            ApplyFrame(frames, index);
        }
    }
}
