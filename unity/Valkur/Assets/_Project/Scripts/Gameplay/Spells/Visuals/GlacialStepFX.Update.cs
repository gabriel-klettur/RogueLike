using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Per-frame half of the glacial blink: the shards, the pose that breaks, the body that resolves, and the rime patch thawing from its own edge inward.
    /// </summary>
    internal sealed partial class GlacialStepFX
    {

        private void Update()
        {
            _age += Time.deltaTime;
            float beat = Mathf.Clamp01((_age - _delay) / SHARD_SECONDS);

            UpdateShards(beat);
            UpdateGhost(beat);
            UpdateBody(beat);
            UpdatePatch();
            UpdatePop(beat);
            UpdateLight(beat);

            if (_age >= _life) Destroy(gameObject);
        }

        private void UpdateShards(float beat)
        {
            if (_age < _delay) return;

            for (int i = 0; i < _shardTransforms.Length; i++)
            {
                Vector3 position;
                float visibility;

                if (_mode == Mode.Shatter)
                {
                    // Out, and falling: a broken plate does not float away, it drops.
                    float fall = beat * beat * _silhouette.y * 0.9f;
                    position = _shardSlot[i] + _shardScatter[i] * beat - Vector3.up * fall;
                    visibility = Mathf.Clamp01(beat / 0.12f) * (1f - Mathf.SmoothStep(0.45f, 1f, beat));
                }
                else
                {
                    // In, and eased so they ACCELERATE into place rather than drifting to it —
                    // which is what makes the arrival feel like an arrival.
                    float ease = beat * beat;
                    position = Vector3.Lerp(_shardSlot[i] + _shardScatter[i], _shardSlot[i], ease);
                    visibility = Mathf.Clamp01(beat / 0.1f) * (1f - Mathf.SmoothStep(0.72f, 1f, beat));
                }

                _shardTransforms[i].localPosition = position;
                _shardTransforms[i].localRotation =
                    Quaternion.Euler(0f, 0f, _shardSpin[i] * beat * (_mode == Mode.Shatter ? 1f : -0.4f));
                _shardRenderers[i].color = WithAlpha(_palette.hotCore, visibility * 0.85f);
            }
        }

        private void UpdateGhost(float beat)
        {
            if (_ghost == null) return;
            // The pose is only there long enough to be recognised before it breaks.
            float solidity = 1f - Mathf.SmoothStep(0f, 0.30f, beat);
            _ghost.color = new Color(1f, 1f, 1f, solidity);
        }

        private void UpdateBody(float beat)
        {
            if (_mode != Mode.Resolve || _bodyTint == null || _bodyReleased) return;

            float presence = _age < _delay ? 0f : Mathf.SmoothStep(0.25f, 0.85f, beat);
            _bodyTint.Set(TintLayer.Teleport, new Color(1f, 1f, 1f, presence));

            if (beat < 1f) return;
            _bodyReleased = true;
            _bodyTint.Clear(TintLayer.Teleport);
        }

        private void UpdatePatch()
        {
            if (_patch == null || _ring == null) return;

            float open = Mathf.Clamp01((_age - _delay) / 0.14f);
            float held = Mathf.Clamp01((_age - _delay) / Mathf.Max(0.01f, _patchHold));

            // Thaw = SHRINK. The alpha is held nearly flat until the very end on purpose: a
            // patch that dims uniformly reads as a fade, one that closes in from its own edge
            // reads as ice melting.
            float thaw = held <= (1f - THAW_FRACTION)
                ? 1f
                : 1f - Mathf.InverseLerp(1f - THAW_FRACTION, 1f, held);
            float extent = open * thaw;

            _patch.transform.localScale = new Vector3(_patchRadius * extent,
                                                      _patchRadius * 2f * extent, 1f);
            _patch.color = WithAlpha(_palette.glow, 0.42f * Mathf.Clamp01(extent * 3f));

            _ring.transform.localScale = Vector3.one * (_patchRadius / 0.39f * extent);
            _ring.color = WithAlpha(_palette.core, 0.55f * Mathf.Clamp01(extent * 3f));
        }

        private void UpdatePop(float beat)
        {
            if (_pop == null) return;

            // The EVENT layer, and only the arrival has one: a hard white flash across roughly
            // a third of the beat, which is what stops the convergence reading as a slow
            // gather that never resolves.
            float pop = _mode == Mode.Resolve && _age >= _delay
                ? Mathf.Clamp01((beat - 0.72f) / 0.14f) * (1f - Mathf.Clamp01((beat - 0.86f) / 0.14f))
                : 0f;
            _pop.color = WithAlpha(_palette.hotCore, pop * 0.9f);
        }

        private void UpdateLight(float beat)
        {
            if (_light == null) return;
            float k = _age < _delay ? 0f : Mathf.Sin(Mathf.Clamp01(beat) * Mathf.PI);
            try
            {
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 0.6f + k * 1.8f);
            }
            catch { /* URP 2D lighting absent in this project configuration. */ }
        }

        /// <summary>
        /// Whatever happened — beat finished, scene torn down, caster killed mid-blink — the
        /// character must not be left holding the alpha this effect was driving.
        /// </summary>
        private void OnDestroy() => _bodyTint?.Clear(TintLayer.Teleport);

    }
}
