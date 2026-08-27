using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The envelope. Every beat the arcane flame has lives here: the ignition punch,
    /// the staggered settle, the three decorrelated flicker channels that keep the
    /// layers from breathing in lockstep, the per-tick boundary flare, and the
    /// dissipation that collapses the core while the halo expands and thins.
    ///
    /// The old effect had none of this — it was born at full size and full alpha on
    /// frame one and deleted in a single frame five seconds later, which at a 2 s
    /// cooldown the player saw every two seconds.
    /// </summary>
    public partial class ArcaneFlameController
    {
        /// <summary>Overall opacity of the whole rig: 0 before ignition, 1 at sustain, 0 at the end.</summary>
        private float _envelopeAlpha;
        /// <summary>Extra scale on the inner layers during ignition, and the collapse at the end.</summary>
        private float _coreEnvelope = 1f;
        /// <summary>Halo expands and thins as the mass leaves.</summary>
        private float _haloEnvelope = 1f;

        private void AnimateVisuals(float dt)
        {
            EvaluateEnvelope();

            float t = Time.time;
            // Three independent Perlin channels at different rates. One shared channel is
            // what made the old effect read as a single object pulsing, rather than as
            // fire — everything moved by the same number on the same frame.
            float fast   = 0.80f + 0.20f * Mathf.PerlinNoise(t * 7.5f, _flickA);
            float mid    = 0.88f + 0.12f * Mathf.PerlinNoise(t * 3.9f, _flickB);
            float slow   = 0.94f + 0.06f * Mathf.PerlinNoise(t * 1.7f, _flickC);

            float pulse = 1f + 0.42f * _pulsePhase;
            float a = _envelopeAlpha;

            // Interior alphas above the build-time values: on screen the disc read HOLLOW in
            // daylight, because additive over bright stone adds little and the eye then only
            // saw the rim. The volume has to carry the shape, not the outline.
            SetLayer(_hotCore, HotCoreRadiusMul, fast * _coreEnvelope * pulse, 0.80f * fast, a);
            SetLayer(_core,    CoreRadiusMul,    fast * _coreEnvelope * pulse, 0.62f * fast, a);
            SetLayer(_glow,    GlowRadiusMul,    mid  * _coreEnvelope * pulse, 0.42f * mid,  a);
            SetLayer(_halo,    HaloRadiusMul,    slow * _haloEnvelope,         0.24f * slow, a);
            SetLayer(_haze,    HazeRadiusMul,    slow * _haloEnvelope,         0.26f,        a);
            SetLayer(_scorch,  ScorchRadiusMul,  1f,                           0.34f,        a);

            // Ground ring. Its alpha snaps on a connecting tick so the boundary is
            // re-asserted rather than the empty centre wobbling.
            float ringDiameter = _radius * 2f / RingCrestNormalized;
            if (_runeSpin != null)
            {
                _runeSpin.transform.localRotation = Quaternion.Euler(0f, 0f, t * RuneSpinSpeed);
                _runeSpin.transform.localScale = Vector3.one * ringDiameter;
                _runeSpin.color = WithAlpha(RingColor, (0.44f + 0.34f * _pulsePhase) * mid * a);
            }
            if (_runeStatic != null)
            {
                _runeStatic.transform.localScale = Vector3.one * ringDiameter;
                _runeStatic.color = WithAlpha(RingColor, 0.20f * a);
            }
            if (_accent != null)
            {
                // Counter-rotates the rune. A single uniformly spinning ring reads as
                // dead; two rings turning against each other read as machinery.
                _accent.transform.localRotation =
                    Quaternion.Euler(0f, 0f, -t * _palette.accentSpinSpeed * 0.35f);
                _accent.transform.localScale =
                    Vector3.one * _radius * 2f * AccentRadiusMul * _coreEnvelope * pulse;
                _accent.color = WithAlpha(_palette.accent, 0.34f * fast * a);
            }

            AnimateBoundaryRings(dt);
        }

        private const float RuneSpinSpeed = 26f;

        private void SetLayer(SpriteRenderer sr, float radiusMul, float scaleMul, float alpha, float envelope)
        {
            if (sr == null) return;
            sr.transform.localScale = Vector3.one * (_radius * 2f * radiusMul * scaleMul);
            var c = sr.color;
            c.a = alpha * envelope;
            sr.color = c;
        }

        /// <summary>
        /// IGNITION (0 -> 0.14 s): the core punches out to 1.18x on an ease-out while the
        /// whole rig fades up. SETTLE (-> 0.34 s): the core relaxes to 1.0 and the outer
        /// layers arrive behind it. SUSTAIN: flat. DISSIPATION (last <c>_dissipateSeconds</c>):
        /// alpha falls on a 1.5 power while the core collapses to 0.30 and the halo
        /// expands to 1.30 — mass reading as leaving, not as being switched off.
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
        /// the fire hurts twelve times per cast.
        /// </summary>
        private void SpawnBoundaryRing()
        {
            if (_rings.Count >= MaxBoundaryRings) return;

            // LAYER_VFX for the same reason the standing rings are there: this flare IS the
            // danger boundary being redrawn, and a boundary a tree can hide is not one.
            var sr = MakeChild("BoundaryRing", ElementalSprites.Ring,
                WithAlpha(RingColor, 0.85f), 0f,
                SortingConfig.LAYER_VFX, 6, additive: false);
            _rings.Add(new BoundaryRing { Sr = sr, Age = 0f });
        }

        private void AnimateBoundaryRings(float dt)
        {
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                var r = _rings[i];
                r.Age += dt;

                if (r.Sr == null || r.Age >= BoundaryRingLife)
                {
                    if (r.Sr != null) Destroy(r.Sr.gameObject);
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

        /// <summary>A connecting tick throws motes outward, scaled by how many it caught.</summary>
        private void EmitTickBurst(int hits)
        {
            if (_motes == null || !_motes.gameObject.activeInHierarchy) return;
            // Emit(count) goes through the shape and applies startSpeed. Emit(EmitParams)
            // with an explicit zero velocity does NOT, which once made half the catalog
            // look motionless — so this deliberately uses the count overload.
            _motes.Emit(Mathf.Clamp(6 + hits * 3, 6, 18));
        }

        // ── Teardown helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Stop EMITTING, never clear. The motes already alive keep flying and fade on
        /// their own colorOverLifetime — that tail is the dissipation. Clearing here is
        /// what deleted 22 particles mid-air on every one of the old effect's exits.
        /// </summary>
        private void StopEmitters()
        {
            if (_motes != null) _motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_hazePs != null) _hazePs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void HideSpriteLayers()
        {
            HideOne(_scorch); HideOne(_runeSpin); HideOne(_runeStatic); HideOne(_haze);
            HideOne(_halo); HideOne(_glow); HideOne(_core); HideOne(_hotCore); HideOne(_accent);
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                if (_rings[i].Sr != null) Destroy(_rings[i].Sr.gameObject);
            }
            _rings.Clear();
            HideLight();
        }

        private static void HideOne(SpriteRenderer sr)
        {
            if (sr != null) sr.enabled = false;
        }
    }
}
