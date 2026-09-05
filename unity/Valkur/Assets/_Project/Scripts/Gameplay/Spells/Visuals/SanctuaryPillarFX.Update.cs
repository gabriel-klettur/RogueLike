using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The sanctuary's frame loop and its one EVENT — the heal tick.
    ///
    /// <para>The idle half is deliberately quiet: a slow breath on the ring, a drizzle of motes,
    /// a steady light. A ten-second field that is busy the whole time is a ten-second
    /// distraction, and it also leaves the tick nothing to stand out against.</para>
    ///
    /// <para>The pulse has an IN-FAST, OUT-SLOW envelope. An event needs an attack sharper than
    /// its decay, or it reads as a slow swell — which is a steady state with extra steps.</para>
    /// </summary>
    internal sealed partial class SanctuaryPillarFX
    {
        public void Tick(float deltaTime, float fade)
        {
            if (_destroyed) return;

            float dt = Mathf.Max(0f, deltaTime);
            _age += dt;
            _fade = Mathf.Clamp01(fade);

            if (_pulse > 0f) _pulse = Mathf.Max(0f, _pulse - dt / PULSE_SECONDS);
            if (_waveAge < WAVE_SECONDS) _waveAge += dt;

            AdvanceGround();
            AdvanceShaft();
            AdvanceWave();
            AdvanceRipples(dt);

            SetLightIntensity((1.05f + 0.85f * _pulse) * _fade);
        }

        /// <summary>
        /// A heal landed. Called from the SWEEP, never from a timer of its own — which is what
        /// lets the player count the ticks and read the totem's cadence off the picture.
        /// </summary>
        /// <param name="healedCount">How many bodies were mended. Zero still pulses, because a
        /// tick that finds everybody at full health is still a tick and hiding it would make the
        /// totem look broken exactly when it is working.</param>
        public void Pulse(int healedCount)
        {
            if (_destroyed) return;
            _pulse = 1f;
            _waveAge = 0f;

            if (_motes == null) return;
            // A WAVE of motes lifting together. The idle drizzle already runs at a low rate; a
            // burst is the only thing that says "now" rather than "still".
            int count = PULSE_MOTES + Mathf.Clamp(healedCount, 0, 4) * 4;
            _motes.Emit(new ParticleSystem.EmitParams { applyShapeToPosition = true }, count);
        }

        /// <summary>A single body was mended. A ripple over it, so the player can see WHO the
        /// circle reached rather than only that it fired.</summary>
        public void Ripple(Vector3 worldPosition)
        {
            if (_destroyed || _ripples == null || _root == null) return;

            int i = _rippleCursor;
            _rippleCursor = (_rippleCursor + 1) % RIPPLES;

            Vector3 local = _root.InverseTransformPoint(worldPosition);
            _ripples[i].localPosition = new Vector3(local.x, local.y + 0.55f, 0f);
            _rippleAge[i] = 0f;
        }

        private void AdvanceGround()
        {
            // Brightness only. A circle that breathes in SIZE is a promise that moves, and this
            // one is the promise: it is pinned to the radius the sweep actually queries.
            float breathe = 0.78f + 0.22f * Mathf.Sin(_age * 1.5f);
            float pulse = PulseEnvelope();
            SetAlpha(_ring, (0.30f + 0.14f * breathe + 0.62f * pulse) * _fade);
            SetAlpha(_dome, (0.09f + 0.03f * breathe + 0.10f * pulse) * _fade);
        }

        private void AdvanceShaft()
        {
            // The stone itself does not pulse — it is matter, and matter does not brighten.
            // What answers the tick is the band and the capital, which are the lit parts.
            SetAlpha(_shaft, 0.96f * _fade);

            float pulse = PulseEnvelope();
            float shimmer = 0.72f + 0.28f * Mathf.Sin(_age * 2.6f);
            SetAlpha(_band, (0.55f + 0.20f * shimmer + 0.45f * pulse) * _fade);
            SetAlpha(_capital, (0.30f + 0.12f * shimmer + 0.70f * pulse) * _fade);

            if (_capital != null)
            {
                float span = SHAFT_WIDTH * (2.4f + 0.9f * pulse);
                _capital.transform.localScale = Vector3.one * span;
            }
        }

        /// <summary>
        /// The tick's ground wave, travelling from the shaft out to the rim. It is the one thing
        /// here that DOES change size, and legitimately: it is not a boundary claim, it is a
        /// thing moving across the ground the ring has already marked out.
        /// </summary>
        private void AdvanceWave()
        {
            if (_wave == null) return;
            if (_waveAge >= WAVE_SECONDS) { SetAlpha(_wave, 0f); return; }

            float t = Mathf.Clamp01(_waveAge / WAVE_SECONDS);
            float eased = 1f - Mathf.Pow(1f - t, 2.2f);
            float span = RingSpanFor(Mathf.Lerp(_radius * 0.10f, _radius, eased));

            _wave.transform.localScale = new Vector3(span, span * GROUND_SQUASH, 1f);
            SetAlpha(_wave, 0.55f * (1f - t) * _fade);
        }

        private void AdvanceRipples(float dt)
        {
            for (int i = 0; i < RIPPLES; i++)
            {
                if (_rippleAge[i] >= RIPPLE_SECONDS) continue;
                _rippleAge[i] += dt;

                float t = Mathf.Clamp01(_rippleAge[i] / RIPPLE_SECONDS);
                float span = Mathf.Lerp(0.35f, 1.35f, 1f - Mathf.Pow(1f - t, 2.4f));
                _ripples[i].localScale = new Vector3(span, span * 0.62f, 1f);
                SetAlpha(_rippleRenderers[i], Mathf.Clamp01(t / 0.15f) * (1f - t) * 0.85f * _fade);
            }
        }

        /// <summary>In fast, out slow — see the class doc.</summary>
        private float PulseEnvelope()
        {
            if (_pulse <= 0f) return 0f;
            // _pulse runs 1 -> 0 linearly; squaring the tail is what makes the decay longer
            // than the attack the tick itself already provides.
            return _pulse * _pulse;
        }
    }
}
