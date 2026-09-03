using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// One frame of the root field. Driven by <c>PuddleController</c>, which owns the
    /// clock — a rig that read <c>Time.deltaTime</c> itself could not be measured from a
    /// harness, which is the trap the vortex force model documents.
    /// </summary>
    internal sealed partial class RootWhipFX
    {
        /// <summary>Seconds the ground takes to open before the first stem is through.</summary>
        private const float OPEN_SECONDS = 0.30f;

        /// <param name="deltaTime">Passed in, never read off <c>Time</c>.</param>
        /// <param name="fade">0..1 master alpha. The controller ramps this down over the
        /// field's last second so the patch sinks instead of being cut.</param>
        public void Tick(float deltaTime, float fade)
        {
            if (_root == null) return;

            _age += deltaTime;
            _fade = Mathf.Clamp01(fade);

            float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_age / OPEN_SECONDS));

            TickGround(open);
            TickCracks(open);
            TickStems(deltaTime);
            TickClods(deltaTime);
            TickBursts(deltaTime);

            // The light answers the lashes, not the clock: the field is dim while it merely
            // sits there and flares when it strikes, which is the only beat the spell has.
            float lashEnergy = 0f;
            for (int i = 0; i < TENDRILS; i++)
                lashEnergy += Mathf.Clamp01(_stemLash[i] / LASH_SECONDS);
            lashEnergy = Mathf.Clamp01(lashEnergy / LASH_STEMS);

            SetLightIntensity((0.55f + 0.85f * lashEnergy) * open * _fade);
        }

        private void TickGround(float open)
        {
            if (_groundRing != null)
            {
                // Brightness only. A ring that breathes in SIZE is a promise that moves, and
                // the promise is the one thing on screen that is exact.
                float pulse = 0.72f + 0.28f * Mathf.Sin(_age * 2.1f);
                _groundRing.color = WithAlpha(_palette.Leaf, 0.58f * pulse * open * _fade);
            }
            if (_groundGlow != null)
                _groundGlow.color = WithAlpha(_palette.Bark, 0.22f * open * _fade);
        }

        private void TickCracks(float open)
        {
            if (_cracks == null) return;
            for (int i = 0; i < _cracks.Length; i++)
            {
                if (_cracks[i] == null) continue;
                // Opened in sequence outward, so the ground tears rather than appearing torn.
                float t = Mathf.Clamp01((_age - i * 0.022f) / OPEN_SECONDS);
                _cracks[i].color = WithAlpha(_palette.Soil, 0.75f * Mathf.SmoothStep(0f, 1f, t) * _fade);
            }
        }

        private void TickStems(float deltaTime)
        {
            for (int i = 0; i < TENDRILS; i++)
            {
                _stemAge[i] += deltaTime;
                float age = _stemAge[i];

                // Still staggered in: nothing drawn yet.
                if (age < 0f)
                {
                    _stemRenderers[i].color = WithAlpha(_palette.Bark, 0f);
                    _stemPivots[i].localScale = Vector3.zero;
                    continue;
                }

                float total = SPROUT_SECONDS + _stemLife[i] + RETRACT_SECONDS;
                if (age >= total)
                {
                    Seed(i);
                    continue;
                }

                // ── height ───────────────────────────────────────────────────────────
                float grow;
                bool justBroke = false;
                if (age < SPROUT_SECONDS)
                {
                    float u = age / SPROUT_SECONDS;
                    // Overshoot then settle: a linear stretch reads as a rectangle being
                    // scaled, which is exactly what the old rig looked like.
                    grow = Mathf.Sin(u * Mathf.PI * 0.5f) * (1f + SPROUT_OVERSHOOT * (1f - u));
                    justBroke = age - deltaTime < 0f;
                }
                else if (age < SPROUT_SECONDS + _stemLife[i])
                {
                    grow = 1f;
                }
                else
                {
                    float u = (age - SPROUT_SECONDS - _stemLife[i]) / RETRACT_SECONDS;
                    // Sinks faster than it rose, and keeps a little width to the last
                    // frame so it looks pulled under rather than faded out.
                    grow = 1f - Mathf.SmoothStep(0f, 1f, u);
                }

                if (justBroke)
                    EruptAt(_stemPivots[i].localPosition, Random.Range(2, 4));

                // ── lash ─────────────────────────────────────────────────────────────
                float lash01 = 0f;
                if (_stemLash[i] > 0f)
                {
                    _stemLash[i] = Mathf.Max(0f, _stemLash[i] - deltaTime);
                    float u = 1f - _stemLash[i] / LASH_SECONDS;
                    // Fast out, slow back: a crack is not symmetrical in time.
                    lash01 = u < 0.35f
                        ? Mathf.Sin(u / 0.35f * Mathf.PI * 0.5f)
                        : Mathf.Cos((u - 0.35f) / 0.65f * Mathf.PI * 0.5f);
                }

                // ── sway ─────────────────────────────────────────────────────────────
                float sway = Mathf.Sin(_age * _stemSwayHz[i] * Mathf.PI * 2f + _stemSwayPhase[i])
                             * SWAY_AMPLITUDE_DEG;
                float lean = Mathf.Lerp(_stemLean[i] + sway, _stemLashLean[i], lash01);

                float height = _stemHeight[i] * grow * (1f + LASH_STRETCH * lash01);
                // Width does NOT take the lash stretch: a stem that got wider as it struck
                // would read as inflating rather than as reaching.
                float width = RootSprites.TendrilWorldWidth * _stemMirror[i]
                              * Mathf.Lerp(1f, 0.82f, lash01);

                _stemPivots[i].localScale = new Vector3(width, Mathf.Max(0f, height), 1f);
                _stemPivots[i].localRotation = Quaternion.Euler(0f, 0f, lean);

                // A lashing stem shows its living colour; a resting one is bark.
                Color c = Color.Lerp(_palette.Bark, _palette.Leaf, lash01 * 0.85f);
                _stemRenderers[i].color = WithAlpha(c, Mathf.Clamp01(grow * 1.4f) * _fade);
            }
        }

        private void TickClods(float deltaTime)
        {
            const float GRAVITY = -7.5f;
            for (int i = 0; i < CLODS; i++)
            {
                if (_clodAge[i] >= _clodLife[i])
                {
                    if (_clodRenderers[i].color.a != 0f)
                        _clodRenderers[i].color = WithAlpha(_palette.Soil, 0f);
                    continue;
                }

                _clodAge[i] += deltaTime;
                _clodVelocity[i].y += GRAVITY * deltaTime;
                _clods[i].localPosition += (Vector3)(_clodVelocity[i] * deltaTime);
                _clods[i].localRotation *= Quaternion.Euler(0f, 0f, _clodSpin[i] * deltaTime);

                float u = Mathf.Clamp01(_clodAge[i] / _clodLife[i]);
                _clodRenderers[i].color = WithAlpha(_palette.Soil, (1f - u * u) * _fade);
            }
        }

        private void TickBursts(float deltaTime)
        {
            const float BURST_SECONDS = 0.22f;
            for (int i = 0; i < TENDRILS; i++)
            {
                if (_burstAge[i] > BURST_SECONDS) continue;
                _burstAge[i] += deltaTime;
                float u = Mathf.Clamp01(_burstAge[i] / BURST_SECONDS);
                float span = Mathf.Lerp(0.10f, 0.65f, u);
                _bursts[i].transform.localScale = new Vector3(span, span * 0.75f, 1f);
                _bursts[i].color = WithAlpha(_palette.Sap, (1f - u) * 0.85f * _fade);
            }
        }
    }
}
