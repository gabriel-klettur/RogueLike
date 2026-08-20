using UnityEngine;
using Valkur.Data.Feel;

namespace Valkur.Gameplay.Feel
{
    /// <summary>
    /// How a cue becomes motion.
    ///
    /// One path for every beat in the game, so two effects meant to hit equally hard cannot
    /// drift apart, and so the rate limiting, the trauma budget and the master intensity are
    /// impossible to forget at a call site.
    /// </summary>
    public sealed partial class CameraFeelDirector
    {
        /// <summary>Last real time each cue fired, indexed by the enum.</summary>
        private readonly float[] _lastFiredAt =
            new float[System.Enum.GetValues(typeof(CameraFeelCue)).Length];

        private bool _deathFlowActive;

        /// <summary>
        /// Fire one authored beat.
        ///
        /// <paramref name="direction"/> is where the blow pushes the frame; zero means
        /// omnidirectional and produces no kick at all — which is what makes a reward read as
        /// a swell rather than as damage.
        /// </summary>
        public void FireCue(CameraFeelCue cue, Vector2 direction, float intensity01)
        {
            if (_profile == null || Suppressed) return;
            if (_deathFlowActive && cue != CameraFeelCue.Death) return;

            FeelCue tuning = _profile.GetCue(cue);
            int index = (int)cue;

            float now = Time.realtimeSinceStartup;
            if (tuning.minIntervalSeconds > 0f &&
                now - _lastFiredAt[index] < tuning.minIntervalSeconds) return;
            _lastFiredAt[index] = now;

            float scale = Mathf.Max(0f, intensity01) * _profile.MasterIntensity01;

            AddTraumaWithinBudget(tuning.traumaAdd * scale, tuning.traumaDecayPerSecond,
                                  tuning.shakeFrequencyHz, now);
            ApplyKick(direction, tuning.kickAmplitudeWu * scale, tuning.kickOmega, tuning.kickZeta);

            if (tuning.leadFreezeSeconds > 0f)
                _state.LeadFreezeRemaining = Mathf.Max(_state.LeadFreezeRemaining,
                                                       tuning.leadFreezeSeconds);

            if (tuning.hitStopSeconds > 0f) FireFreeze(tuning.hitStopSeconds);
        }

        /// <summary>
        /// Trauma is additive and clamped, and spends from a rolling per-second budget.
        ///
        /// The old shake took the maximum of the new and current amplitude and never lowered
        /// it, so one heavy hit permanently raised every later shake in the session. The
        /// budget is the other half: beams and cones report a hit per tick per victim, and
        /// without a ceiling they would hold the screen at full trauma for as long as they
        /// are firing.
        /// </summary>
        private void AddTraumaWithinBudget(float add, float decay, float frequencyHz, float now)
        {
            if (add <= 0f) return;

            if (now >= _state.TraumaBudgetResetAt)
            {
                _state.TraumaBudgetResetAt = now + 1f;
                _state.TraumaSpentThisSecond = 0f;
            }

            float allowed = Mathf.Max(0f, _profile.MaxTraumaPerSecond - _state.TraumaSpentThisSecond);
            float granted = Mathf.Min(add, allowed);
            if (granted <= 0f) return;

            _state.TraumaSpentThisSecond += granted;
            _state.Trauma = CameraFeelMath.AddTrauma(_state.Trauma, granted);

            // The slowest active decay wins, so a long boss rumble is not cut short by a
            // sword hit landing during it.
            if (decay > 0f) _state.TraumaDecay = Mathf.Min(_state.TraumaDecay, decay);
            if (frequencyHz > 0f) _state.ShakeFrequencyHz = frequencyHz;
        }

        /// <summary>
        /// The impulse is applied as a velocity chosen so the spring peaks at exactly the
        /// authored displacement — otherwise the same number would travel a different
        /// distance for every damping ratio, with nothing on screen to explain why.
        /// </summary>
        private void ApplyKick(Vector2 direction, float amplitudeWu, float omega, float zeta)
        {
            if (amplitudeWu <= 0f || omega <= 0f) return;
            if (direction.sqrMagnitude <= 0.000001f) return;

            _state.KickOmega = omega;
            _state.KickZeta = zeta;
            _state.KickVelocity += direction.normalized * amplitudeWu *
                                   CameraFeelMath.ImpulseGainForUnitPeak(omega, zeta);
        }

        /// <summary>
        /// Dash: the frame commits to where the player is going, holds while they cross, then
        /// settles with a counter-kick as they land.
        /// </summary>
        public void FireDash(Vector2 direction, float distanceWu, float moveDuration)
        {
            if (_profile == null || Suppressed || direction.sqrMagnitude <= 0.000001f) return;

            Vector2 unit = direction.normalized;
            _state.LeadOverride = unit * Mathf.Min(distanceWu * 0.5f, 2.2f) * _state.LeadScale;
            _state.LeadOverrideRemaining = Mathf.Max(0.08f, moveDuration) + 0.06f;
            _pendingDashLandAt = Time.realtimeSinceStartup + _state.LeadOverrideRemaining;
            _pendingDashDirection = unit;

            FireCue(CameraFeelCue.DashLaunch, Vector2.zero, 1f);
        }

        /// <summary>
        /// Global time freeze. Routed to the existing hit-stop driver rather than a second
        /// one, because two systems owning <c>Time.timeScale</c> is how a pause survives a
        /// freeze that ends after it.
        /// </summary>
        public void FireFreeze(float realSeconds)
        {
            if (realSeconds <= 0f || Time.timeScale <= 0.0001f) return;
            Spells.RegularSlashHitStop.Trigger(realSeconds);
        }

        /// <summary>Kills every transient at once — teleport, editor entry, death.</summary>
        public void ResetTransients()
        {
            if (_profile == null) return;
            _state.ClearTransients(_profile.DefaultTraumaDecay);
            _appliedOffset = Vector2.zero;
            _appliedOffsetPreviousFrame = Vector2.zero;
        }

        private float _pendingDashLandAt = -1f;
        private Vector2 _pendingDashDirection;
        private float _pendingWhiffAt = -1f;
        private Vector2 _pendingWhiffDirection;

        /// <summary>
        /// The two beats that are defined by something NOT happening: a dash arriving, and a
        /// swing that never connected. Both are resolved on the clock rather than by an event,
        /// because no event exists for either.
        /// </summary>
        private void TickDeferredCues(float dt)
        {
            float now = Time.realtimeSinceStartup;

            if (_pendingDashLandAt > 0f && now >= _pendingDashLandAt)
            {
                _pendingDashLandAt = -1f;
                FireCue(CameraFeelCue.DashLand, -_pendingDashDirection, 1f);
            }

            if (_pendingWhiffAt > 0f && now >= _pendingWhiffAt)
            {
                _pendingWhiffAt = -1f;
                FireCue(CameraFeelCue.AttackWhiff, -_pendingWhiffDirection, 1f);
            }
        }
    }
}
