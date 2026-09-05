using UnityEngine;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The cloud's frame loop: the shared wind, the settling grit, and the BLOOM that is its
    /// event layer.
    ///
    /// <para>A cloud with nothing but continuous drift is a texture within a second — the same
    /// failure the vortex's turning bands had before its discharges existed, and the reason this
    /// spell measured 0 % event duty. A puff that swells somewhere in the volume and is gone
    /// resets attention; at 0.50 s against a 1.2–2.5 s interval that is roughly 27 %, which is
    /// the band an event layer has to sit in.</para>
    ///
    /// <para>A DAMAGE TICK BLOOMS ON THE VICTIM. The ambient bloom is decoration; the same
    /// gesture fired at whatever the cloud just poisoned turns the layer into a free damage
    /// indicator, which is the trade <c>static_field</c>'s arcs make and the reason a persistent
    /// field's only real EVENT should be the one it already has.</para>
    /// </summary>
    internal sealed partial class SporeCloudFX
    {
        public void Tick(float deltaTime, float fade)
        {
            if (_destroyed) return;

            float dt = Mathf.Max(0f, deltaTime);
            _age += dt;
            _fade = Mathf.Clamp01(fade);

            ApplyWind();
            AdvanceGround();
            AdvanceGrit();
            AdvanceBlooms(dt);

            // Dim and steady. The light's only job is to let the motes pick up the world's
            // colour at night so the cloud does not render at noon brightness in a dark room;
            // a bright one here would make a poison cloud read as a lantern.
            SetLightIntensity(0.55f * _fade);
        }

        /// <summary>
        /// The field just poisoned something. Bloom on it — see the class doc.
        /// </summary>
        public void Lash(Vector3 worldTarget)
        {
            if (_destroyed || _root == null) return;
            Vector3 local = _root.InverseTransformPoint(worldTarget);
            FireBloom(new Vector3(local.x, local.y, 0f), 0.72f);
        }

        /// <summary>
        /// Lean the volume downwind, CLAMPED. See the class doc: an unclamped wind would carry
        /// the picture off the circle the damage is actually queried on, which is the shape of
        /// defect this project files under "the drawn boundary is a lie".
        /// </summary>
        private void ApplyWind()
        {
            float windX = WeatherWind.VelocityX;

            for (int i = 0; i < SLICES; i++)
            {
                var ps = _slices[i];
                if (ps == null) continue;

                float maxDrift = _radius * MAX_DRIFT_FRAC / Mathf.Max(0.1f, SLICE_LIFE[i]);
                float drift = Mathf.Clamp(windX * SLICE_WIND[i] * 0.5f, -maxDrift, maxDrift);

                // Writing a module every frame is not free; skip anything below a pixel of
                // travel over a whole particle lifetime.
                if (!float.IsNaN(_sliceAppliedDrift[i]) && Mathf.Abs(drift - _sliceAppliedDrift[i]) < 0.01f)
                    continue;

                var velocity = ps.velocityOverLifetime;
                velocity.x = new ParticleSystem.MinMaxCurve(drift);
                _sliceAppliedDrift[i] = drift;
            }

            for (int i = 0; i < SLICES; i++)
            {
                if (_slices[i] == null) continue;
                var emission = _slices[i].emission;
                emission.rateOverTime = _sliceRate[i] * _fade;

                var main = _slices[i].main;
                main.startColor = WithAlpha(_palette.core, SLICE_ALPHA[i] * _fade);
            }
        }

        private void AdvanceGround()
        {
            // Brightness only. A ring that breathes in SIZE is a promise that moves, and pinning
            // it to the damage radius is the whole point of drawing it.
            float breathe = 0.70f + 0.30f * Mathf.Sin(_age * 1.35f);
            SetAlpha(_ring, (0.26f + 0.14f * breathe) * _fade);
            SetAlpha(_haze, (0.13f + 0.04f * breathe) * _fade);
        }

        private void AdvanceGrit()
        {
            if (_grit == null) return;

            for (int i = 0; i < _gritRenderers.Length; i++)
            {
                // Each chip settles at its own moment inside the window, so the floor fills in
                // gradually instead of fourteen chips appearing together.
                float settled = Mathf.Clamp01((_age - _gritPhase[i] * SETTLE_SECONDS) / 0.45f);
                SetAlpha(_gritRenderers[i], 0.55f * settled * _fade);
            }
        }

        private void AdvanceBlooms(float dt)
        {
            _bloomTimer -= dt;
            if (_bloomTimer <= 0f)
            {
                _bloomTimer = Random.Range(BLOOM_INTERVAL_MIN, BLOOM_INTERVAL_MAX);
                FireBloom(RandomVolumePoint(), 1f);
            }

            for (int i = 0; i < BLOOMS; i++)
            {
                if (_bloomAge[i] >= BLOOM_SECONDS) continue;
                _bloomAge[i] += dt;

                float t = Mathf.Clamp01(_bloomAge[i] / BLOOM_SECONDS);
                // In fast, out slow. An event needs an attack sharper than its decay or it
                // reads as a slow pulse, which is the steady state it exists to break.
                float envelope = Mathf.Clamp01(t / 0.18f) * Mathf.Clamp01((1f - t) / 0.55f);
                float span = _radius * Mathf.Lerp(0.34f, 0.98f, t) * _bloomScale[i];

                _blooms[i].localScale = new Vector3(span, span * 0.78f, 1f);
                SetAlpha(_bloomRenderers[i], 0.30f * envelope * _fade);
            }
        }

        private Vector3 RandomVolumePoint()
        {
            float r = _radius * Mathf.Sqrt(Random.value) * 0.7f;
            float a = Random.Range(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.62f + _radius * 0.18f, 0f);
        }

        private void FireBloom(Vector3 localPosition, float scale)
        {
            if (_blooms == null) return;
            int i = _bloomCursor;
            _bloomCursor = (_bloomCursor + 1) % BLOOMS;

            _blooms[i].localPosition = localPosition;
            _blooms[i].localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            _blooms[i].localScale = Vector3.one * (_radius * 0.34f * scale);
            _bloomScale[i] = scale;
            _bloomAge[i] = 0f;
        }
    }
}
