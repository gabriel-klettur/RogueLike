using System;
using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Sustained casts: a spell cast again and again (a held fireball, a channelled beam) holds
    /// the gesture inside an authored stretch of frames instead of cycling the whole animation.
    ///
    /// <para>Why a stretch and not the default loop: a cast animation is a gesture with a
    /// wind-up, a climax and a return to rest, and on the shipped art the last two frames are
    /// the rest pose again. Looping all of it while fireballs keep leaving the hands puts the
    /// caster at rest for half of every cycle — measured on the mague, three of every six
    /// fireballs left with his hands folded at his chest. Holding the climax reads as one long
    /// cast that started mid-motion, which is what a repetition is.</para>
    ///
    /// <para>Three rules make it work:</para>
    /// <list type="bullet">
    /// <item>A FRESH cast plays the animation from frame 0, exactly as before. A single click
    /// must still show the whole gesture; only a repetition enters the stretch.</item>
    /// <item>The stretch is ARMED by the repetition, for a short window the caller sizes from
    /// the spell's own cadence (<see cref="SustainRepeat"/>). While armed the cursor wraps
    /// inside it; when the window lapses the tail plays out once and the last frame holds,
    /// and <see cref="RepeatTailFinished"/> tells the owner of the cast it may hand the
    /// animator back.</item>
    /// <item>A repetition that arrives while the tail is already playing jumps back to the
    /// START of the stretch — back into the middle of the motion, never to rest. One that
    /// arrives while the cursor is still inside or before the stretch changes nothing on
    /// screen, so a steady cadence never pops a frame.</item>
    /// </list>
    ///
    /// <para>Reversed playback and an installed timeline opt out: a sheathe does not repeat,
    /// and a timeline replaces the frame clock this rides on.</para>
    /// </summary>
    public partial class DirectionalAnimator
    {
        /// <summary>Clock time until which the stretch is armed; 0 = not armed.</summary>
        private float _repeatUntil;

        /// <summary>True once the tail's last frame has been on screen for a full tick.</summary>
        private bool _repeatTailDone;

        /// <summary>
        /// Test seam for the clock. EditMode delivers no frames, so a fixture that needs the
        /// window to lapse sets this rather than waiting. Instance-scoped on purpose — a static
        /// clock would be mutable state that survives a Play session with Domain Reload off.
        /// </summary>
        internal Func<float> RepeatClockForTests;

        private float RepeatNow => RepeatClockForTests != null ? RepeatClockForTests() : Time.time;

        /// <summary>True while a sustained cast holds the current variant inside its stretch.</summary>
        public bool RepeatArmed => _repeatUntil > 0f && RepeatNow < _repeatUntil;

        /// <summary>
        /// True when the current variant has a repeat stretch, nothing is sustaining it, and its
        /// tail has finished playing. The owner of the cast may end the cast pose now instead of
        /// waiting out a window sized for the whole animation.
        /// </summary>
        public bool RepeatTailFinished => _repeatTailDone && !RepeatArmed;

        /// <summary>True when the variant on screen carries an authored repeat stretch.</summary>
        public bool CurrentVariantRepeats
            => !_playReversed && !HasTimeline && PacingOf(_currentState, _activeVariant).HasRepeat;

        /// <summary>
        /// Arms the current variant's repeat stretch for <paramref name="seconds"/>. Called by
        /// the owner of a cast each time the SAME spell is cast again while its pose is still up.
        ///
        /// <para>If the cursor has already left the stretch and is playing the tail, it jumps
        /// back to the stretch's first frame. Otherwise nothing on screen changes and the wrap in
        /// <see cref="TryAdvanceRepeat"/> keeps the cursor inside.</para>
        /// </summary>
        /// <returns>False when the current animation has no stretch to repeat (or is reversed,
        /// or is driven by a timeline), which leaves every other animation exactly as it was.</returns>
        public bool SustainRepeat(float seconds)
        {
            if (!CurrentVariantRepeats) return false;

            Sprite[] frames = ResolveFrames(_currentState, _currentDirection, _activeVariant);
            if (frames == null || frames.Length < 2) return false;

            RepeatBounds(PacingOf(_currentState, _activeVariant), frames.Length,
                         out int start, out int end);

            bool wasArmed = RepeatArmed;
            _repeatUntil = RepeatNow + Mathf.Max(0f, seconds);

            if (!wasArmed && _frameIndex >= end)
            {
                // Back into the middle of the gesture, drawn now rather than on the next tick:
                // the spell has just fired, and the pose that goes with it is the stretch.
                _frameIndex = start;
                _frameTimer = 0f;
                _repeatTailDone = false;
                AdvanceFrame();
                return true;
            }

            _repeatTailDone = false;
            return true;
        }

        /// <summary>Disarms and forgets the tail. A new animation starts with no repeat.</summary>
        private void ResetRepeat()
        {
            _repeatUntil = 0f;
            _repeatTailDone = false;
        }

        /// <summary>
        /// The frame clock for a variant with a repeat stretch. Returns false for every other
        /// animation so the ordinary branches run unchanged.
        ///
        /// <para>The cursor counts up to <c>frames.Length</c> — one past the last index — and
        /// the drawn frame is clamped. That extra step is what lets the tail's last frame stay on
        /// screen for a full tick before <see cref="RepeatTailFinished"/> goes true, so the owner
        /// ending the pose on that signal never cuts the final frame to nothing.</para>
        /// </summary>
        private bool TryAdvanceRepeat(Sprite[] frames)
        {
            if (_playReversed) return false;

            VariantPacing pace = PacingOf(_currentState, _activeVariant);
            if (!pace.HasRepeat) return false;

            int len = frames.Length;
            RepeatBounds(pace, len, out int start, out int end);
            bool armed = RepeatArmed;

            if (armed && _frameIndex >= end)
                _frameIndex = start;

            int shown = Mathf.Min(_frameIndex, len - 1);
            _repeatTailDone = !armed && _frameIndex >= len;
            ApplyFrame(frames, shown);

            if (_frameIndex < len)
                _frameIndex++;
            return true;
        }

        /// <summary>
        /// The stretch as a half-open range [start, end), clamped into the direction's frames.
        /// Authoring past the end of a short direction bucket resolves to a smaller stretch
        /// rather than throwing in the render path, and a stretch is never empty.
        /// </summary>
        private static void RepeatBounds(VariantPacing pace, int length, out int start, out int end)
        {
            start = Mathf.Clamp(pace.RepeatFrom, 0, length - 1);
            end   = Mathf.Clamp(start + pace.RepeatFrameCount, start + 1, length);
        }
    }
}
