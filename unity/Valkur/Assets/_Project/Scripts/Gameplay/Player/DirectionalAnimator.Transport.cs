using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Transport: pause, step, and the answer to "which frame is on screen right now".
    ///
    /// <para>Written for the Entities editor's frame strip, and deliberately inert for every
    /// other animator in the game — <see cref="Paused"/> defaults to false and nothing in the
    /// gameplay code writes it. It belongs on the animator rather than on the editor because
    /// the loop rules (idle holds frame 0, walk skips it, death holds the last, a variant may
    /// hold) live here: a strip that stepped frames itself would be a second implementation of
    /// those rules and would disagree with the game about what "the next frame" is.</para>
    /// </summary>
    public partial class DirectionalAnimator
    {
        /// <summary>The frame index last handed to the renderer. See <c>ApplyFrame</c> for why
        /// this is not <c>_frameIndex</c>.</summary>
        private int _displayedFrameIndex;

        /// <summary>
        /// Frozen: <see cref="Update"/> stops advancing the clock, and nothing else changes.
        /// The current frame stays on screen, a state change still re-poses, and
        /// <see cref="ShowFrame"/> still works — which is what makes scrubbing possible.
        /// </summary>
        public bool Paused { get; set; }

        /// <summary>Index of the frame currently rendered, within the frames of the current
        /// state, direction and variant. 0 before anything has been drawn.</summary>
        public int DisplayedFrameIndex => _displayedFrameIndex;

        /// <summary>
        /// The frames one state/direction/variant is made of, resolved exactly as the render
        /// path resolves them — including the fallbacks, so a strip shows the frames the entity
        /// WILL play rather than the frames its asset happens to list.
        /// Null when nothing is wired, which a caller reads as zero rather than as one.
        /// </summary>
        public Sprite[] FramesFor(AnimState state, Direction direction, int variant = -1)
            => ResolveFrames(state, direction, variant);

        /// <summary>How many frames the CURRENT state, direction and variant carry.</summary>
        public int CurrentFrameCount
        {
            get
            {
                Sprite[] frames = ResolveFrames(_currentState, _currentDirection, _activeVariant);
                return frames?.Length ?? 0;
            }
        }

        /// <summary>
        /// Draws one specific frame of the current state, direction and variant, and leaves the
        /// cursor there so resuming continues from it rather than from where the clock had got
        /// to. Out-of-range indices are clamped: a strip cannot ask for a frame that is not
        /// there, and a caller that does is asking for the nearest end.
        /// </summary>
        public void ShowFrame(int index)
        {
            // While a timeline owns the frames, a step is the unit the author is looking at —
            // the strip draws steps, not raw frames, and scrubbing has to move the same thing.
            if (HasTimeline) { ShowTimelineStep(index); return; }

            Sprite[] frames = ResolveFrames(_currentState, _currentDirection, _activeVariant);
            if (frames == null || frames.Length == 0) return;

            int clamped = Mathf.Clamp(index, 0, frames.Length - 1);
            _frameIndex = clamped;
            _frameTimer = 0f;
            ApplyFrame(frames, clamped);
        }

        /// <summary>
        /// Steps by <paramref name="delta"/> frames, wrapping. Wrapping rather than clamping
        /// because a strip's "next" button on the last frame of a looping cycle means the first
        /// frame — which is what the animation itself does.
        /// </summary>
        public void StepFrame(int delta)
        {
            if (HasTimeline)
            {
                int steps = TimelineStepCount;
                if (steps <= 0) return;
                int nextStep = (TimelineStepIndex + delta) % steps;
                if (nextStep < 0) nextStep += steps;
                ShowTimelineStep(nextStep);
                return;
            }

            Sprite[] frames = ResolveFrames(_currentState, _currentDirection, _activeVariant);
            if (frames == null || frames.Length == 0) return;

            int next = (_displayedFrameIndex + delta) % frames.Length;
            if (next < 0) next += frames.Length;
            ShowFrame(next);
        }
    }
}
