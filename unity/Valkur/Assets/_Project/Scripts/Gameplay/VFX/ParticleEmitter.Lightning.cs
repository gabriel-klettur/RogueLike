using System.Collections;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticleEmitter
    {
        // ------------------------------------------------------------------ lightning

        private void SetupLightning(ParticleVfxParams p)
        {
            // Park any existing particle system — lightning draws with a LineRenderer.
            // Disabled rather than destroyed: Destroy() only takes effect at the end of
            // the frame, so an emitter switched lightning→particles in quick succession
            // could find the pending-destroy child and build a second one beside it.
            // Keeping the reference also means ApplyPreset can simply wake it back up.
            if (_ps != null)
            {
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _ps.gameObject.SetActive(false);
            }

            EnsureLineRenderer(p);
            if (_lightningCoroutine != null) StopCoroutine(_lightningCoroutine);
            _lightningCoroutine = StartCoroutine(AnimateLightning(p));
        }

        /// <summary>
        /// Stops the lightning animation and hides its LineRenderer. Called when a
        /// non-lightning preset is applied to an emitter that previously ran one —
        /// <see cref="AnimateLightning"/> never terminates on its own, so without this
        /// the bolt keeps drawing over whatever preset comes next.
        ///
        /// The child GameObject is kept (just disabled) so re-selecting a lightning
        /// preset reuses it instead of leaking one per switch.
        /// </summary>
        private void TeardownLightning()
        {
            if (_lightningCoroutine != null)
            {
                StopCoroutine(_lightningCoroutine);
                _lightningCoroutine = null;
            }
            if (_lr != null) _lr.enabled = false;
        }

        private void EnsureLineRenderer(ParticleVfxParams p)
        {
            _lr = GetComponentInChildren<LineRenderer>(true);
            if (_lr == null)
            {
                var child = new GameObject("LightningRenderer");
                child.transform.SetParent(transform, false);
                _lr = child.AddComponent<LineRenderer>();
            }
            _lr.enabled = true;

            _lr.positionCount = p.segments + 1;
            _lr.startWidth = p.thickness * _scaleMultiplier;
            _lr.endWidth = p.thickness * _scaleMultiplier * 0.5f;
            _lr.useWorldSpace = false;
            _lr.sortingLayerName = "VFX";

            // Material: built once and reused. Assigning a fresh Material on every
            // apply leaked one instance per preset switch, and the colours below ride
            // on vertex colour anyway, so one material serves every bolt.
            if (_lr.sharedMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
                _lr.sharedMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }

            // Color
            Color c = PickColor(p);
            _lr.startColor = c;
            _lr.endColor = new Color(c.r, c.g, c.b, 0f);
        }

        private IEnumerator AnimateLightning(ParticleVfxParams p)
        {
            float lifetime = Mathf.Max(0.05f, p.lifespan);
            float elapsed = 0f;
            float offset = p.lightningOffset * _scaleMultiplier;
            int segments = Mathf.Max(2, p.segments);

            while (true)
            {
                // Regenerate zigzag each frame while active
                elapsed += Time.deltaTime;
                if (elapsed < lifetime)
                {
                    RegenerateLightning(segments, offset);
                }
                else
                {
                    // Invisible between flashes
                    _lr.enabled = false;
                    elapsed = 0f;
                }

                _lr.enabled = elapsed < lifetime;
                yield return null;
            }
        }

        private void RegenerateLightning(int segments, float offset)
        {
            if (_lr == null) return;
            _lr.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float x = t * 2f - 1f; // Example: horizontal span -1 to 1
                float y = (i > 0 && i < segments)
                    ? Random.Range(-offset, offset)
                    : 0f;
                _lr.SetPosition(i, new Vector3(x * _scaleMultiplier, y, 0f));
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Color PickColor(ParticleVfxParams p)
        {
            if (p.colors != null && p.colors.Length > 0)
                return p.colors[Random.Range(0, p.colors.Length)];
            return p.color;
        }
    }
}
