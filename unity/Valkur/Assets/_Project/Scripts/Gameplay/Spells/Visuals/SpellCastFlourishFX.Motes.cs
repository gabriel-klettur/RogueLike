using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The motes: where they come from, and where they go.
    ///
    /// <para>They are the piece that carries the family. The sigil and the lance say a lot,
    /// but the motes are sixteen to twenty-two objects MOVING, and movement is what the eye
    /// reads first — inward is a wind-up, outward is a release, upward is a summons, and
    /// backward is a wake left by something that has already gone.</para>
    ///
    /// <para>The SAME motes do both halves. A separate outward burst at the release would read
    /// as two unrelated effects that happened to overlap; reusing them is what ties the
    /// gathering to the firing.</para>
    /// </summary>
    internal sealed partial class SpellCastFlourishFX
    {
        private void BuildMotes()
        {
            int count = Mathf.Max(1, _profile.MoteCount);

            _moteTransforms = new Transform[count];
            _moteRenderers = new SpriteRenderer[count];
            _moteAngle = new float[count];
            _moteRadius = new float[count];
            _moteSpin = new float[count];
            _moteSize = new float[count];
            _moteFlight = new Vector2[count];

            Sprite sprite = _palette.accentSprite != null
                ? _palette.accentSprite
                : ElementalSprites.SparkleStar;

            for (int i = 0; i < count; i++)
            {
                Color tint = Color.Lerp(_palette.core, _palette.hotCore, Random.Range(0.15f, 0.95f));
                var sr = CreateSprite("Mote_" + i.ToString("00"), sprite, tint, ORDER_MOTE,
                    Core.SortingConfig.LAYER_VFX);

                // Evenly spaced with jitter: a perfectly regular ring reads as a UI element,
                // a fully random one reads as noise.
                _moteAngle[i] = (i + Random.Range(-0.3f, 0.3f)) / count * Mathf.PI * 2f;
                _moteRadius[i] = _profile.MoteRadius * Random.Range(0.75f, 1.15f);
                _moteSpin[i] = Random.Range(1.6f, 3.0f) * (Random.value < 0.5f ? -1f : 1f);
                _moteSize[i] = _profile.MoteSize * Random.Range(0.7f, 1.4f);
                _moteFlight[i] = ResolveFlight(_moteAngle[i]);

                _moteTransforms[i] = sr.transform;
                _moteRenderers[i] = sr;
            }
        }

        /// <summary>Velocity a mote leaves with, before drag. Zero for a family that holds.</summary>
        private Vector2 ResolveFlight(float angle)
        {
            Vector2 aim;
            switch (_profile.Departure)
            {
                case MoteDeparture.ThrowForward: aim = _direction; break;
                case MoteDeparture.ThrowUp: aim = Vector2.up; break;
                case MoteDeparture.TrailBehind: aim = -_direction; break;
                case MoteDeparture.PushOutward: aim = GroundRadial(angle); break;
                case MoteDeparture.PullInward: aim = -GroundRadial(angle); break;
                default: return Vector2.zero;
            }

            float spread = Random.Range(-_profile.MoteSpread, _profile.MoteSpread);
            aim = Rotate(aim, spread);
            return aim.normalized * Random.Range(_profile.MoteSpeedMin, _profile.MoteSpeedMax);
        }

        /// <summary>
        /// A direction on the ground PLANE rather than on the screen: squashed on Y, because
        /// from this camera a circle lying on the floor is an ellipse and a mote pushed
        /// straight up-screen has left the ground rather than moved along it.
        /// </summary>
        private static Vector2 GroundRadial(float angle)
            => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.75f).normalized;

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private void UpdateMotes(float gather, float sinceRelease, float afterglow)
        {
            bool released = sinceRelease > 0f;

            for (int i = 0; i < _moteTransforms.Length; i++)
            {
                var moteTransform = _moteTransforms[i];
                var moteRenderer = _moteRenderers[i];
                if (moteTransform == null || moteRenderer == null) continue;

                Vector3 position;
                float alpha;
                float scale;

                if (!released)
                {
                    position = ApproachPosition(i, gather);
                    alpha = Mathf.Clamp01(gather / 0.18f) * 0.95f;
                    scale = _moteSize[i] * Mathf.Lerp(0.6f, 1.25f, EaseInCubic(gather));
                }
                else
                {
                    position = DeparturePosition(i, sinceRelease);
                    ResolveDepartureFade(afterglow, out alpha, out float scaleMul);
                    scale = _moteSize[i] * scaleMul;
                }

                moteTransform.localPosition = position;
                moteTransform.localScale = Vector3.one * scale;
                moteTransform.localRotation = Quaternion.Euler(0f, 0f,
                    (_moteAngle[i] + _moteSpin[i] * _age) * Mathf.Rad2Deg);

                var color = moteRenderer.color;
                color.a = Mathf.Clamp01(alpha);
                moteRenderer.color = color;
            }
        }

        private Vector3 ApproachPosition(int i, float gather)
        {
            float radius = _moteRadius[i];
            float angle = _moteAngle[i];

            switch (_profile.Approach)
            {
                case MoteApproach.RiseFromGround:
                {
                    // Starts on the floor AROUND the caster — local zero is their feet — and
                    // is drawn up into the anchor. The floor ring is flatter than the airborne
                    // ones (0.42 against 0.75) because it really is lying on the ground.
                    var ground = new Vector3(Mathf.Cos(angle) * radius,
                                             Mathf.Sin(angle) * radius * 0.42f, 0f);
                    return Vector3.Lerp(ground, _anchor, EaseInCubic(gather));
                }

                case MoteApproach.DescendFromAbove:
                {
                    var sky = _anchor + new Vector3(Mathf.Cos(angle) * radius * 0.85f,
                                                    radius * 1.7f + Mathf.Sin(angle) * radius * 0.25f, 0f);
                    return Vector3.Lerp(sky, _anchor, EaseInCubic(gather));
                }

                case MoteApproach.OrbitBody:
                {
                    // Never converges: a ward is power HELD, so the ring keeps turning at the
                    // radius it started at instead of being swallowed.
                    float theta = angle + _moteSpin[i] * _age * 1.4f;
                    return _anchor + new Vector3(Mathf.Cos(theta) * radius,
                                                 Mathf.Sin(theta) * radius * 0.55f, 0f);
                }

                case MoteApproach.SweepArc:
                {
                    // Strung along the arc the blade travels, and travelling across it. The
                    // fraction wraps, so the band re-enters rather than stopping at the end.
                    float baseAngle = Mathf.Atan2(_direction.y, _direction.x);
                    float along = Mathf.Repeat(i / (float)_moteTransforms.Length + gather, 1f);
                    float theta = baseAngle + Mathf.Lerp(-1.2f, 1.2f, along);
                    return _anchor + new Vector3(Mathf.Cos(theta) * radius,
                                                 Mathf.Sin(theta) * radius * 0.75f, 0f);
                }

                case MoteApproach.CollapseToBody:
                {
                    // Squared falloff: this one SNAPS shut rather than easing shut, which is
                    // what separates an implosion from a gathering.
                    float shrink = 1f - EaseInCubic(gather);
                    float r = radius * shrink * shrink;
                    float theta = angle + _moteSpin[i] * gather * 0.6f;
                    return _anchor + new Vector3(Mathf.Cos(theta) * r,
                                                 Mathf.Sin(theta) * r * 0.75f, 0f);
                }

                case MoteApproach.SpiralIn:
                default:
                {
                    // EaseInCubic on the radius: slow at the edge, snapping in at the end. A
                    // linear approach reads as a UI animation.
                    float r = radius * (1f - EaseInCubic(gather));
                    float theta = angle + _moteSpin[i] * gather;
                    return _anchor + new Vector3(Mathf.Cos(theta) * r,
                                                 Mathf.Sin(theta) * r * 0.75f, 0f);
                }
            }
        }

        private Vector3 DeparturePosition(int i, float sinceRelease)
        {
            switch (_profile.Departure)
            {
                // A hold does not end — it keeps doing what it was doing and fades out on top.
                case MoteDeparture.Linger:
                    return ApproachPosition(i, 1f);

                // Already at the anchor and going nowhere: the shrink in the fade is what
                // finishes it, because an implosion has no outward phase at all.
                case MoteDeparture.PullInward:
                    return _anchor;

                default:
                {
                    // Drag rather than straight lines, so the throw decelerates into nothing.
                    // The coefficient bounds total travel at speed / MOTE_DRAG.
                    float travel = (1f - Mathf.Exp(-MOTE_DRAG * sinceRelease)) / MOTE_DRAG;
                    return _anchor + (Vector3)(_moteFlight[i] * travel);
                }
            }
        }

        private void ResolveDepartureFade(float afterglow, out float alpha, out float scaleMultiplier)
        {
            switch (_profile.Departure)
            {
                case MoteDeparture.Linger:
                    alpha = Mathf.Pow(1f - afterglow, 1.2f) * 0.9f;
                    scaleMultiplier = 1.15f;
                    break;

                case MoteDeparture.PullInward:
                    // Snuffed out rather than faded: the scale collapses faster than the alpha,
                    // so what the eye sees is each mote being swallowed, not dimmed.
                    alpha = Mathf.Pow(1f - afterglow, 0.7f);
                    scaleMultiplier = Mathf.Pow(1f - afterglow, 2.2f) * 1.3f;
                    break;

                default:
                    alpha = Mathf.Pow(1f - afterglow, 1.7f);
                    scaleMultiplier = Mathf.Lerp(1.25f, 0.35f, afterglow);
                    break;
            }
        }
    }
}
