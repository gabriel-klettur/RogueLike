using UnityEngine;
using Valkur.Core;
using Valkur.Data.Feel;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// <b>Fervor</b> — a shout. The martial silhouette, and the one defined as much by what it
    /// refuses to draw as by what it draws.
    ///
    /// <para>NO SIGIL, NO ORBITING RING, NO LIGHT. <c>SpellType.Buff</c> used to route
    /// unconditionally to the <c>Ward</c> cast family, whose profile is a ground sigil that
    /// EXPANDS at 70 deg/s under eighteen motes orbiting the body — so the game's battle shout
    /// opened by drawing a rotating magic circle. Martial Forms' entire identity is that
    /// nothing in it glows because it is enchanted; a circle and an orbit are the two gestures
    /// that erase exactly that distinction, and they are the reason
    /// <c>CastFlourishFamilies.Rally</c> had to exist rather than being a tweak to Ward.</para>
    ///
    /// <para>WHAT CARRIES IT INSTEAD is a shockwave across the FLOOR reaching 4 u in 0.2 s,
    /// opaque dust lifted along it, a camera kick, and the character's own sprite flashing
    /// warm. None of that is magic and all of it is legible at 16 PPU, which is the problem
    /// <c>SpellCastFlourishFX</c> was written for: on a character forty pixels tall the
    /// difference between an idle frame and a shout is a few pixels, so everything readable
    /// has to happen around the body rather than on it.</para>
    ///
    /// <para>THE DUST IS THE RIG'S ONE OPAQUE LAYER. Every other piece here is a thin warm
    /// rim or a heat mote; the chips are unlit matter, and they are what separates "the ground
    /// was hit" from "something was lit".</para>
    /// </summary>
    internal sealed partial class BuffAuraFX
    {
        /// <summary>Seconds for the wave to reach its full radius. The spec's number, and it is fast on purpose.</summary>
        private const float WAVE_TRAVEL = 0.20f;

        /// <summary>Seconds until the wave is gone entirely.</summary>
        private const float WAVE_LIFE = 0.34f;

        /// <summary>Seconds a dust chip stays up.</summary>
        private const float DUST_LIFE = 0.90f;

        /// <summary>Sustained alpha of the warm rim, and its peak during the shout itself.</summary>
        private const float RIM_HELD = 0.22f;
        private const float RIM_SHOUT = 0.78f;

        private SpriteRenderer[] _dust;
        private float[] _dustAngle;
        private float[] _dustSpeed;
        private float[] _dustLift;
        private float[] _dustSpin;

        private void ClearFervorState()
        {
            _dust = null;
            _dustAngle = null;
            _dustSpeed = null;
            _dustLift = null;
            _dustSpin = null;
        }

        private void BuildFervor()
        {
            // A thin warm rim on the silhouette: heat on skin, not a spell around a body. It
            // is additive because it is light coming OFF the character; its restraint is in
            // the alpha, which never reaches a quarter outside the shout itself.
            _rim = MakeSprite(_root, "HeatRim", ElementalSprites.Glow, _profile.Palette.core,
                              SortingConfig.LAYER_VFX, ORDER_RIM, additive: true);
            _rim.transform.localScale = new Vector3(_size.x * 1.35f, _size.y * 1.15f, 1f);

            int n = Mathf.Max(4, _profile.PieceCount);
            _dust = new SpriteRenderer[n];
            _dustAngle = new float[n];
            _dustSpeed = new float[n];
            _dustLift = new float[n];
            _dustSpin = new float[n];

            for (int i = 0; i < n; i++)
            {
                _dust[i] = MakeSprite(_root, "Dust" + i, TornadoSprites.Dust, _profile.Bark.Soil,
                                      SortingConfig.LAYER_VFX, ORDER_GROUND + 1, additive: false);
                _dust[i].transform.localScale = Vector3.one * (_size.x * 0.16f);
            }
        }

        /// <summary>
        /// The one-shot half of the shout, re-armed on every cast rather than only on a
        /// rebuild. A recast of the same buff keeps its rig — that is what stops a refresh
        /// looking like an interruption — but a shout that made no noise the second time would
        /// be a spell that visibly stopped working.
        /// </summary>
        private void ReplayFervorOnset()
        {
            if (_dust == null) return;

            for (int i = 0; i < _dust.Length; i++)
            {
                _dustAngle[i] = (i + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / _dust.Length;
                _dustSpeed[i] = Random.Range(4.5f, 9.0f);
                _dustLift[i] = Random.Range(1.1f, 2.2f);
                _dustSpin[i] = Random.Range(-260f, 260f);
            }

            // Direction is deliberately zero: a shout pushes the frame from nowhere in
            // particular, and CameraFeelDirector treats a zero direction as an undirected
            // beat rather than defaulting to one.
            CameraFeel.Cue(CameraFeelCue.ImpactMedium, Vector2.zero, 0.85f);
        }

        /// <summary>
        /// <paramref name="onset"/> is deliberately unread. The shockwave and the dust run off
        /// the raw <c>_age</c> because they are one-shot beats that must be able to outlive the
        /// 0.2 s ramp rather than being clamped to it — the wave is still fading at 0.34 s and
        /// the dust is up for nearly a second.
        /// </summary>
        private void TickFervor(float onset, float warn)
        {
            TickShockwave();
            TickDust();

            if (_rim != null)
            {
                // The shout, then the held heat. The rim COOLS to nothing over the warning,
                // which is the whole of this silhouette's expiry beat.
                float shout = Mathf.Clamp01(1f - _age / 0.30f);
                float breath = 0.86f + 0.14f * Mathf.Sin(_age * 3.1f);
                float alpha = Mathf.Lerp(RIM_HELD * breath, RIM_SHOUT, shout * shout);
                _rim.color = WithAlpha(_profile.Palette.core, alpha * Mathf.Lerp(1f, 0f, warn));
            }

            // A FLASH, not a ramp: the body punches warm on the shout and settles back to the
            // profile's held value. Fed to the shared tint so SpriteTintStack stays the one
            // owner of the body's colour.
            _tintBoost = 1f + 1.6f * Mathf.Clamp01(1f - _age / 0.35f);
        }

        private void TickShockwave()
        {
            if (_groundRing == null) return;

            // Reaches its full radius in WAVE_TRAVEL and is gone by WAVE_LIFE. It is a WAVE,
            // not a held circle: a ring still sitting on the floor ten seconds later is the
            // orbiting sigil this silhouette exists to refuse, drawn flat.
            float travel = Mathf.Clamp01(_age / WAVE_TRAVEL);
            float radius = Mathf.Lerp(0.35f, _profile.GroundRingRadius, EaseOutCubic(travel));
            SetRingRadius(_groundRing, radius);

            float k = 1f - Mathf.Clamp01(_age / WAVE_LIFE);
            _groundRing.color = WithAlpha(_profile.Palette.core, k * k * 0.90f);
        }

        private void TickDust()
        {
            if (_dust == null) return;

            float k = Mathf.Clamp01(_age / DUST_LIFE);
            float feet = -_size.y * 0.5f;

            for (int i = 0; i < _dust.Length; i++)
            {
                if (k >= 1f) { _dust[i].color = WithAlpha(_profile.Bark.Soil, 0f); continue; }

                // Drag on the radial run, a ballistic arc on the lift. The ground offset is
                // squashed by hand rather than by parenting to the ground plane, because a
                // chip that LIFTS has left the floor and must not be flattened with it.
                float t = _age;
                float r = _dustSpeed[i] * t * Mathf.Exp(-2.1f * t);
                float lift = _dustLift[i] * t - 5.4f * t * t;

                float x = Mathf.Cos(_dustAngle[i]) * r;
                float y = Mathf.Sin(_dustAngle[i]) * r * GROUND_SQUASH + Mathf.Max(0f, lift);

                var tr = _dust[i].transform;
                tr.localPosition = new Vector3(x, feet + y, 0f);
                tr.localRotation = Quaternion.Euler(0f, 0f, _dustSpin[i] * t);

                float a = Mathf.Sin(Mathf.Clamp01(k * 1.15f) * Mathf.PI);
                _dust[i].color = WithAlpha(_profile.Bark.Soil, 0.85f * a);
            }
        }
    }
}
