using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The envelope. Every beat the arcane flame has lives here: the ignition punch, the
    /// staggered settle, the three decorrelated flicker channels that keep the ground beds from
    /// breathing in lockstep, the per-tick boundary flare and flare-up, and the dissipation
    /// that dims the beds while the haze expands and thins.
    ///
    /// <para>The old effect had none of this — it was born at full size and full alpha on frame
    /// one and deleted in a single frame five seconds later, which at a 2 s cooldown the player
    /// saw every two seconds.</para>
    ///
    /// <para>The fire itself is driven from here too, through EMISSION RATE rather than alpha.
    /// A particle already in the air cannot be un-lit without deleting it, so fading a fire by
    /// its colour makes the flames go grey while still standing there; ramping the rate lets
    /// the patch fill and empty the way a fire actually catches and dies, and the tongues
    /// already alive finish on their own gradients.</para>
    /// </summary>
    public partial class ArcaneFlameController
    {
        /// <summary>Overall opacity of the whole rig: 0 before ignition, 1 at sustain, 0 at the end.</summary>
        private float _envelopeAlpha;
        /// <summary>Extra scale on the inner layers during ignition, and the collapse at the end.</summary>
        private float _coreEnvelope = 1f;
        /// <summary>The haze expands and thins as the mass leaves.</summary>
        private float _haloEnvelope = 1f;

        private void AnimateVisuals(float dt)
        {
            EvaluateEnvelope();

            float t = Time.time;
            // Three independent Perlin channels at different rates. One shared channel is
            // what made the old effect read as a single object pulsing, rather than as
            // fire — everything moved by the same number on the same frame. The tongues carry
            // their own flicker through the noise module; these are for the ground under them.
            float fast   = 0.80f + 0.20f * Mathf.PerlinNoise(t * 7.5f, _flickA);
            float mid    = 0.88f + 0.12f * Mathf.PerlinNoise(t * 3.9f, _flickB);
            float slow   = 0.94f + 0.06f * Mathf.PerlinNoise(t * 1.7f, _flickC);

            float pulse = 1f + 0.42f * _pulsePhase;
            // A connecting tick has to be READABLE on the volume, not only on the light. The
            // pulse used to move scale alone: measured, the summed additive alpha at the
            // centre was 2.274 before and after a tick, identical, so the only thing that
            // actually changed brightness was the Light2D — and in daylight, where the light
            // reads least, a hit produced almost no change at all. Kept small on purpose: the
            // additive stack blows out to flat white somewhere above 3.
            float pulseAlpha = 1f + PulseAlphaGain * _pulsePhase;
            float a = _envelopeAlpha;

            // The ground the fire is burning on. Two beds so the patch is brighter where the
            // flames are denser; alphas here are the build-time values multiplied through, and
            // the two must stay in step — two different numbers for one layer is exactly the
            // trap the old executor's overwritten localScale was.
            SetLayer(_groundHot,  GroundHotRadiusMul,  fast * _coreEnvelope * pulse, 0.46f * fast * pulseAlpha, a);
            SetLayer(_groundGlow, GroundGlowRadiusMul, mid  * _coreEnvelope,         0.38f * mid  * pulseAlpha, a);
            SetLayer(_haze,       HazeRadiusMul,       slow * _haloEnvelope,         0.20f,                     a);
            SetLayer(_scorch,     ScorchRadiusMul,     1f,                           0.42f,                     a);

            // Ground ring. Its alpha snaps on a connecting tick so the boundary is
            // re-asserted rather than the empty centre wobbling.
            float ringDiameter = _radius * 2f / RingCrestNormalized;
            if (_runeSpin != null)
            {
                _runeSpin.transform.localRotation = Quaternion.Euler(0f, 0f, t * RuneSpinSpeed);
                _runeSpin.transform.localScale = Vector3.one * ringDiameter;
                _runeSpin.color = WithAlpha(RingColor, (0.36f + 0.30f * _pulsePhase) * mid * a);
            }
            if (_runeStatic != null)
            {
                _runeStatic.transform.localScale = Vector3.one * ringDiameter;
                _runeStatic.color = WithAlpha(RingColor, 0.20f * a);
            }

            AnimateEmitters(mid);
            AnimateBoundaryRings(dt);
        }

        private const float RuneSpinSpeed = 26f;

        /// <summary>
        /// How much brighter a layer gets on a connecting tick. Alpha is COVERAGE on the
        /// additive material, so this is a real brightness dial and it stacks across the
        /// ground beds — see the note at the callsite for the measured ceiling.
        /// </summary>
        private const float PulseAlphaGain = 0.28f;

        /// <summary>Extra emission a connecting tick buys, as a fraction of the base rate.</summary>
        private const float PulseEmissionGain = 0.55f;

        private void SetLayer(SpriteRenderer sr, float radiusMul, float scaleMul, float alpha, float envelope)
        {
            if (sr == null) return;
            sr.transform.localScale = Vector3.one * (_radius * 2f * radiusMul * scaleMul);
            var c = sr.color;
            // Clamped: the pulse can push an authored alpha past 1, and a SpriteRenderer
            // colour above 1 is silently saturated rather than rejected, which would make
            // the pulse gain look bigger in the numbers than it is on screen.
            c.a = Mathf.Min(1f, alpha) * envelope;
            sr.color = c;
        }

        /// <summary>
        /// The fire's density follows the envelope and the beat. Emission, not colour — see the
        /// class note. Once the emitters have been stopped this does nothing: a stopped system
        /// ignores its rate, and rewriting it would be a way to accidentally restart a fire
        /// that is supposed to be going out.
        /// </summary>
        private void AnimateEmitters(float flicker)
        {
            if (_emittersStopped) return;

            float scale = _envelopeAlpha * (0.92f + 0.08f * flicker)
                          * (1f + PulseEmissionGain * _pulsePhase);

            for (int i = 0; i < _emitters.Count; i++)
            {
                var ps = _emitters[i];
                if (ps == null) continue;
                var emission = ps.emission;
                emission.rateOverTime = _emitterBaseRates[i] * scale;
            }
        }

        /// <summary>
        /// IGNITION (0 -> 0.14 s): the beds punch out to 1.18x on an ease-out while the whole
        /// rig fades up and the fire catches. SETTLE (-> 0.34 s): they relax to 1.0 and the
        /// outer layers arrive behind them. SUSTAIN: flat. DISSIPATION (last
        /// <c>_dissipateSeconds</c>): alpha falls on a 1.5 power while the beds collapse to
        /// 0.30 and the haze expands to 1.30 — mass reading as leaving, not as being switched
        /// off.
        /// </summary>
        private void EvaluateEnvelope()
        {
            if (_dissipating || _remaining <= _dissipateSeconds)
            {
                float k = Mathf.Clamp01(_remaining / Mathf.Max(0.01f, _dissipateSeconds));
                _envelopeAlpha = Mathf.Pow(k, 1.5f);
                _coreEnvelope = Mathf.Lerp(0.30f, 1f, k);
                _haloEnvelope = Mathf.Lerp(1.30f, 1f, k);
                return;
            }

            if (_age < IgnitionSeconds)
            {
                float k = _age / IgnitionSeconds;
                float ease = 1f - (1f - k) * (1f - k);       // ease-out quad
                _envelopeAlpha = ease;
                _coreEnvelope = Mathf.Lerp(0.25f, 1.18f, ease);
                _haloEnvelope = Mathf.Lerp(0.55f, 1f, ease);
                return;
            }

            if (_age < IgnitionSeconds + SettleSeconds)
            {
                float k = (_age - IgnitionSeconds) / SettleSeconds;
                _envelopeAlpha = 1f;
                _coreEnvelope = Mathf.Lerp(1.18f, 1f, k);
                _haloEnvelope = 1f;
                return;
            }

            _envelopeAlpha = 1f;
            _coreEnvelope = 1f;
            _haloEnvelope = 1f;
        }

        // ── Boundary flare ──────────────────────────────────────────────────────

        /// <summary>
        /// A ring that eases out to exactly <c>_radius</c>. Every connecting tick redraws
        /// the danger circle instead of wobbling the centre, so the player relearns where
        /// the fire hurts several times per cast.
        /// </summary>
        private void SpawnBoundaryRing()
        {
            if (_rings.Count >= MaxBoundaryRings) return;

            // Recycled, not rebuilt. One ring is spawned per CONNECTING tick — about eight
            // over a cast, and the old path minted a GameObject + SpriteRenderer for each and
            // destroyed it 0.34 s later. The live count is already capped at
            // MaxBoundaryRings, so the pool can never hold more than that either.
            SpriteRenderer sr = null;
            int last = _ringPool.Count - 1;
            while (last >= 0)
            {
                sr = _ringPool[last];
                _ringPool.RemoveAt(last);
                if (sr != null) break;          // a pooled ring can still be Unity-null
                sr = null;
                last = _ringPool.Count - 1;
            }

            if (sr == null)
            {
                // LAYER_VFX for the same reason the standing rings are there: this flare IS
                // the danger boundary being redrawn, and a boundary a tree can hide is not one.
                sr = MakeChild("BoundaryRing", ElementalSprites.Ring,
                    WithAlpha(RingColor, 0.85f), 0f,
                    SortingConfig.LAYER_VFX, 6, additive: false);
            }
            else
            {
                sr.enabled = true;
                sr.color = WithAlpha(RingColor, 0.85f);
                sr.transform.localScale = Vector3.zero;
            }

            _rings.Add(new BoundaryRing { Sr = sr, Age = 0f });
        }

        /// <summary>Park an expired ring where the next tick can pick it up again.</summary>
        private void RecycleRing(SpriteRenderer sr)
        {
            if (sr == null) return;
            sr.enabled = false;
            if (_ringPool.Count < MaxBoundaryRings) _ringPool.Add(sr);
            else Destroy(sr.gameObject);
        }

        private void AnimateBoundaryRings(float dt)
        {
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                var r = _rings[i];
                r.Age += dt;

                if (r.Sr == null || r.Age >= BoundaryRingLife)
                {
                    RecycleRing(r.Sr);
                    _rings.RemoveAt(i);
                    continue;
                }

                float k = r.Age / BoundaryRingLife;
                float ease = 1f - (1f - k) * (1f - k) * (1f - k);   // ease-out cubic
                // Crest travels from 0.30 x radius to exactly radius.
                float crest = Mathf.Lerp(_radius * 0.30f, _radius, ease);
                r.Sr.transform.localScale = Vector3.one * (crest * 2f / RingCrestNormalized);
                r.Sr.color = WithAlpha(RingColor, 0.85f * (1f - k) * _envelopeAlpha);

                _rings[i] = r;
            }
        }

        /// <summary>
        /// A connecting tick makes the patch FLARE: extra tongues where the fire caught
        /// something, and embers thrown off it. Scaled by how many it caught, so burning a
        /// crowd looks different from burning one straggler.
        /// </summary>
        private void EmitTickBurst(int hits)
        {
            // Emit(count) goes through the shape and applies the velocity module. Emit with an
            // explicit EmitParams and a zero velocity does NOT, which once made half the
            // catalog look motionless — so this deliberately uses the count overload.
            EmitExtra(_flameBody, Mathf.Clamp(5 + hits * 3, 5, 16));
            EmitExtra(_flameCore, Mathf.Clamp(4 + hits * 2, 4, 12));
            EmitExtra(_embers, Mathf.Clamp(5 + hits * 3, 5, 16));
        }

        private static void EmitExtra(ParticleSystem ps, int count)
        {
            if (ps == null || !ps.gameObject.activeInHierarchy) return;
            ps.Emit(count);
        }

        // ── Teardown helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Stop EMITTING, never clear. The tongues and embers already alive keep burning and
        /// fade on their own colorOverLifetime — that tail is the dissipation, and it is why
        /// <see cref="LongestParticleLifetime"/> sizes the wait before the object is destroyed.
        /// Clearing here is what deleted 22 particles mid-air on every one of the old effect's
        /// exits.
        /// </summary>
        private void StopEmitters()
        {
            for (int i = 0; i < _emitters.Count; i++)
            {
                if (_emitters[i] != null)
                    _emitters[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void HideSpriteLayers()
        {
            HideOne(_scorch); HideOne(_runeSpin); HideOne(_runeStatic);
            HideOne(_groundGlow); HideOne(_groundHot); HideOne(_haze);
            for (int i = _rings.Count - 1; i >= 0; i--) HideOne(_rings[i].Sr);
            _rings.Clear();
            for (int i = _ringPool.Count - 1; i >= 0; i--) HideOne(_ringPool[i]);
            HideLight();
        }

        private static void HideOne(SpriteRenderer sr)
        {
            if (sr != null) sr.enabled = false;
        }
    }
}
