using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The per-frame half of the flourish: three beats driven off one clock, with every
    /// length and every shape coming from <see cref="CastFlourishProfile"/>.
    /// </summary>
    internal sealed partial class SpellCastFlourishFX
    {
        private void Update()
        {
            _age += Time.deltaTime;

            FollowOwner();

            float gather = Mathf.Clamp01(_age / Mathf.Max(0.01f, _profile.Gather));
            float sinceRelease = Mathf.Max(0f, _age - _profile.Gather);
            float tail = Mathf.Max(0.01f, _profile.Duration - _profile.Gather - _profile.Release);

            // Up in Release seconds, then down over everything that is left. Asymmetric on
            // purpose: a symmetric curve reads as a pulse, this reads as something firing.
            float punch = sinceRelease <= 0f
                ? 0f
                : sinceRelease < _profile.Release
                    ? Mathf.SmoothStep(0f, 1f, sinceRelease / _profile.Release)
                    : 1f - Mathf.SmoothStep(0f, 1f, (sinceRelease - _profile.Release) / tail);
            float afterglow = Mathf.Clamp01(sinceRelease /
                Mathf.Max(0.01f, _profile.Duration - _profile.Gather));

            UpdateSigil(gather, punch, afterglow);
            UpdateAura(gather, punch);
            UpdateHand(gather, punch);
            UpdateBurstAndLance(punch, afterglow, sinceRelease > 0f);
            UpdateMotes(gather, sinceRelease, afterglow);
            UpdateBody(gather, punch);
            UpdateLight(gather, punch);

            if (_age >= _profile.Duration) Destroy(gameObject);
        }

        /// <summary>
        /// Keeps the flourish on the caster, who is free to walk through it — most spells set
        /// <c>allowMovement</c>. Losing the owner mid-cycle (a zone change, a death) is not an
        /// error: the flourish stays where it was and finishes.
        /// </summary>
        private void FollowOwner()
        {
            if (_owner != null) transform.position = _owner.position;
        }

        private void UpdateSigil(float gather, float punch, float afterglow)
        {
            if (_sigilOuter == null) return;

            float radius = _profile.SigilRadius;
            float outer, inner;

            switch (_profile.Sigil)
            {
                case SigilMotion.Expand:
                    // Power being LAID DOWN on the world. A conjuring that drew its circle
                    // inward would be describing the opposite transaction.
                    outer = Mathf.Lerp(radius * 0.30f, radius * 1.10f, EaseOutCubic(gather)) + punch * 0.60f;
                    inner = Mathf.Lerp(radius * 0.14f, radius * 0.62f, EaseOutCubic(gather)) + punch * 0.32f;
                    break;

                case SigilMotion.Pulse:
                    // Breathes instead of resolving: a channel is a hold, not an event.
                    outer = radius * (0.92f + 0.10f * Mathf.Sin(_age * 9f));
                    inner = radius * 0.55f * (0.92f + 0.10f * Mathf.Sin(_age * 9f + 1.6f));
                    break;

                case SigilMotion.Contract:
                default:
                    outer = Mathf.Lerp(radius * 1.15f, radius * 0.72f, EaseOutCubic(gather)) + punch * 0.55f;
                    inner = Mathf.Lerp(radius * 0.70f, radius * 0.40f, EaseOutCubic(gather)) + punch * 0.30f;
                    break;
            }

            float fade = _profile.SigilAlpha * (0.25f + 0.75f * gather) * (1f - EaseInCubic(afterglow));

            PlaceSigil(_sigilOuter, outer, _age * _profile.SigilSpin, _palette.core, fade);
            PlaceSigil(_sigilInner, inner, _age * -_profile.SigilSpin * 1.6f, _palette.hotCore, fade * 0.78f);
        }

        /// <summary>Lay one ring flat on the floor at the caster's feet.</summary>
        private void PlaceSigil(SpriteRenderer sigil, float radius, float spinDegrees,
            Color color, float alpha)
        {
            if (sigil == null) return;
            float scale = RingScaleFor(Mathf.Max(0.05f, radius));
            sigil.transform.localPosition = Vector3.zero;
            sigil.transform.localRotation = Quaternion.Euler(0f, 0f, spinDegrees);
            // Squashed on Y: a circle drawn on the ground is an ellipse from this camera.
            sigil.transform.localScale = new Vector3(scale, scale * 0.42f, 1f);
            sigil.color = WithAlpha(color, alpha);
        }

        private void UpdateAura(float gather, float punch)
        {
            if (_aura == null) return;
            // Swells through the gather and is consumed by the release: the body stops being
            // the brightest thing on screen the moment the anchor becomes it.
            float alpha = _palette.halo.a * (0.35f * EaseOutCubic(gather) + _profile.AuraDrive * punch);
            _aura.transform.localPosition = _bodyOffset;
            _aura.color = WithAlpha(_palette.halo, alpha);
        }

        private void UpdateHand(float gather, float punch)
        {
            if (_hand != null)
            {
                float size = Mathf.Lerp(0.12f, 0.55f, EaseOutCubic(gather)) + punch * _profile.HandScale * 0.6f;
                _hand.transform.localPosition = _anchor;
                _hand.transform.localScale = Vector3.one * size;
                _hand.color = WithAlpha(_palette.core, 0.30f * gather + 0.95f * punch);
            }

            if (_handHot != null)
            {
                float size = Mathf.Lerp(0.05f, 0.20f, gather) + punch * _profile.HandScale * 0.30f;
                _handHot.transform.localPosition = _anchor;
                _handHot.transform.localScale = Vector3.one * size;
                // The gather term stays well under 1 so the release still has somewhere to
                // go: measured at 0.45 the core was already clipping before the flash.
                _handHot.color = WithAlpha(_palette.hotCore, 0.28f * gather * gather + punch);
            }
        }

        private void UpdateBurstAndLance(float punch, float afterglow, bool released)
        {
            UpdateBurst(afterglow, released);
            UpdateLance(punch);
        }

        private void UpdateBurst(float afterglow, bool released)
        {
            if (_burst == null) return;

            Vector3 origin;
            float squash;
            switch (_profile.Burst)
            {
                // Flat on the floor: the wave of a conjuring travels along the ground it is
                // being written on, not out through the air.
                case BurstOrigin.Ground: origin = Vector3.zero; squash = 0.40f; break;
                case BurstOrigin.Body: origin = _bodyOffset; squash = 0.85f; break;
                default: origin = _handOffset; squash = 0.72f; break;
            }

            float radius = Mathf.Lerp(0.15f, _profile.BurstRadius, EaseOutCubic(afterglow));
            float scale = RingScaleFor(radius);
            _burst.transform.localPosition = origin;
            _burst.transform.localScale = new Vector3(scale, scale * squash, 1f);
            // Held at zero until the release actually happens. Measured, the falloff term is
            // at its BRIGHTEST when afterglow is 0 — which is the whole gather — so without
            // this gate a shockwave sits on the hand for the entire wind-up and the cast reads
            // as having already gone off.
            _burst.color = WithAlpha(_palette.core,
                released ? Mathf.Pow(1f - afterglow, 2.0f) * 0.85f : 0f);
        }

        private void UpdateLance(float punch)
        {
            if (_lance == null) return;

            Vector2 aim;
            float angle;
            switch (_profile.Lance)
            {
                case LanceAim.Up: aim = Vector2.up; angle = 90f; break;
                case LanceAim.Down: aim = Vector2.down; angle = 90f; break;
                default:
                    aim = _direction;
                    angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
                    break;
            }

            // The only piece that carries a DIRECTION. Without it a cast to the left and a
            // cast to the right look identical — and an Invoke that pointed forward instead
            // of up would be describing a bolt rather than a summons.
            float reach = _profile.LanceLength;
            _lance.transform.localPosition = _anchor + (Vector3)(aim * Mathf.Lerp(reach * 0.25f, reach * 0.5f, punch));
            _lance.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            _lance.transform.localScale = new Vector3(reach * (0.7f + 0.6f * punch), reach * 0.24f, 1f);
            _lance.color = WithAlpha(_palette.core, punch * punch * 0.85f);
        }

        /// <summary>
        /// The character's own colour, through the stack rather than through the renderer.
        /// This is what makes them part of the effect instead of something standing behind
        /// it — and going through <see cref="SpriteTintStack"/> is what stops it fighting a
        /// burn, a hit flash or a weapon swap that happens to overlap.
        /// </summary>
        private void UpdateBody(float gather, float punch)
        {
            if (_bodyTint == null) return;
            float drive = 0.16f * EaseOutCubic(gather) + _profile.BodyDrive * punch;
            _bodyTint.Set(TintLayer.Cast, Color.Lerp(Color.white, _palette.core, Mathf.Clamp01(drive)));
        }

        private void UpdateLight(float gather, float punch)
        {
            if (_light == null) return;
            var property = ElementalProjectileVisual.GetLight2DIntensityProp();
            if (property == null) return;
            try
            {
                property.SetValue(_light, _palette.lightIntensity * _profile.LightMul *
                                          (0.30f * gather + 2.10f * punch));
            }
            catch { }
        }

        /// <summary>
        /// Interruption is the normal case, not the edge: a zone change, a death or a scene
        /// unload all destroy this mid-cycle, and a tint layer left set is a character who
        /// stays lit for the rest of the session.
        /// </summary>
        private void OnDestroy()
        {
            if (_bodyTint != null) _bodyTint.Clear(TintLayer.Cast);
        }

        private static float EaseOutCubic(float x)
        {
            float t = 1f - Mathf.Clamp01(x);
            return 1f - t * t * t;
        }

        private static float EaseInCubic(float x)
        {
            float t = Mathf.Clamp01(x);
            return t * t * t;
        }
    }
}
