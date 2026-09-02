using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The per-frame life of the aura: ignition, the steady burn, the pulses it sends into the
    /// ground, the lightning, and the fade.
    /// </summary>
    internal sealed partial class KiAuraFX
    {
        private bool _fading;
        private float _fadeDuration;
        private float _fadeTime;

        /// <summary>True once the fade has run its course and the object can be destroyed.</summary>
        public bool FadeComplete => _fading && _fadeTime >= _fadeDuration;

        /// <summary>Fires when a ground pulse leaves, so the owner can shake the camera.</summary>
        public System.Action OnGroundPulse;

        public void Tick(float deltaTime)
        {
            _age += deltaTime;
            if (_fading) _fadeTime += deltaTime;

            float fade = _fading
                ? 1f - Mathf.Clamp01(_fadeTime / Mathf.Max(0.01f, _fadeDuration))
                : 1f;

            // The ignition OVERSHOOTS. A charge that ramps straight to its steady value reads
            // as a light being turned up; the flare-and-settle reads as something catching.
            float ignition = Mathf.Clamp01(_age / IgnitionSeconds);
            float envelope = fade * Mathf.Lerp(0.35f, 1f, ignition);
            float flare = Mathf.Exp(-Mathf.Pow(_age / (IgnitionSeconds * 0.45f), 2f));

            UpdateColumn(envelope, flare);
            UpdateTongues(envelope, flare);
            UpdateRings(envelope);
            UpdateBolts(envelope);
            UpdateLight(envelope, flare);
        }

        private void UpdateColumn(float envelope, float flare)
        {
            // A slow breath under the fast flicker. Without it the aura is busy but static in
            // its overall mass, which is what a looping particle effect looks like.
            float breath = 1f + 0.07f * Mathf.Sin(_age * 2.3f);

            if (_column != null)
            {
                float width = _config.BodySize.x * Mathf.Lerp(1.45f, 2.15f, Intensity) * breath;
                float height = _config.BodySize.y *
                               Mathf.Lerp(COLUMN_HEIGHT_CALM, COLUMN_HEIGHT_FIERCE, Intensity) *
                               breath * (1f + 0.28f * flare);
                KiSprites.ScaleTongue(_column.transform, width, height);
                SetAlpha(_column, (0.42f + 0.25f * Intensity + 0.55f * flare) * envelope);
            }

            if (_haze != null)
                SetAlpha(_haze, (0.16f + 0.20f * Intensity + 0.30f * flare) * envelope);

            if (_hot != null)
                SetAlpha(_hot, (0.10f + 0.22f * Intensity + 0.45f * flare) * envelope);
        }

        private void UpdateTongues(float envelope, float flare)
        {
            for (int i = 0; i < _tongues.Count; i++)
            {
                var tongue = _tongues[i];
                if (tongue.Root == null) continue;

                // Two sines at different rates: one flicker would be a pulse, and a pulse is
                // periodic enough for the eye to lock onto and stop believing.
                float flicker = 0.72f
                              + 0.28f * Mathf.Sin(_age * tongue.FlickerSpeed + tongue.Phase)
                              + 0.14f * Mathf.Sin(_age * tongue.FlickerSpeed * 2.37f + tongue.Phase * 1.7f);

                float height = tongue.BaseHeight * (0.80f + 0.32f * flicker) * (1f + 0.30f * flare);
                float width = tongue.BaseWidth * (0.88f + 0.18f * flicker);
                KiSprites.ScaleTongue(tongue.Root, width, height);

                // Swaying about its own base, not sliding: a flame is attached to what feeds it.
                float sway = Mathf.Sin(_age * tongue.FlickerSpeed * 0.55f + tongue.Phase) * tongue.SwayAmount;
                tongue.Root.localPosition = tongue.Anchor + new Vector3(sway, 0f, 0f);
                tongue.Root.localRotation = Quaternion.Euler(0f, 0f,
                    tongue.LeanDegrees + sway * 24f);

                SetAlpha(tongue.Renderer, (0.30f + 0.45f * flicker + 0.35f * flare) * envelope);
            }
        }

        /// <summary>
        /// Pressure leaving in pulses. A ring is launched on a period that shortens with
        /// intensity, and the pool is reused rather than allocated — three is enough because
        /// a ring's life is always shorter than three periods.
        /// </summary>
        private void UpdateRings(float envelope)
        {
            if (_rings.Count == 0) return;

            if (!_fading && _age >= _nextRing)
            {
                _ringStart[_nextRingSlot] = _age;
                _nextRingSlot = (_nextRingSlot + 1) % _rings.Count;
                _nextRing = _age + _ringPeriod;
                OnGroundPulse?.Invoke();
            }

            float life = Mathf.Min(0.9f, _ringPeriod * 2.4f);
            for (int i = 0; i < _rings.Count; i++)
            {
                float t = (_age - _ringStart[i]) / life;
                if (t < 0f || t > 1f) { SetAlpha(_rings[i], 0f); continue; }

                float radius = Mathf.Lerp(_config.GroundRadius * 0.25f,
                                          _config.GroundRadius * Mathf.Lerp(1.1f, 1.9f, Intensity),
                                          EaseOutCubic(t));
                float scale = radius / 0.39f;   // Ring's band peaks at normalized radius 0.78
                _rings[i].transform.localScale = new Vector3(scale, scale * 0.40f, 1f);
                SetAlpha(_rings[i], Mathf.Pow(1f - t, 1.8f) * (0.30f + 0.45f * Intensity) * envelope);
            }
        }

        /// <summary>
        /// Arcs crawling over the aura, above <see cref="KiPalette.LightningThreshold"/> only.
        /// Each one is on for two or three frames at a random place and angle — a bolt that
        /// animates is a bolt the eye can follow, and lightning that can be followed stops
        /// looking like lightning.
        /// </summary>
        private void UpdateBolts(float envelope)
        {
            if (_bolts.Count == 0) return;

            if (!_fading && _age >= _nextBolt)
            {
                int slot = _rng.Next(_bolts.Count);
                var sr = _bolts[slot];

                float reach = _config.BodySize.y * Range(0.45f, 1.05f);
                sr.transform.localPosition = _config.BodyOffset + new Vector3(
                    Range(-_config.BodySize.x, _config.BodySize.x),
                    Range(-_config.BodySize.y * 0.5f, _config.BodySize.y * 0.9f), 0f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, Range(0f, 360f));
                sr.transform.localScale = new Vector3(reach * Range(0.35f, 0.7f), reach, 1f);

                _boltUntil[slot] = _age + Range(0.045f, 0.10f);
                // Denser the harder it burns: roughly six a second at the top of the ladder.
                _nextBolt = _age + Mathf.Lerp(0.34f, 0.055f,
                    Mathf.InverseLerp(KiPalette.LightningThreshold, 1f, Intensity)) * Range(0.6f, 1.5f);
            }

            for (int i = 0; i < _bolts.Count; i++)
                SetAlpha(_bolts[i], _age < _boltUntil[i] ? 0.95f * envelope : 0f);
        }

        private void UpdateLight(float envelope, float flare)
        {
            if (_light == null) return;
            var property = ElementalProjectileVisual.GetLight2DIntensityProp();
            if (property == null) return;

            float pulse = 0.88f + 0.12f * Mathf.Sin(_age * 5.5f);
            float intensity = Mathf.Lerp(1.1f, 3.4f, Intensity) * pulse * envelope
                              + 2.2f * flare * envelope;
            try { property.SetValue(_light, intensity); }
            catch { }
        }

        /// <summary>
        /// Wind the aura down over <paramref name="seconds"/>. Emission stops at once while
        /// the sparks already in the air finish their lives, so the stream thins out instead
        /// of being cut.
        /// </summary>
        public void BeginFade(float seconds)
        {
            if (_fading) return;
            _fading = true;
            _fadeDuration = Mathf.Max(0.05f, seconds);
            _fadeTime = 0f;
            StopEmitting();
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            if (renderer == null) return;
            var color = renderer.color;
            color.a = Mathf.Clamp01(alpha);
            renderer.color = color;
        }

        private static float EaseOutCubic(float x)
        {
            float t = 1f - Mathf.Clamp01(x);
            return 1f - t * t * t;
        }
    }
}
