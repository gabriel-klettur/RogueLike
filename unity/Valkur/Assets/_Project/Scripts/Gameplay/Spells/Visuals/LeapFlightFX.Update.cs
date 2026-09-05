using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Per-frame half of the leap: the arc, the shrinking shadow that is the entire illusion of height, the launch dust, and the landing hand-off back to the executor.
    /// </summary>
    internal sealed partial class LeapFlightFX
    {

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _duration);

            UpdateFlight(t);
            UpdateLaunchDust();

            if (!_landed && _age >= _duration) Land();
            if (_age >= _duration + DUST_TAIL) Destroy(gameObject);
        }

        private void UpdateFlight(float t)
        {
            if (_landed) return;

            // Horizontal travel is LINEAR and the height is a half-sine: that is what a jump
            // does, and easing the ground travel as well makes the character look like it is
            // being dragged along a wire.
            Vector3 ground = Vector3.Lerp(_from, _to, t);
            float height = Mathf.Sin(t * Mathf.PI) * _arcHeight;

            if (_shadow != null)
            {
                _shadow.transform.position = ground + _feet;
                // Smaller and darker-edged the higher the body goes: the separation alone
                // reads as a jump, and the shrink is what fixes HOW high.
                float k = height / Mathf.Max(0.01f, _arcHeight);
                float width = _shadowRestWidth * Mathf.Lerp(1f, 0.52f, k);
                _shadow.transform.localScale = new Vector3(width, width * 0.42f, 1f);
                _shadow.color = new Color(0.04f, 0.04f, 0.06f, Mathf.Lerp(0.55f, 0.20f, k));
            }

            if (_ghost == null) return;

            _ghost.sprite = _body != null ? _body.sprite : _ghost.sprite;
            if (_body != null) { _ghost.flipX = _body.flipX; _ghost.flipY = _body.flipY; }
            _ghost.transform.position = ground + Vector3.up * height;
            _ghost.transform.localScale = Vector3.Scale(_ghostRestScale, SquashAt(t));
        }

        /// <summary>
        /// Compression at both ends of the jump — pushing off and absorbing the landing — and
        /// nothing in between. Two frames' worth at each end, which is all a 16 PPU sprite can
        /// carry before the deformation itself becomes the thing being looked at.
        /// </summary>
        private Vector3 SquashAt(float t)
        {
            float launch = 1f - Mathf.Clamp01(_age / SQUASH_SECONDS);
            float land = 1f - Mathf.Clamp01((_duration - _age) / SQUASH_SECONDS);
            float squash = Mathf.Max(launch, land);
            return new Vector3(Mathf.Lerp(1f, SQUASH_X, squash), Mathf.Lerp(1f, SQUASH_Y, squash), 1f);
        }

        private void UpdateLaunchDust()
        {
            float k = Mathf.Clamp01(_age / (_duration * 0.55f));

            if (_dustRing != null)
            {
                // Pinned nowhere in particular: a push-off ring is not a damage boundary, so
                // it is free to be a gesture rather than a promise about reach.
                float scale = Mathf.Lerp(0.35f, 2.1f, k);
                _dustRing.transform.localScale = new Vector3(scale, scale * 0.42f, 1f);
                _dustRing.color = WithAlpha(_palette.Leaf, (1f - k) * (1f - k) * 0.4f);
            }

            if (_motes == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _motes.Length; i++)
            {
                _motes[i].transform.position += _moteDrift[i] * dt;
                _moteDrift[i] *= Mathf.Pow(0.06f, dt);
                _motes[i].color = WithAlpha(_palette.Bark, (1f - k) * 0.4f);
            }
        }

        private void Land()
        {
            _landed = true;
            RestoreBody();

            if (_ghost != null) { Destroy(_ghost.gameObject); _ghost = null; }
            if (_shadow != null) { Destroy(_shadow.gameObject); _shadow = null; }

            // The live caster position rather than the planned one: a leap that was clamped
            // short of a wall must slam where the body actually is.
            Vector2 landing = _caster != null ? (Vector2)_caster.position : (Vector2)_to;
            var callback = _onLanded;
            _onLanded = null;
            callback?.Invoke(landing);
        }

        /// <summary>
        /// The character must never be left invisible — cycle finished, scene torn down, or
        /// killed in mid-air. Same discipline <c>TransporterFX</c> applies to the layer it
        /// drives.
        /// </summary>
        private void RestoreBody() => _bodyTint?.Clear(TintLayer.Teleport);

        private void OnDestroy()
        {
            RestoreBody();
            // A rig destroyed before its own landing still owes the executor its slam: the
            // damage must not be lost because a zone changed mid-jump.
            if (_landed) return;
            _landed = true;
            _onLanded?.Invoke(_caster != null ? (Vector2)_caster.position : (Vector2)_to);
        }

    }
}
