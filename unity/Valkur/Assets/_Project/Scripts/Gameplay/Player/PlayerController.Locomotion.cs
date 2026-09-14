using System;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Player;
using Valkur.Gameplay.Skills;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Walking into a run: the seam between <see cref="LocomotionGait"/> (the pure rule),
    /// <see cref="Energy"/> (what running spends), the animator (walk or run, at what rate) and the
    /// <c>athletics</c> skill (what running teaches).
    ///
    /// <para><b>ONE STEP PER PHYSICS STEP, MEASURED.</b> The real speed handed to the gait is the
    /// body's displacement since the previous <c>FixedUpdate</c>, not the velocity this code wrote:
    /// a wall, a collider and the void clamp all take speed away after the write, and the whole
    /// point of the gait's "keeping up" test is to notice that.</para>
    ///
    /// <para><b>EVERY READER LISTENS, NOBODY POLLS THE FLAGS.</b> The gait's event flags live for
    /// one physics step, and a visual polling them from <c>Update</c> would miss a step at high
    /// frame rates and read one twice at low ones. They are raised here as C# events instead, the
    /// moment the step that produced them ends.</para>
    /// </summary>
    public partial class PlayerController
    {
        private LocomotionGait _gait;
        private AthleticsTrainer _athleticsTrainer;
        private Energy _energy;
        private Vector2 _lastFixedPosition;
        private bool _hasLastFixedPosition;
        private float _measuredSpeed;

        /// <summary>Raised the step the body breaks into a run.</summary>
        public event Action RunStarted;

        /// <summary>Raised when momentum is thrown away, with why and how much was lost.</summary>
        public event Action<GaitBreak, float> MomentumBroken;

        /// <summary>Raised the step energy runs out mid-run.</summary>
        public event Action BecameWinded;

        /// <summary>Raised when the winded lock lifts.</summary>
        public event Action WindRecovered;

        /// <summary>The live gait, for the HUD, the ground mark and the <c>run</c> probe. Never null.</summary>
        public LocomotionGait Gait => EnsureGait();

        /// <summary>The body's measured speed last physics step, world units per second.</summary>
        public float MeasuredSpeed => _measuredSpeed;

        /// <summary>The walking speed the stat layer resolved, with no run multiplier.</summary>
        public float WalkSpeed => moveSpeed;

        /// <summary>True while the feet are at a run.</summary>
        public bool IsRunning => _gait != null && _gait.State == GaitState.Run;

        /// <summary>The energy pool, or null on an entity that has none.</summary>
        public Energy Energy
        {
            get
            {
                if (_energy == null)
                {
                    _energy = GetComponent<Energy>();
                    if (_energy != null) _energy.SetExternallyRegulated(true);
                }
                return _energy;
            }
        }

        /// <summary>Athletics, 0..1. Reads without creating a skills component.</summary>
        public float AthleticsSkill01
        {
            get
            {
                var skills = PlayerSkills.Peek(gameObject);
                return skills == null ? 0f : skills.GetPercent(LocomotionTuning.SkillKey) / 100f;
            }
        }

        private LocomotionGait EnsureGait()
        {
            if (_gait == null)
            {
                var tuning = LocomotionTuning.Active;
                _gait = new LocomotionGait(tuning);
                _athleticsTrainer = new AthleticsTrainer(tuning);
            }
            return _gait;
        }

        /// <summary>The run multiplier this step. 1 whenever the gait is not running.</summary>
        private float LocomotionSpeedMultiplier => EnsureGait().SpeedMultiplier;

        /// <summary>
        /// Measures, steps the gait, applies its energy and skill, and answers the velocity to write.
        /// Called from <c>FixedUpdate</c> only on the branch where the player is free to walk.
        /// </summary>
        private Vector2 StepLocomotion(Vector2 clampedInput)
        {
            var gait = EnsureGait();
            float dt = Time.fixedDeltaTime;
            MeasureSpeed(dt);

            var energy = Energy;
            float skill01 = AthleticsSkill01;

            var input = new GaitInput
            {
                DeltaTime = dt,
                Desired = clampedInput,
                WalkSpeed = moveSpeed,
                RealSpeed = _measuredSpeed,
                // No pool at all (a test rig, a future NPC with the controller) runs forever
                // rather than never: the energy half is an addition, not a gate on the gait.
                Energy01 = energy != null ? energy.Normalized : 1f,
                Skill01 = skill01,
                Backpedal = IsBackpedalInput(clampedInput),
            };
            gait.Step(input);

            if (energy != null)
            {
                if (gait.EnergyDelta < 0f) energy.Drain(-gait.EnergyDelta);
                else if (gait.EnergyDelta > 0f) energy.Regenerate(gait.EnergyDelta);
            }

            TrainAthletics(gait, skill01);
            RaiseGaitEvents(gait);
            PaceLocomotionAnimation(gait);

            return EaseBodyVelocity(clampedInput, dt);
        }

        /// <summary>
        /// Moving AWAY from where the character faces. Facing belongs to the cursor (aiming wins,
        /// see PlayerFacingResolver), so walking away from it used to play the forward cycle while
        /// the body went backwards: a moonwalk, at full run speed. Past the tuning's angle the
        /// cycle plays reversed at a fraction of the walk and never builds a run.
        /// </summary>
        private bool IsBackpedalInput(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f || _facingDirection.sqrMagnitude < 0.0001f) return false;
            return Vector2.Angle(input, _facingDirection) > LocomotionTuning.Active.backpedalAngle;
        }

        /// <summary>True when the locomotion cycle on screen should play back to front.</summary>
        private bool LocomotionReversed => IsMoving && IsBackpedalInput(_moveInput);

        private float _stoppedAt = -1f;
        private float _speedEnvelope;
        private Vector2 _lastMoveDirection;

        private Vector2 _debugMove;
        private float _debugMoveUntil = -1f;

        /// <summary>
        /// Walks the player without a keyboard for <paramref name="seconds"/> of GAME time — the
        /// <c>autowalk</c> console verb. It exists so the look of walking and running can be
        /// captured and judged from a probe (Time.timeScale slowed, screenshots between calls); a
        /// feel nobody can reproduce on demand is a feel nobody can polish. Zero seconds stops it.
        /// </summary>
        public void SetDebugMove(Vector2 direction, float seconds)
        {
            _debugMove = direction.sqrMagnitude > 1f ? direction.normalized : direction;
            _debugMoveUntil = seconds > 0f ? Time.time + seconds : -1f;
        }

        /// <summary>True while a debug walk is overriding the keys.</summary>
        public bool HasDebugMove => _debugMoveUntil > 0f && Time.time < _debugMoveUntil;

        /// <summary>
        /// A body with a little mass: it reaches walking speed in
        /// <see cref="LocomotionTuning.startEaseSeconds"/> and stops over
        /// <see cref="LocomotionTuning.stopEaseSeconds"/>, still heading where it last went (and
        /// still refused by the void clamp). A change of direction while moving is instant, because
        /// that is what the controls are for. Before this a stop was a freeze on the frame the key
        /// came up, while the legs went on finishing their step: legs moving under a still body is
        /// exactly what sliding looks like.
        /// </summary>
        private Vector2 EaseBodyVelocity(Vector2 clampedInput, float dt)
        {
            var tuning = LocomotionTuning.Active;
            if (clampedInput.sqrMagnitude > 0.0001f)
            {
                _lastMoveDirection = clampedInput;
                _speedEnvelope = tuning.startEaseSeconds <= 0f
                    ? 1f : Mathf.Min(1f, _speedEnvelope + dt / tuning.startEaseSeconds);
                return clampedInput * CurrentMoveSpeed * _speedEnvelope;
            }

            _speedEnvelope = tuning.stopEaseSeconds <= 0f
                ? 0f : Mathf.Max(0f, _speedEnvelope - dt / tuning.stopEaseSeconds);
            if (_speedEnvelope <= 0f || _lastMoveDirection.sqrMagnitude < 0.0001f) return Vector2.zero;
            return ClampInputAgainstVoid(_lastMoveDirection) * CurrentMoveSpeed * _speedEnvelope;
        }

        private void MeasureSpeed(float dt)
        {
            Vector2 pos = _rb != null ? _rb.position : (Vector2)transform.position;
            _measuredSpeed = _hasLastFixedPosition && dt > 0f ? (pos - _lastFixedPosition).magnitude / dt : 0f;
            _lastFixedPosition = pos;
            _hasLastFixedPosition = true;
        }

        /// <summary>
        /// Throws momentum away from the controller's own early returns (stunned, rooted, planted
        /// by a cast, a ghost). The position is forgotten too: the next free step must not measure
        /// a teleport or a knock-back as a sprint.
        /// </summary>
        private void BreakLocomotion(GaitBreak reason)
        {
            _hasLastFixedPosition = false;
            // A stun, a root or a planting cast stops the body dead, not over a glide.
            _speedEnvelope = 0f;
            if (_gait == null) return;
            bool had = _gait.Momentum > 0f || _gait.State == GaitState.Run;
            float lost = _gait.Momentum;
            _gait.Break(reason);
            if (had) MomentumBroken?.Invoke(reason, lost);
            if (_gait.State == GaitState.Run) return;
            if (_animator != null) _animator.SetLocomotionRate(1f);
        }

        private void HandleLocomotionHit(int amount)
        {
            if (amount <= 0) return;
            BreakLocomotion(GaitBreak.Hit);
        }

        private void TrainAthletics(LocomotionGait gait, float skill01)
        {
            if (gait.RunDistance <= 0f || _athleticsTrainer == null) return;
            int rolls = _athleticsTrainer.Accumulate(gait.RunDistance, Time.time);
            if (rolls <= 0) return;

            var def = SkillCatalog.Shared != null ? SkillCatalog.Shared.Find(LocomotionTuning.SkillKey) : null;
            var skills = def != null ? PlayerSkills.For(gameObject) : null;
            if (skills == null) return;

            // The difficulty IS the current skill: running has no node to be hard or trivial
            // against, so the band multiplier stays at 1 and only the decay curve shapes the climb.
            int difficulty = Mathf.RoundToInt(skill01 * 100f);
            for (int i = 0; i < rolls; i++) skills.TryGain(def, difficulty, false);
        }

        private void RaiseGaitEvents(LocomotionGait gait)
        {
            if (gait.StartedRun) RunStarted?.Invoke();
            if (gait.BrokeThisStep) MomentumBroken?.Invoke(gait.LastBreak, gait.LostMomentum);
            if (gait.BecameWinded) BecameWinded?.Invoke();
            if (gait.RecoveredWind) WindRecovered?.Invoke();
        }

        /// <summary>
        /// Walk or run — and at what rate, so the feet land where the body goes.
        /// The run cycle shows from the moment the gait runs and stays while the speed is still
        /// more run than walk, so easing out of a run does not snap back to a walk at full speed.
        /// </summary>
        private DirectionalAnimator.AnimState ResolveLocomotionAnimState()
        {
            if (!IsMoving)
            {
                if (_stoppedAt < 0f) _stoppedAt = Time.time;
                return ShouldSettleStride() ? _animator.CurrentState : DirectionalAnimator.AnimState.Idle;
            }
            _stoppedAt = -1f;
            if (_gait != null && (_gait.State == GaitState.Run || _gait.RunBlend > 0.5f))
                return DirectionalAnimator.AnimState.Chase;
            return DirectionalAnimator.AnimState.Walk;
        }

        /// <summary>
        /// Letting go mid-stride finishes the step: the legs play on to the next foot contact (at
        /// most <see cref="LocomotionTuning.settleMaxFrames"/>) before the idle pose, instead of a
        /// raised leg vanishing in one frame. The BODY is already still; only the drawing settles.
        /// A measured cycle is required; an unmeasured one stops at once as it always did.
        /// </summary>
        private bool ShouldSettleStride()
        {
            if (_animator == null) return false;
            var state = _animator.CurrentState;
            if (state != DirectionalAnimator.AnimState.Walk && state != DirectionalAnimator.AnimState.Chase) return false;
            int toContact = _animator.FramesToNextContact();
            int budget = LocomotionTuning.Active.settleMaxFrames;
            if (toContact <= 0 || toContact > budget) return false;
            float window = (budget + 0.5f) * _animator.BaseFrameIntervalFor(state, -1);
            return Time.time - _stoppedAt < window;
        }

        private void PaceLocomotionAnimation(LocomotionGait gait)
        {
            if (_animator == null) return;
            var tuning = LocomotionTuning.Active;

            // A settling stride plays at its drawn rate: the body is still, so "speed" is zero and
            // would crawl the last frames of a step that should simply finish.
            if (!IsMoving)
            {
                _animator.SetLocomotionRate(1f);
                return;
            }

            var state = gait.State == GaitState.Run || gait.RunBlend > 0.5f
                ? DirectionalAnimator.AnimState.Chase
                : DirectionalAnimator.AnimState.Walk;

            // Speed asked for, not measured, when the body is moving freely: a physics step's
            // displacement jitters by a few percent and the cycle would wobble with it. The
            // measurement only takes over when it is clearly lower (a wall, a slide).
            float asked = moveSpeed * gait.SpeedMultiplier;
            float speed = _measuredSpeed < asked * 0.85f ? _measuredSpeed : asked;

            _animator.SetLocomotionRate(ResolveLocomotionRate(_animator, state, speed, moveSpeed, tuning));
        }

        /// <summary>
        /// How fast a cycle must play for its feet to land where a body moving at
        /// <paramref name="speed"/> goes.
        ///
        /// <para><b>Measured cycle</b> (<see cref="LocomotionCycle.strideUnits"/>): a loop of N frames
        /// carries one step per contact, so at the drawn rate it covers <c>contacts x stride</c> in
        /// <c>N x frameInterval</c>. The rate that plants the feet is speed over that, capped in
        /// FRAMES PER SECOND (14 walking, 18 running): past it legs blur, and whatever the cap does
        /// not absorb is residual skate.</para>
        ///
        /// <para><b>Unmeasured</b>: an authored reference speed if the art has one, else the class
        /// speed at the gait's own fraction, clamped to the historical range.</para>
        /// </summary>
        public static float ResolveLocomotionRate(DirectionalAnimator animator, DirectionalAnimator.AnimState state,
                                                  float speed, float classSpeed, LocomotionTuning tuning)
        {
            bool run = state == DirectionalAnimator.AnimState.Chase;
            float baseInterval = animator.BaseFrameIntervalFor(state, -1);
            var cycle = animator.CycleFor(state);

            if (cycle != null && cycle.strideUnits > 0f && baseInterval > 0f)
            {
                int loop = Mathf.Max(2, animator.LoopLengthFor(state));
                float steps = cycle.HasContacts ? cycle.contactFrames.Length : 2;
                float drawnSpeed = steps * cycle.strideUnits / (loop * baseInterval);
                float cap = (run ? tuning.runMaxFps : tuning.walkMaxFps) * baseInterval;
                return Mathf.Clamp(speed / drawnSpeed, tuning.locomotionRateMin, Mathf.Max(tuning.locomotionRateMin, cap));
            }

            float reference = run
                ? (animator.RunReferenceSpeed > 0f ? animator.RunReferenceSpeed : classSpeed * tuning.runReferenceFactor)
                : (animator.WalkReferenceSpeed > 0f ? animator.WalkReferenceSpeed : classSpeed * tuning.walkSpeedFraction);
            float rate = reference > 0f ? speed / reference : 1f;
            return Mathf.Clamp(rate, tuning.locomotionRateMin, tuning.locomotionRateMax);
        }
    }
}
