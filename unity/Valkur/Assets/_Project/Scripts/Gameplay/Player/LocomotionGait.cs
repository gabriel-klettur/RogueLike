using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Player
{
    /// <summary>What the feet are doing.</summary>
    public enum GaitState
    {
        Idle = 0,
        Walk = 1,
        Run  = 2,
    }

    /// <summary>Why momentum was thrown away — shown by the ground mark, printed by <c>run</c>.</summary>
    public enum GaitBreak
    {
        None = 0,
        Stopped,
        Turned,
        Blocked,
        Hit,
        Cast,
        Winded,
        Disabled,
        Backpedal,
    }

    /// <summary>One step's worth of what the controller knows.</summary>
    public struct GaitInput
    {
        /// <summary>Seconds this step covers.</summary>
        public float DeltaTime;

        /// <summary>Where the player is pushing. Zero when nothing is held.</summary>
        public Vector2 Desired;

        /// <summary>The class speed the stat layer resolved (MoveSpeed). Walking, running and
        /// backpedalling are fractions of it.</summary>
        public float WalkSpeed;

        /// <summary>True while the body moves away from where it faces.</summary>
        public bool Backpedal;

        /// <summary>The body's MEASURED speed last step, world units per second.</summary>
        public float RealSpeed;

        /// <summary>Energy fill, 0..1.</summary>
        public float Energy01;

        /// <summary>Athletics skill, 0..1.</summary>
        public float Skill01;
    }

    /// <summary>
    /// Walking into a run, as a pure state machine: no MonoBehaviour, no clock, no physics — the
    /// controller hands it one <see cref="GaitInput"/> per physics step and reads the answer.
    /// Pure for the reason <c>MarketCycle</c> and <c>HarvestNode.StepSessionForTests</c> are: a
    /// feel that can only be checked by playing is a feel nobody can pin.
    ///
    /// <para><b>MOMENTUM IS EARNED BY DISPLACEMENT, NOT BY INPUT.</b> <c>PlayerController.IsMoving</c>
    /// answers "is a key held", which is true for a player pushing into a wall. Momentum only
    /// builds while the measured speed keeps up with what was asked, so a wall builds nothing and
    /// the skill cannot be trained by holding W against a house.</para>
    ///
    /// <para><b>SIX WAYS TO LOSE IT</b> (<see cref="GaitBreak"/>): letting go past a short grace,
    /// a turn sharper than the skill tolerates, being blocked, being hit, a cast that plants the
    /// feet, running out of energy. Disabled (stunned, rooted, a ghost) is the seventh and is not
    /// a choice. The DASH is deliberately not one of them: the controller simply skips the step
    /// while the body is teleporting, so a dash out of a run lands still running.</para>
    ///
    /// <para><b>WINDED HAS HYSTERESIS.</b> An empty pool drops the body to a walk and locks momentum
    /// until the pool is back to <see cref="LocomotionTuning.windedRecoverFraction"/>. Without the
    /// gap an empty bar refills a sliver, the run restarts, drains it, and the character flickers
    /// between two gaits every few frames.</para>
    /// </summary>
    public sealed class LocomotionGait
    {
        private readonly LocomotionTuning _tuning;

        private Vector2 _heading;
        private float _graceTimer;
        private float _blockedTimer;
        private float _sinceRunEnded = float.PositiveInfinity;

        public LocomotionGait(LocomotionTuning tuning)
        {
            _tuning = tuning;
            SpeedMultiplier = tuning.walkSpeedFraction;
        }

        public GaitState State { get; private set; } = GaitState.Idle;

        /// <summary>0..1. Reaching 1 while walking breaks into a run.</summary>
        public float Momentum { get; private set; }

        /// <summary>0..1 ease between the walk speed and the run speed.</summary>
        public float RunBlend { get; private set; }

        public bool IsWinded { get; private set; }

        /// <summary>Seconds spent at full run without interruption.</summary>
        public float SecondsAtFullRun { get; private set; }

        /// <summary>What to multiply the class speed by this step: the walk fraction at a walk,
        /// the run multiplier at a run, eased between, times the backpedal fraction.</summary>
        public float SpeedMultiplier { get; private set; } = 1f;

        /// <summary>True while the last step was a backpedal.</summary>
        public bool IsBackpedalling { get; private set; }

        /// <summary>The energy this step spent (negative) or regained (positive).</summary>
        public float EnergyDelta { get; private set; }

        /// <summary>World units covered while running, this step only.</summary>
        public float RunDistance { get; private set; }

        // One-step event flags. Cleared at the start of every Step, so a reader polls them
        // right after calling it and never sees yesterday's news.
        public bool StartedRun { get; private set; }
        public bool EndedRun { get; private set; }
        public bool BecameWinded { get; private set; }
        public bool RecoveredWind { get; private set; }
        public GaitBreak LastBreak { get; private set; }
        public bool BrokeThisStep { get; private set; }

        /// <summary>The momentum that was thrown away by the last break, for the ground mark's cascade.</summary>
        public float LostMomentum { get; private set; }

        /// <summary>The last heading the body committed to.</summary>
        public Vector2 Heading => _heading;

        /// <summary>
        /// Throws momentum away from outside — a blow landed, a cast planted the feet, a stun.
        /// Safe to call at any time; a break with nothing to lose still records its reason.
        /// </summary>
        public void Break(GaitBreak reason)
        {
            if (reason == GaitBreak.None) return;
            LostMomentum = Momentum;
            if (Momentum > 0f || State == GaitState.Run) BrokeThisStep = true;
            LastBreak = reason;
            Momentum = 0f;
            if (State == GaitState.Run)
            {
                State = GaitState.Walk;
                EndedRun = true;
                _sinceRunEnded = 0f;
                SecondsAtFullRun = 0f;
            }
        }

        public void Step(in GaitInput input)
        {
            StartedRun = EndedRun = BecameWinded = RecoveredWind = BrokeThisStep = false;
            EnergyDelta = 0f;
            RunDistance = 0f;

            float dt = Mathf.Max(0f, input.DeltaTime);
            float skill = Mathf.Clamp01(input.Skill01);
            bool pushing = input.Desired.sqrMagnitude > 0.0001f;

            if (IsWinded && input.Energy01 >= _tuning.windedRecoverFraction)
            {
                IsWinded = false;
                RecoveredWind = true;
            }

            if (!pushing)
            {
                _graceTimer += dt;
                if (_graceTimer > _tuning.stopGraceSeconds)
                {
                    if (Momentum > 0f || State == GaitState.Run) Break(GaitBreak.Stopped);
                    State = GaitState.Idle;
                }
            }
            else
            {
                _graceTimer = 0f;
                Vector2 dir = input.Desired.normalized;
                if (State == GaitState.Idle) State = GaitState.Walk;

                // Backpedalling never builds a run: a body stepping backwards is not accelerating.
                if (input.Backpedal && (Momentum > 0f || State == GaitState.Run))
                    Break(GaitBreak.Backpedal);

                if (_heading.sqrMagnitude > 0.0001f)
                    ApplyTurn(Vector2.Angle(_heading, dir), skill);
                _heading = dir;

                float expected = Mathf.Max(0.01f, input.WalkSpeed * SpeedMultiplier);
                bool keepingUp = input.RealSpeed >= expected * _tuning.minRealSpeedFraction;
                _blockedTimer = keepingUp ? 0f : _blockedTimer + dt;

                if (State == GaitState.Walk)
                {
                    if (IsWinded || input.Backpedal)
                        Momentum = 0f;
                    else if (keepingUp)
                        Momentum = Mathf.Min(1f, Momentum + dt / Mathf.Max(0.05f, _tuning.StartSeconds(skill)));
                    else
                        Momentum = Mathf.Max(0f, Momentum - 2f * dt / Mathf.Max(0.05f, _tuning.StartSeconds(skill)));

                    if (Momentum >= 1f && !IsWinded && input.Energy01 > 0f)
                    {
                        State = GaitState.Run;
                        StartedRun = true;
                        LastBreak = GaitBreak.None;
                    }
                }
                else if (State == GaitState.Run)
                {
                    // A run that survived a turn wins its momentum back as it straightens out, or
                    // every adjustment would leave it permanently closer to dropping to a walk.
                    if (keepingUp)
                        Momentum = Mathf.Min(1f, Momentum + dt / Mathf.Max(0.05f, _tuning.StartSeconds(skill)));

                    // A grace equal to the blend, so the first frames of a run — while the body
                    // is still accelerating into the new speed — are never read as a wall.
                    if (_blockedTimer > Mathf.Max(0.15f, _tuning.blendSeconds))
                        Break(GaitBreak.Blocked);
                    else if (Momentum < _tuning.runExitMomentum)
                        Break(GaitBreak.Turned);
                }
            }

            // Energy: a run spends, everything else regains after a short breath.
            if (State == GaitState.Run && pushing && input.Energy01 <= 0f)
            {
                Break(GaitBreak.Winded);
                IsWinded = true;
                BecameWinded = true;
            }

            if (State == GaitState.Run && pushing)
            {
                EnergyDelta = -_tuning.DrainPerSecond(skill) * dt;
                _sinceRunEnded = 0f;
                RunDistance = input.RealSpeed * dt;
            }
            else
            {
                _sinceRunEnded += dt;
                if (_sinceRunEnded >= _tuning.RegenDelay(skill))
                    EnergyDelta = (pushing ? _tuning.RegenWalking(skill) : _tuning.RegenIdle(skill)) * dt;
            }

            // Blend and speed.
            float blendStep = dt / Mathf.Max(0.01f, _tuning.blendSeconds);
            RunBlend = State == GaitState.Run
                ? Mathf.Min(1f, RunBlend + blendStep)
                : Mathf.Max(0f, RunBlend - blendStep);
            IsBackpedalling = pushing && input.Backpedal;
            SpeedMultiplier = Mathf.Lerp(_tuning.walkSpeedFraction, _tuning.RunMultiplier(skill),
                                         Mathf.SmoothStep(0f, 1f, RunBlend));
            if (IsBackpedalling) SpeedMultiplier *= _tuning.backpedalSpeedFraction;

            SecondsAtFullRun = State == GaitState.Run && RunBlend >= 1f ? SecondsAtFullRun + dt : 0f;
        }

        /// <summary>
        /// A turn within the skill's tolerance is free; past it the loss grows linearly until
        /// <see cref="LocomotionTuning.turnBreakDegrees"/>, where all momentum goes.
        /// </summary>
        private void ApplyTurn(float degrees, float skill)
        {
            float tolerance = _tuning.TurnTolerance(skill);
            if (degrees <= tolerance || Momentum <= 0f) return;
            float span = Mathf.Max(1f, _tuning.turnBreakDegrees - tolerance);
            float loss = Mathf.Clamp01((degrees - tolerance) / span);
            if (loss >= 1f)
            {
                Break(GaitBreak.Turned);
                return;
            }
            Momentum *= 1f - loss;
        }

        /// <summary>Back to a standing start: a respawn, a teleport, a scene load.</summary>
        public void Reset()
        {
            State = GaitState.Idle;
            Momentum = RunBlend = SecondsAtFullRun = 0f;
            SpeedMultiplier = _tuning.walkSpeedFraction;
            IsWinded = false;
            _heading = Vector2.zero;
            _graceTimer = _blockedTimer = 0f;
            _sinceRunEnded = float.PositiveInfinity;
            LastBreak = GaitBreak.None;
        }
    }
}
