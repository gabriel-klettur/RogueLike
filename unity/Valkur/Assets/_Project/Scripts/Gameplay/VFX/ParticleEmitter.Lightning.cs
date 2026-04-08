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
            // Destroy any existing particle system — lightning uses LineRenderer
            if (_ps != null)
            {
                Destroy(_ps.gameObject);
                _ps = null;
            }

            EnsureLineRenderer(p);
            if (_lightningCoroutine != null) StopCoroutine(_lightningCoroutine);
            _lightningCoroutine = StartCoroutine(AnimateLightning(p));
        }

        private void EnsureLineRenderer(ParticleVfxParams p)
        {
            _lr = GetComponentInChildren<LineRenderer>();
            if (_lr == null)
            {
                var child = new GameObject("LightningRenderer");
                child.transform.SetParent(transform, false);
                _lr = child.AddComponent<LineRenderer>();
            }

            _lr.positionCount = p.segments + 1;
            _lr.startWidth = p.thickness * _scaleMultiplier;
            _lr.endWidth = p.thickness * _scaleMultiplier * 0.5f;
            _lr.useWorldSpace = false;
            _lr.sortingLayerName = "VFX";

            // Material
            var shader = Shader.Find("Sprites/Default");
            _lr.material = new Material(shader ?? Shader.Find("Hidden/Internal-Colored"));

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
