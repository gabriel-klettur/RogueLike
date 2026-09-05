using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Per-frame animation: heading, spin, and the glint that is this rig's event layer.
    /// </summary>
    public sealed partial class SpellProjectileVisual
    {
        private void Update()
        {
            if (!_built || _impacted) return;

            TrackHeading();
            OrientRig();
            AnimateGlint();
            Breathe();
        }

        /// <summary>
        /// Heading comes from measured travel rather than from the rigidbody's velocity so a
        /// homing shot's drawn direction is the one it actually moved in this frame — the
        /// difference is visible on exactly the projectile that turns.
        /// </summary>
        private void TrackHeading()
        {
            Vector3 delta = transform.position - _lastPosition;
            if (delta.sqrMagnitude > 0.000001f)
                _travelDirection = ((Vector2)delta).normalized;
            _lastPosition = transform.position;
        }

        private void OrientRig()
        {
            float heading = Mathf.Atan2(_travelDirection.y, _travelDirection.x) * Mathf.Rad2Deg;

            // The travel anchor is ALWAYS aligned to heading and never spun, so the trail
            // leaves the back of the projectile whatever the body is doing.
            if (_travelAnchor != null)
                _travelAnchor.rotation = Quaternion.Euler(0f, 0f, heading);

            if (_rig == null) return;

            if (_profile.SpinDegPerSecond > 0f)
            {
                _spin += _profile.SpinDegPerSecond * Time.deltaTime;
                _rig.rotation = Quaternion.Euler(0f, 0f, _spin);
            }
            else
            {
                _rig.rotation = Quaternion.Euler(0f, 0f, heading);
            }
        }

        /// <summary>
        /// Law L4's event layer. A steady glow is read once and then filed as texture; a point
        /// that appears and is gone resets attention. On a blade this is the edge catching the
        /// camera as it turns, which is the only bright thing Martial Forms is allowed.
        /// </summary>
        private void AnimateGlint()
        {
            if (_glint == null) return;

            if (_profile.GlintInterval > 0f)
            {
                _glintClock -= Time.deltaTime;
                if (_glintClock <= 0f)
                {
                    _glintClock = _profile.GlintInterval * Random.Range(0.75f, 1.25f);
                    _glintFlash = 1f;
                }
            }

            // Decay is faster than a frame budget would suggest on purpose: a flash that
            // lingers becomes the steady glow it was meant to replace.
            _glintFlash = Mathf.Max(0f, _glintFlash - Time.deltaTime * 5.5f);

            float baseAlpha = _profile.GlintInterval > 0f ? 0.10f : 0.55f;
            float alpha = (baseAlpha + 0.90f * _glintFlash) * _power * _profile.Opacity;
            var c = _profile.Palette.hotCore;
            _glint.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));

            float scale = _profile.Width * (0.62f + 0.55f * _glintFlash);
            _glint.transform.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// A small continuous shimmer under the event layer. Kept deliberately shallow — it is
        /// there so the projectile is not perfectly static between glints, not to be noticed.
        /// </summary>
        private void Breathe()
        {
            float t = Time.time + _seed;
            float shimmer = 0.88f + 0.12f * Mathf.Sin(t * 19f);

            if (_shell != null)
            {
                var c = _profile.Palette.glow;
                _shell.color = new Color(c.r, c.g, c.b, c.a * shimmer * _power * _profile.Opacity);
            }

            if (_rim != null)
            {
                var c = _profile.Palette.hotCore;
                float pulse = 0.80f + 0.20f * Mathf.Sin(t * 25f + 0.7f);
                _rim.color = new Color(c.r, c.g, c.b, c.a * pulse * _power * _profile.Opacity);
            }

            if (_profile.Silhouette != ProjectileSilhouette.Wisp) return;

            // The wisp is the one silhouette that should look unfinished, so its shards drift
            // instead of holding formation — a mark on its way somewhere, not a bullet.
            for (int i = 0; i < _shards.Length; i++)
            {
                if (_shards[i] == null) continue;
                float phase = t * 2.3f + i * 1.7f;
                var basePos = new Vector3(-_profile.Length * 0.2f + i * 0.10f, 0f, 0f);
                _shards[i].transform.localPosition = basePos + new Vector3(
                    Mathf.Sin(phase) * 0.07f, Mathf.Cos(phase * 0.8f) * 0.09f, 0f);
            }
        }

        /// <summary>Re-reads the palette without rebuilding — used when a pooled shot changes spell.</summary>
        private void ApplyPalette()
        {
            var p = _profile.Palette;
            if (_shell != null) _shell.color = p.glow;
            if (_rim != null) _rim.color = p.hotCore;
            if (_glint != null) _glint.color = p.hotCore;
            for (int i = 0; i < _shards.Length; i++)
                if (_shards[i] != null) _shards[i].color = p.accent;

            if (_trail != null && _trail.colorGradient != null)
            {
                _trail.colorGradient = new Gradient
                {
                    colorKeys = new[]
                    {
                        new GradientColorKey(p.core, 0f),
                        new GradientColorKey(p.halo, 1f),
                    },
                    alphaKeys = new[]
                    {
                        new GradientAlphaKey(_profile.Opacity * 0.85f, 0f),
                        new GradientAlphaKey(0f, 1f),
                    },
                };
            }

            if (_light != null)
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, p.lightColor);
        }

        /// <summary>
        /// Pushes the pierce-drained power into every layer at once. Three readings move
        /// together — opacity, the trail's width, and the light — because one alone is a
        /// brightness change the player reads as distance rather than as weakening.
        /// </summary>
        private void ApplyPower()
        {
            if (_core != null)
            {
                Color body = _profile.Silhouette == ProjectileSilhouette.Blade
                    ? _profile.Palette.accent
                    : Color.Lerp(_profile.Palette.halo, Color.white, 0.35f);
                _core.color = new Color(body.r, body.g, body.b,
                    Mathf.Clamp01(_profile.Opacity * (0.55f + 0.45f * _power)));
            }

            if (_trail != null)
                _trail.widthMultiplier = _profile.TrailWidth * Mathf.Lerp(0.55f, 1f, _power);

            if (_light != null)
                ElementalProjectileVisual.GetLight2DIntensityProp()?
                    .SetValue(_light, 1.35f * Mathf.Lerp(0.5f, 1f, _power));
        }
    }
}
