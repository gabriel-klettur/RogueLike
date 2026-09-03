using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Per-frame animation of the breath: the travelling flicker, the envelope that ignites and
    /// extinguishes it, and the light.
    /// </summary>
    internal sealed partial class FlameConeFX
    {
        /// <summary>How bright the whole rig is drawing right now, 0..1. Read by tests.</summary>
        public float Envelope { get; private set; }

        /// <summary>
        /// Advance the fire. <paramref name="remaining"/> is how long the breath has left, in
        /// seconds — the extinction ramp is derived from it, so a cast cut short by eviction
        /// fades out on the same curve as one that ran its course.
        /// </summary>
        public void Tick(float deltaTime, float remaining)
        {
            _age += Mathf.Max(0f, deltaTime);

            float ignite = Mathf.Clamp01(_age / IGNITE_SECONDS);
            float extinguish = Mathf.Clamp01(remaining / EXTINGUISH_SECONDS);
            Envelope = Mathf.Min(ignite, extinguish);

            AnimateBody();
            AnimateCore();
            AnimateMuzzle();
            AnimateGround();
            AnimateEmission();
            AnimateLight();
        }

        /// <summary>
        /// Shut the emitters off and let what is already in the air burn out. Called when the
        /// breath ends: killing the particles instead would cut the jet mid-flight, which is
        /// the one moment the effect cannot afford a hard edge.
        /// </summary>
        public void StopEmitting()
        {
            SetRate(_fire, 0f);
            SetRate(_embers, 0f);
        }

        /// <summary>Tear down anything the GameObject hierarchy does not own.</summary>
        public void Dispose()
        {
            // Every renderer here was given a SHARED material, so there is nothing to destroy —
            // and nothing may read `renderer.material` on the way out either, because that
            // getter INSTANTIATES a clone. The old controller did exactly that in both
            // CleanupAndDestroy and OnDestroy, minting two materials per cast inside the code
            // whose comment claimed the per-cast material had been removed.
        }

        private void AnimateBody()
        {
            float axisExtent = (_length / SLICES) * SLICE_OVERLAP;
            float perSlice = BODY_ALPHA_BUDGET / SLICES;

            for (int i = 0; i < SLICES; i++)
            {
                float t = _bodyT[i];
                float d = t * _length;
                float halfW = HalfWidthAt(d);

                // ONE noise field read at an offset that moves with t, so a crest born at the
                // mouth travels outward. Sampling each slice independently gives a cone that
                // vibrates in place, which the eye reads as static rather than as fire.
                float wave = Mathf.PerlinNoise(_seed + _age * FLICKER_RATE - t * FLICKER_TRAVEL, t * 3.1f);
                float lickNoise = Mathf.PerlinNoise(_seed + 51.7f + _age * (FLICKER_RATE * 0.6f), t * 4.3f);

                float widthMul = 1f + FLICKER_DEPTH * (wave - 0.5f);
                float lick = (lickNoise - 0.5f) * 2f * LICK_DEPTH * halfW;

                _bodySlices[i].localPosition = new Vector3(d, lick, 0f);
                _bodySlices[i].localScale = new Vector3(axisExtent, halfW * 2f * widthMul, 1f);

                float taper = Mathf.Lerp(1f, 0.32f, t * t);
                float alpha = perSlice * taper * (0.72f + 0.56f * wave) * Envelope;
                SetAlpha(_bodyRenderers[i], alpha);
            }
        }

        private void AnimateCore()
        {
            float reach = _length * CORE_REACH;
            float axisExtent = (reach / CORE_SLICES) * SLICE_OVERLAP;
            float perSlice = CORE_ALPHA_BUDGET / CORE_SLICES;

            for (int i = 0; i < CORE_SLICES; i++)
            {
                float t = _coreT[i];
                float d = t * reach;
                float halfW = HalfWidthAt(d) * CORE_WIDTH;

                float wave = Mathf.PerlinNoise(_seed + 19.3f + _age * (FLICKER_RATE * 1.35f) - t * FLICKER_TRAVEL,
                                               t * 2.7f);

                _coreSlices[i].localPosition = new Vector3(d, 0f, 0f);
                _coreSlices[i].localScale = new Vector3(axisExtent,
                                                        halfW * 2f * (1f + FLICKER_DEPTH * 0.6f * (wave - 0.5f)),
                                                        1f);

                float taper = Mathf.Lerp(1f, 0.15f, t);
                SetAlpha(_coreRenderers[i], perSlice * taper * (0.7f + 0.6f * wave) * Envelope);
            }
        }

        private void AnimateMuzzle()
        {
            float pulse = Mathf.PerlinNoise(_seed + 77.1f, _age * (FLICKER_RATE * 1.8f));
            SetAlpha(_muzzleHot, (0.55f + 0.45f * pulse) * Envelope);
            SetAlpha(_muzzleHalo, (0.20f + 0.16f * pulse) * Envelope);

            float mouth = _length * MOUTH_WIDTH;
            _muzzleHot.transform.localScale = Vector3.one * (mouth * 2.4f * (0.88f + 0.24f * pulse));
        }

        private void AnimateGround()
        {
            // The scorch accumulates rather than tracking the envelope up: ground does not
            // un-blacken when a flicker dips. It only follows the envelope DOWN, at the end.
            float build = Mathf.Clamp01(_age / 0.55f);
            SetAlpha(_scorch, build * Envelope * 0.55f);
        }

        private void AnimateEmission()
        {
            SetRate(_fire, FIRE_DENSITY * _length * Envelope);
            SetRate(_embers, EMBER_DENSITY * _length * Envelope);
        }

        private void AnimateLight()
        {
            if (_light == null) return;
            // Perlin, not Random: a lamp that jumps to an unrelated value every frame reads as a
            // broken light, where a wandering one reads as fire.
            float flick = 0.80f + 0.35f * Mathf.PerlinNoise(_seed + 5.5f, _age * 14f);
            SetLightIntensity(LIGHT_INTENSITY * flick * Envelope);
        }

        private void SetLightIntensity(float value)
        {
            if (_light == null) return;
            try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, value); }
            catch { }
        }

        private static void SetAlpha(SpriteRenderer sr, float a)
        {
            if (sr == null) return;
            var c = sr.color;
            c.a = Mathf.Clamp01(a);
            sr.color = c;
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
        }
    }
}
