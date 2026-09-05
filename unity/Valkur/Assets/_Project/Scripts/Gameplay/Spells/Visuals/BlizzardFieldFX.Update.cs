using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The blizzard's frame loop, and the GUST that is its event layer.
    ///
    /// <para>WHY A GUST AT ALL. An effect made only of CONTINUOUS motion stops being read after
    /// about a second — flakes falling at a steady rate for eight seconds are filed by the eye
    /// as one texture, exactly as the vortex's turning bands were before its discharges existed.
    /// What resets attention is an EVENT: something that appears, is over, and leaves the scene
    /// in its previous state. The whole column leaning for four tenths of a second is that.</para>
    ///
    /// <para>THE DUTY CYCLE IS THE DESIGN. 0.42 s held against a 0.9–2.2 s interval is roughly
    /// 26 %. The first version of the vortex's discharge layer measured 78 % of frames lit,
    /// which is not lightning but a lamp with a flicker, and it forfeits the one thing the layer
    /// exists for.</para>
    /// </summary>
    internal sealed partial class BlizzardFieldFX
    {
        /// <summary>How fast a gust's lean is reached and released. Snapping it on is a cut;
        /// easing it over the whole gust makes it a slow sway and loses the event.</summary>
        private const float LEAN_ATTACK = 9f;
        private const float LEAN_RELEASE = 3.2f;

        private float _leanDeg;

        public void Tick(float deltaTime, float fade)
        {
            if (_destroyed) return;

            float dt = Mathf.Max(0f, deltaTime);
            _age += dt;
            _fade = Mathf.Clamp01(fade);

            if (_tickFlash > 0f) _tickFlash = Mathf.Max(0f, _tickFlash - dt * 2.6f);

            AdvanceGust(dt);
            ApplyLean();
            AdvanceGround(dt);
            AdvanceGrit(dt);

            // The light is dim on purpose and is dimmest between gusts. A bright light held
            // steady for eight seconds flattens everything under it, and this field is a place
            // the player has to keep fighting in.
            float gust01 = _gustRemaining > 0f ? 1f : 0f;
            SetLightIntensity((0.62f + 0.22f * gust01 + 0.55f * _tickFlash) * _fade);
        }

        /// <summary>
        /// The field just hurt something. A gust is the ambient event; a damage tick is the
        /// only event that carries mechanical information, so the ring answers it directly.
        /// </summary>
        public void Lash(Vector3 worldTarget)
        {
            _tickFlash = 1f;
        }

        private void ScheduleGust()
        {
            _gustTimer = Random.Range(GUST_INTERVAL_MIN, GUST_INTERVAL_MAX);
            _gustRemaining = 0f;
        }

        private void AdvanceGust(float dt)
        {
            if (_gustRemaining > 0f)
            {
                _gustRemaining -= dt;
                if (_gustRemaining <= 0f) ScheduleGust();
            }
            else
            {
                _gustTimer -= dt;
                if (_gustTimer <= 0f)
                {
                    _gustRemaining = GUST_SECONDS;
                    // A sign per gust, so the storm is not always pushed the same way. Steady
                    // one-way drift reads as the camera moving rather than as wind.
                    float sign = Random.value < 0.5f ? -1f : 1f;
                    _gustLeanDeg = sign * Random.Range(GUST_LEAN_MIN_DEG, GUST_LEAN_MAX_DEG);
                }
            }

            float target = _gustRemaining > 0f ? _gustLeanDeg : 0f;
            float rate = _gustRemaining > 0f ? LEAN_ATTACK : LEAN_RELEASE;
            _leanDeg = Mathf.Lerp(_leanDeg, target, 1f - Mathf.Exp(-rate * dt));
        }

        /// <summary>
        /// Push each slice sideways by its own wind factor. The horizontal component is
        /// <c>tan(lean) x fallSpeed</c>, which is what makes the LEAN a lean rather than a
        /// nudge: every slice tilts to the same visual angle while moving at its own speed.
        /// </summary>
        private void ApplyLean()
        {
            float tan = Mathf.Tan(_leanDeg * Mathf.Deg2Rad);

            for (int i = 0; i < SLICES; i++)
            {
                var ps = _slices[i];
                if (ps == null) continue;

                float lean = tan * SLICE_SPEED[i] * SLICE_WIND[i];
                // Writing a module every frame is not free; skip when nothing moved enough to
                // be visible at 16 PPU.
                if (!float.IsNaN(_sliceAppliedLean[i]) && Mathf.Abs(lean - _sliceAppliedLean[i]) < 0.01f)
                    continue;

                var velocity = ps.velocityOverLifetime;
                velocity.x = new ParticleSystem.MinMaxCurve(lean);
                _sliceAppliedLean[i] = lean;
            }

            // The near slice thickens during a gust: it is the slice the player is standing in,
            // so it is the one where a gust is felt rather than merely observed.
            var near = _slices[SLICES - 1];
            if (near != null)
            {
                float gain = _gustRemaining > 0f ? GUST_NEAR_RATE_GAIN : 1f;
                var emission = near.emission;
                emission.rateOverTime = _sliceRate[SLICES - 1] * gain * _fade;
            }

            for (int i = 0; i < SLICES - 1; i++)
            {
                if (_slices[i] == null) continue;
                var emission = _slices[i].emission;
                emission.rateOverTime = _sliceRate[i] * _fade;
            }
        }

        private void AdvanceGround(float dt)
        {
            // Brightness only. A circle that breathes in SIZE is a promise that moves, and the
            // whole point of pinning the ring to the damage radius is that it does not.
            float breathe = 0.72f + 0.28f * Mathf.Sin(_age * 1.7f);
            SetAlpha(_ring, (0.34f + 0.16f * breathe + 0.55f * _tickFlash) * _fade);

            // Frost creeps out over the first couple of seconds and goes with the field. The
            // thaw is the fade the controller hands down, so a blizzard cut short by an
            // eviction thaws instead of vanishing mid-storm.
            float accumulation = Mathf.Clamp01(_age / FROST_RISE_SECONDS);
            SetAlpha(_frost, 0.16f * accumulation * _fade);

            for (int i = 0; i < SLICES; i++)
            {
                var ps = _slices[i];
                if (ps == null) continue;
                var main = ps.main;
                main.startColor = WithAlpha(_palette.core, SLICE_ALPHA[i] * _fade);
            }
        }

        /// <summary>
        /// Chips driven across the floor. They wrap rather than dying, because the layer is
        /// about the surface being scoured continuously — a chip that fades out mid-slide reads
        /// as a particle, and a particle is not matter.
        /// </summary>
        private void AdvanceGrit(float dt)
        {
            if (_grit == null) return;

            // Grit follows the gust when there is one and its own bearing otherwise, so the
            // floor and the air agree about which way the wind is blowing.
            float gustBearing = _leanDeg >= 0f ? 0f : 180f;
            float gustWeight = Mathf.Clamp01(Mathf.Abs(_leanDeg) / GUST_LEAN_MAX_DEG);
            float accumulation = Mathf.Clamp01(_age / FROST_RISE_SECONDS);

            for (int i = 0; i < _grit.Length; i++)
            {
                var t = _grit[i];
                if (t == null) continue;

                float bearing = Mathf.LerpAngle(_gritBearing[i], gustBearing, gustWeight);
                float rad = bearing * Mathf.Deg2Rad;
                float speed = _gritSpeed[i] * (1f + 1.6f * gustWeight);

                Vector3 p = t.localPosition;
                p.x += Mathf.Cos(rad) * speed * dt;
                p.y += Mathf.Sin(rad) * speed * dt * GROUND_SQUASH;

                // Un-squash before testing, or a chip that is genuinely inside the circle looks
                // out of bounds on the Y axis and every one of them wraps every second.
                float nx = p.x / _radius;
                float ny = p.y / (_radius * GROUND_SQUASH);
                if (nx * nx + ny * ny > 0.92f)
                {
                    p = RandomGroundPoint();
                    _gritBearing[i] = Random.Range(0f, 360f);
                }
                t.localPosition = p;

                // A slow shimmer per chip, phase-offset, so twelve of them never blink together.
                float twinkle = 0.65f + 0.35f * Mathf.Sin((_age + _gritPhase[i] * 4f) * 2.4f);
                SetAlpha(_gritRenderers[i], 0.62f * accumulation * twinkle * _fade);
            }
        }
    }
}
