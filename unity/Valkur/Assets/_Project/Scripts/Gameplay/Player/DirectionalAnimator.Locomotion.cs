using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The walk and run cycles: paced by how fast the body really moves, looped where the drawing
    /// loops, and announcing the frames a foot lands on.
    ///
    /// <para><b>A FOURTH DIAL, AND IT IS NOT AUTHORED.</b> The entity, state and variant
    /// multipliers are tuning; <see cref="LocomotionRate"/> is a measurement written by whoever
    /// moves the body. It applies to <see cref="AnimState.Walk"/> and <see cref="AnimState.Chase"/>
    /// only and composes with the other three, so an authored idle is untouched.</para>
    ///
    /// <para><b>THE CYCLE IS DATA, LOOKED UP BY THE NAME OF THE FRAMES.</b>
    /// <see cref="LocomotionCycleCatalog"/> is keyed by sheet ("dwarf_armed_running"), because one
    /// drawing is reached from a base state, a variant, a loadout and a Dark twin. It replaces two
    /// guesses. The loop used to skip frame 0 of every walk and run — a Python-era rule for strips
    /// that opened on a standing pose — and in 7 of the 12 shipped cycles frame 0 is part of the
    /// loop (in the elf's run it is one of the two foot contacts, so the rule amputated a step and
    /// made him limp). And footsteps were emitted by distance, blind to which frame plants a foot.
    /// A sheet with no entry keeps the old behaviour exactly.</para>
    /// </summary>
    public partial class DirectionalAnimator
    {
        private float _locomotionRate = 1f;
        private float _walkReferenceSpeed;
        private float _runReferenceSpeed;
        private int _lastContactFrame = -1;
        private bool _suppressContact;

        private readonly Dictionary<Sprite[], LocomotionCycle> _cycleCache = new Dictionary<Sprite[], LocomotionCycle>();

        /// <summary>A foot landed: raised the frame a contact frame of a measured cycle is SHOWN.
        /// The argument is which foot (0 or 1).</summary>
        public event Action<int> FootContact;

        /// <summary>The live playback multiplier on walk and run. 1 = the authored rate.</summary>
        public float LocomotionRate => _locomotionRate;

        public float WalkReferenceSpeed => _walkReferenceSpeed;
        public float RunReferenceSpeed => _runReferenceSpeed;

        /// <summary>True while walk or run plays back to front — the backpedal.</summary>
        public bool IsLocomotionReversed => _playReversed && IsLocomotionState(_currentState);

        public void SetLocomotionRate(float rate)
        {
            _locomotionRate = rate > 0f ? Mathf.Clamp(rate, 0.05f, 8f) : 1f;
        }

        /// <summary>Bound from <c>EntityAssetConfig</c> on every bind (a loadout's run may differ).</summary>
        public void SetLocomotionReference(float walkSpeed, float runSpeed)
        {
            _walkReferenceSpeed = Mathf.Max(0f, walkSpeed);
            _runReferenceSpeed = Mathf.Max(0f, runSpeed);
        }

        private float LocomotionRateFor(AnimState state) =>
            IsLocomotionState(state) ? _locomotionRate : 1f;

        private static bool IsLocomotionState(AnimState state) =>
            state == AnimState.Walk || state == AnimState.Chase;

        /// <summary>Seconds one frame of a state holds WITHOUT the locomotion rate — what a cycle
        /// was drawn to take, used to turn a stride into a speed.</summary>
        public float BaseFrameIntervalFor(AnimState state, int variant)
        {
            float variantSpeed = PacingOf(state, variant).SpeedMultiplier;
            if (variantSpeed <= 0f) variantSpeed = 1f;
            return EffectiveFrameInterval / (StateSpeedOf(state) * variantSpeed);
        }

        // ── Cycle data ───────────────────────────────────────────────────────

        /// <summary>The measured cycle of the frames a state shows in the current direction, or null.</summary>
        public LocomotionCycle CycleFor(AnimState state)
        {
            if (!IsLocomotionState(state)) return null;
            return CycleOf(ResolveFrames(state, _currentDirection, state == _currentState ? _activeVariant : -1));
        }

        /// <summary>The cycle on screen now, or null.</summary>
        public LocomotionCycle CurrentCycle => CycleFor(_currentState);

        /// <summary>
        /// World units the body on screen is OFF the ground: the frame's measured lift above its
        /// cycle's planted frames, over the sprite's own pixels-per-unit. 0 for anything unmeasured
        /// or not moving. Read by the contact shadow, which shrinks while the feet are in the air.
        /// </summary>
        public float CurrentGroundLiftUnits
        {
            get
            {
                if (!IsLocomotionState(_currentState) || targetRenderer == null || targetRenderer.sprite == null) return 0f;
                var cycle = CurrentCycle;
                if (cycle == null) return 0f;
                int px = cycle.LiftAbovePlanted(_displayedFrameIndex);
                return px <= 0 ? 0f : px / Mathf.Max(1f, targetRenderer.sprite.pixelsPerUnit);
            }
        }

        /// <summary>Frames in the loop of the cycle on screen (the whole set when unmeasured).</summary>
        public int CurrentLoopLength
        {
            get
            {
                var frames = ResolveFrames(_currentState, _currentDirection, _activeVariant);
                if (frames == null) return 0;
                return frames.Length - LoopStartOf(CycleOf(frames), frames.Length);
            }
        }

        /// <summary>Frames in the loop of <paramref name="state"/>'s cycle in the current direction.</summary>
        public int LoopLengthFor(AnimState state)
        {
            var frames = ResolveFrames(state, _currentDirection, state == _currentState ? _activeVariant : -1);
            if (frames == null) return 0;
            return frames.Length - LoopStartOf(CycleOf(frames), frames.Length);
        }

        private LocomotionCycle CycleOf(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0) return null;
            if (_cycleCache.TryGetValue(frames, out var cached)) return cached;
            var catalog = LocomotionCycleCatalog.Active;
            var cycle = catalog != null ? catalog.Find(LocomotionCycleCatalog.SheetKeyOf(frames[0])) : null;
            if (cycle != null && cycle.contactFrames != null)
            {
                // A cycle measured on a different frame count (a re-cut sheet) is ignored rather
                // than trusted: an index past the end would plant a foot that never draws.
                foreach (int c in cycle.contactFrames)
                    if (c < 0 || c >= frames.Length) { cycle = null; break; }
            }
            _cycleCache[frames] = cycle;
            return cycle;
        }

        /// <summary>Where the loop starts: measured, or the historical "skip frame 0".</summary>
        private static int LoopStartOf(LocomotionCycle cycle, int length)
        {
            int start = cycle != null ? cycle.loopStart : 1;
            return Mathf.Clamp(start, 0, Mathf.Max(0, length - 2));
        }

        /// <summary>Drops cached cycle lookups — the frame arrays change on every bind.</summary>
        private void ClearCycleCache() => _cycleCache.Clear();

        // ── Playback ─────────────────────────────────────────────────────────

        /// <summary>
        /// One tick of a walk or run: advance within the measured loop, forwards or back, and
        /// announce a contact frame. <c>_frameIndex</c> counts forwards through the loop either way;
        /// reversed playback maps it, the same arrangement <see cref="FrameAt"/> uses.
        /// </summary>
        private void AdvanceLocomotionFrame(Sprite[] frames)
        {
            var cycle = CycleOf(frames);
            int start = LoopStartOf(cycle, frames.Length);
            if (_frameIndex < start || _frameIndex >= frames.Length) _frameIndex = start;

            int shown = ShownLocomotionFrame(_frameIndex, start, frames.Length);
            ApplyFrame(frames, shown);
            AnnounceContact(cycle, shown);

            _frameIndex++;
            if (_frameIndex >= frames.Length) _frameIndex = start;
        }

        /// <summary>
        /// How long the frame on screen holds, as a multiple of the cycle's average: a foot
        /// contact holds <see cref="LocomotionTuning.contactHold"/>, every other frame of the loop
        /// shares out the difference so the loop's total length (and so the pacing the rate was
        /// computed for) is unchanged. 1 for anything unmeasured.
        /// </summary>
        public float LocomotionHoldWeight()
        {
            if (!IsLocomotionState(_currentState)) return 1f;
            var frames = ResolveFrames(_currentState, _currentDirection, _activeVariant);
            var cycle = CycleOf(frames);
            if (cycle == null || !cycle.HasContacts) return 1f;
            float hold = LocomotionTuning.Active.contactHold;
            if (hold <= 1.0001f) return 1f;
            int loop = frames.Length - LoopStartOf(cycle, frames.Length);
            int contacts = cycle.contactFrames.Length;
            if (loop <= contacts) return 1f;
            if (cycle.IsContact(_displayedFrameIndex, out _)) return hold;
            return Mathf.Max(0.2f, (loop - hold * contacts) / (loop - contacts));
        }

        private int ShownLocomotionFrame(int cursor, int start, int length) =>
            _playReversed ? length - 1 - (cursor - start) : cursor;

        private int CursorForShown(int shown, int start, int length) =>
            _playReversed ? start + (length - 1 - shown) : shown;

        private void AnnounceContact(LocomotionCycle cycle, int shown)
        {
            if (cycle == null || !cycle.IsContact(shown, out int foot))
            {
                _lastContactFrame = -1;
                return;
            }
            if (shown == _lastContactFrame) return;
            _lastContactFrame = shown;
            if (!_suppressContact) FootContact?.Invoke(foot);
        }

        /// <summary>
        /// Frames until the next contact is shown, counting the direction of play. 0 when a
        /// contact is on screen; -1 when the cycle is unmeasured.
        /// </summary>
        public int FramesToNextContact()
        {
            if (!IsLocomotionState(_currentState)) return -1;
            var frames = ResolveFrames(_currentState, _currentDirection, _activeVariant);
            var cycle = CycleOf(frames);
            if (cycle == null || !cycle.HasContacts) return -1;
            if (cycle.IsContact(_displayedFrameIndex, out _)) return 0;

            int start = LoopStartOf(cycle, frames.Length);
            int loop = frames.Length - start;
            int cursor = CursorForShown(_displayedFrameIndex, start, frames.Length);
            for (int step = 1; step <= loop; step++)
            {
                int c = start + ((cursor - start + step) % loop);
                if (cycle.IsContact(ShownLocomotionFrame(c, start, frames.Length), out _)) return step;
            }
            return -1;
        }

        // ── Transitions ──────────────────────────────────────────────────────

        /// <summary>
        /// Enters or keeps a locomotion state the way a body moves.
        ///
        /// <list type="bullet">
        /// <item><b>Walk↔run keeps the FOOT, not just the fraction.</b> Measured cycles: the new
        /// cycle resumes at the same progress between the same foot's contact and the next, so the
        /// lead foot never swaps on the transition. Unmeasured: the same fraction of the loop.</item>
        /// <item><b>Starting from a stand lands on a contact</b>, so the first frame of a walk is a
        /// planted foot rather than whatever frame the cursor was left on.</item>
        /// <item><b><paramref name="reversed"/> is the backpedal</b>: the cycle plays back to front,
        /// and flipping it mid-stride keeps the frame on screen.</item>
        /// </list>
        /// Any other state goes through the ordinary <see cref="SetState(AnimState, Direction)"/>.
        /// </summary>
        public void SetLocomotionState(AnimState state, Direction direction, bool reversed = false)
        {
            if (!IsLocomotionState(state) || HasTimeline)
            {
                SetState(state, direction);
                return;
            }

            bool fromLocomotion = IsLocomotionState(_currentState) && _stateEverSet;
            bool swap = fromLocomotion && state != _currentState;
            bool flip = fromLocomotion && state == _currentState && reversed != _playReversed;

            if (fromLocomotion && !swap && !flip)
            {
                // Same state, same playback: a turn at most, which SetState already handles
                // without touching the cursor.
                SetState(state, direction, _activeVariant, reversed);
                return;
            }

            Sprite[] from = fromLocomotion ? ResolveFrames(_currentState, _currentDirection, _activeVariant) : null;
            var fromCycle = CycleOf(from);
            int fromShown = _displayedFrameIndex;
            float timer = _frameTimer;

            int variant = state == _currentState ? _activeVariant : ResolveEntryVariant(state);
            // SetState draws a provisional frame that is replaced below; it must not announce a
            // footfall for a frame that is never really shown.
            _suppressContact = true;
            try { SetState(state, direction, variant, reversed); }
            finally { _suppressContact = false; }

            Sprite[] to = ResolveFrames(_currentState, _currentDirection, _activeVariant);
            if (to == null || to.Length < 2) return;
            var toCycle = CycleOf(to);
            int start = LoopStartOf(toCycle, to.Length);

            int shown;
            if (flip)
                shown = Mathf.Clamp(fromShown, start, to.Length - 1);
            else if (from != null && from.Length >= 2)
                shown = MapPhase(from, fromCycle, fromShown, to, toCycle);
            else if (toCycle != null && toCycle.HasContacts)
                shown = toCycle.contactFrames[0];
            else
                shown = start;

            ApplyFrame(to, shown);
            if (toCycle != null && toCycle.IsContact(shown, out int foot))
            {
                // A foot planted by starting from a stand is a footfall. One the walk-to-run swap
                // or a backpedal flip lands on was already the body's, so it is not announced twice.
                if (!flip && !swap) FootContact?.Invoke(foot);
                _lastContactFrame = shown;
            }
            else
            {
                _lastContactFrame = -1;
            }

            _frameIndex = CursorForShown(shown, start, to.Length) + 1;
            if (_frameIndex >= to.Length) _frameIndex = start;
            if (fromLocomotion)
                _frameTimer = Mathf.Min(timer, FrameIntervalFor(_currentState, _activeVariant) * 0.99f);
        }

        /// <summary>The frame of <paramref name="to"/> at the same place in the stride as
        /// <paramref name="fromShown"/> of <paramref name="from"/>.</summary>
        private static int MapPhase(Sprite[] from, LocomotionCycle fromCycle, int fromShown,
                                    Sprite[] to, LocomotionCycle toCycle)
        {
            int fromStart = LoopStartOf(fromCycle, from.Length);
            int toStart = LoopStartOf(toCycle, to.Length);
            int fromLoop = from.Length - fromStart;
            int toLoop = to.Length - toStart;

            bool byContact = fromCycle != null && toCycle != null &&
                             fromCycle.contactFrames != null && toCycle.contactFrames != null &&
                             fromCycle.contactFrames.Length >= 2 && toCycle.contactFrames.Length >= 2;
            if (!byContact)
            {
                float phase = Mathf.Clamp01((fromShown - fromStart) / Mathf.Max(1f, fromLoop));
                return toStart + Mathf.Clamp(Mathf.RoundToInt(phase * toLoop), 0, toLoop - 1);
            }

            // Which foot's step are we in, and how far through it?
            int foot = 0;
            int bestDist = int.MaxValue;
            for (int i = 0; i < fromCycle.contactFrames.Length; i++)
            {
                int d = Mod(fromShown - fromCycle.contactFrames[i], fromLoop);
                if (d < bestDist) { bestDist = d; foot = i; }
            }
            int fromNext = fromCycle.contactFrames[(foot + 1) % fromCycle.contactFrames.Length];
            int fromStep = Mod(fromNext - fromCycle.contactFrames[foot], fromLoop);
            if (fromStep == 0) fromStep = fromLoop;
            float t = bestDist / (float)fromStep;

            int toFoot = Mathf.Min(foot, toCycle.contactFrames.Length - 1);
            int toNext = toCycle.contactFrames[(toFoot + 1) % toCycle.contactFrames.Length];
            int toStep = Mod(toNext - toCycle.contactFrames[toFoot], toLoop);
            if (toStep == 0) toStep = toLoop;
            int offset = Mathf.Clamp(Mathf.RoundToInt(t * toStep), 0, toStep - 1);
            return toStart + Mod(toCycle.contactFrames[toFoot] - toStart + offset, toLoop);
        }

        private static int Mod(int a, int m) => m <= 0 ? 0 : ((a % m) + m) % m;
    }
}
