using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Per-frame half of the slam: the pinned ring snapping open, the fissures being drawn outward, and the earth thrown up and falling back.
    /// </summary>
    internal sealed partial class LeapImpactFX
    {

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;

            UpdateRing();
            UpdateCracks();
            UpdateDust(dt);
            UpdateClods(dt);
            UpdateLight();

            if (_age >= CRACK_SECONDS) Destroy(gameObject);
        }

        private void UpdateRing()
        {
            float k = Mathf.Clamp01(_age / 0.16f);

            if (_ring != null)
            {
                // Snaps open in a sixth of a second and holds the authored radius: the point
                // of the ring is to say how far the blow reached, so it must ARRIVE at the
                // real boundary rather than drift past it.
                float scale = Mathf.Lerp(0.2f, _radius / 0.39f, 1f - (1f - k) * (1f - k));
                _ring.transform.localScale = Vector3.one * scale;
                _ring.color = WithAlpha(_palette.Leaf,
                    0.85f * (1f - Mathf.Clamp01(_age / FLASH_SECONDS)));
            }

            if (_flash == null) return;
            float flash = 1f - Mathf.Clamp01(_age / 0.22f);
            _flash.color = WithAlpha(_palette.Sap, flash * flash * 0.7f);
        }

        private void UpdateCracks()
        {
            if (_cracks == null) return;

            float open = Mathf.Clamp01(_age / 0.10f);
            // Alpha holds while the fissure is drawn and only lets go at the very end, so the
            // mark reads as damage the ground took rather than as a light that faded.
            float linger = 1f - Mathf.Clamp01((_age - (CRACK_SECONDS - 0.7f)) / 0.7f);

            for (int i = 0; i < _cracks.Length; i++)
            {
                var t = _cracks[i].transform;
                var s = t.localScale;
                t.localScale = new Vector3(_crackLength[i] * open, s.y, 1f);
                _cracks[i].color = WithAlpha(_palette.Soil, linger * 0.9f);
            }
        }

        private void UpdateDust(float dt)
        {
            if (_dust == null) return;

            float fade = 1f - Mathf.Clamp01(_age / FLASH_SECONDS);
            for (int i = 0; i < _dust.Length; i++)
            {
                _dust[i].transform.localPosition += (Vector3)(_dustVelocity[i] * dt);
                _dustVelocity[i] *= Mathf.Pow(0.03f, dt);
                _dust[i].color = WithAlpha(_palette.Bark, fade * fade * 0.42f);
            }
        }

        private void UpdateClods(float dt)
        {
            if (_clods == null) return;

            float fade = 1f - Mathf.Clamp01(_age / (FLASH_SECONDS * 1.6f));
            for (int i = 0; i < _clods.Length; i++)
            {
                // Thrown out AND up. The ground travel and the HEIGHT are tracked as separate
                // quantities and only summed when the transform is written — reading the
                // position back and adding to it would fold last frame's height into this
                // frame's ground position, and the chip would climb away instead of arcing.
                _clodRise[i] -= 13f * dt;
                _clodHeight[i] = Mathf.Max(0f, _clodHeight[i] + _clodRise[i] * dt);

                // The 0.42 matches the ground plane's own squash, so a chip that has landed
                // travels across the floor on the same foreshortened plane the ring lies on.
                _clodGround[i] += new Vector2(_clodVelocity[i].x, _clodVelocity[i].y * 0.42f) * dt;
                _clodVelocity[i] *= Mathf.Pow(0.25f, dt);

                var t = _clods[i].transform;
                t.localPosition = new Vector3(_clodGround[i].x, _clodGround[i].y + _clodHeight[i], 0f);
                t.localRotation *= Quaternion.Euler(0f, 0f, _clodVelocity[i].x * 260f * dt);

                _clods[i].color = WithAlpha(_palette.Soil, fade);
            }
        }

        private void UpdateLight()
        {
            if (_light == null) return;
            float k = 1f - Mathf.Clamp01(_age / 0.30f);
            try
            {
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, k * k * 2.8f);
            }
            catch { /* URP 2D lighting absent in this project configuration. */ }
        }

    }
}
