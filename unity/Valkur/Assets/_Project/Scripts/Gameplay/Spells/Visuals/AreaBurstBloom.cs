using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A short additive wash over a body — used when a spell detonates ON its caster and the
    /// character has to be seen to be inside it.
    ///
    /// <para>WHY NOT <c>SpriteTintStack</c>. That is the single legal owner of a body sprite's
    /// colour and it must stay so, but two facts rule it out here. Every one of its sixteen
    /// layers already has an owner — <c>TintLayer.Cast</c> belongs to
    /// <c>SpellCastFlourishFX</c>, which is writing it during this very cast — and a second
    /// writer of one layer is exactly the defect the stack was built to end. And the stack
    /// MULTIPLIES: a white tint on this project's white-based sprites is a no-op, so the only
    /// thing it could actually do is DARKEN the caster, which is the opposite of a light going
    /// off on them.</para>
    ///
    /// <para>IT FOLLOWS RATHER THAN PARENTS, for the reason <c>WeaponSwapFlashFX</c> records:
    /// parenting inherits the entity's scale, and an entity scaled to anything but one would
    /// resize the wash with it.</para>
    /// </summary>
    internal sealed class AreaBurstBloom : MonoBehaviour
    {
        private Transform _follow;
        private SpriteRenderer _glow;
        private SpriteRenderer _hot;
        private Color _color;
        private float _size, _life, _gain, _age;
        private Vector3 _lastKnown;

        internal void Begin(Transform follow, SpriteRenderer glow, SpriteRenderer hot,
                            Color color, float size, float life, float gain)
        {
            _follow = follow;
            _glow = glow;
            _hot = hot;
            _color = color;
            _size = Mathf.Max(0.2f, size);
            _life = Mathf.Max(0.05f, life);
            _gain = Mathf.Max(1f, gain);
            _lastKnown = follow != null ? follow.position : transform.position;
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;

            // LateUpdate, and a remembered position: the caster may move or die mid-burst, and
            // a wash that snaps to the origin when its subject is destroyed is worse than one
            // that finishes where the body was.
            if (_follow != null) _lastKnown = _follow.position;
            transform.position = _lastKnown;

            float u = Mathf.Clamp01(_age / _life);
            // Fast in, slow out. A symmetric envelope reads as a lamp rather than a flash.
            float envelope = u < 0.12f ? u / 0.12f : Mathf.Pow(1f - (u - 0.12f) / 0.88f, 1.4f);

            if (_glow != null)
            {
                _glow.transform.localScale = Vector3.one * (_size * Mathf.Lerp(0.9f, 1.6f, u));
                _glow.color = Overdriven(0.55f * envelope);
            }

            if (_hot != null)
            {
                _hot.transform.localScale = Vector3.one * (_size * Mathf.Lerp(0.75f, 0.30f, u));
                _hot.color = Overdriven(0.70f * envelope);
            }

            if (_age >= _life) Destroy(gameObject);
        }

        /// <summary>
        /// Law L2 again: on an additive material the brightness dial is the COLOUR, which may
        /// exceed 1 because HDR is on. Reaching for the alpha to wash a body out widens the
        /// bloom into fog around the character instead of blowing the character out.
        /// </summary>
        private Color Overdriven(float alpha)
            => new Color(_color.r * _gain, _color.g * _gain, _color.b * _gain,
                         Mathf.Clamp01(alpha));
    }
}
