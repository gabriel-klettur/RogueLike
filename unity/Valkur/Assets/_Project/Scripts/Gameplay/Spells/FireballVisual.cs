using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Attaches to a projectile to give it a minimalist fireball visual.
    /// Generates a procedural glow sprite with animated flickering.
    /// Creates a trailing particle effect using a child object.
    /// </summary>
    public class FireballVisual : MonoBehaviour
    {
        private SpriteRenderer _coreSr;
        private SpriteRenderer _glowSr;
        private float _baseScale;
        private Color _coreColor = new Color(1f, 0.85f, 0.3f, 1f);
        private Color _glowColor = new Color(1f, 0.4f, 0.05f, 0.45f);

        private static Sprite _coreSprite;
        private static Sprite _glowSprite;
        private static Material _unlitMaterial;

        private void Awake()
        {
            EnsureSprites();
            EnsureMaterial();
            BuildVisual();
        }

        private void Update()
        {
            // Flicker animation — subtle scale + color pulse
            float flicker = 1f + 0.12f * Mathf.Sin(Time.time * 18f) + 0.06f * Mathf.Sin(Time.time * 31f);
            if (_coreSr != null)
                _coreSr.transform.localScale = Vector3.one * _baseScale * flicker;
            if (_glowSr != null)
            {
                float glowFlicker = 1f + 0.15f * Mathf.Sin(Time.time * 14f + 1f);
                _glowSr.transform.localScale = Vector3.one * _baseScale * 2.2f * glowFlicker;
                float a = _glowColor.a * (0.7f + 0.3f * Mathf.Sin(Time.time * 20f));
                _glowSr.color = new Color(_glowColor.r, _glowColor.g, _glowColor.b, a);
            }
        }

        private void BuildVisual()
        {
            _baseScale = 0.35f;

            // Core (bright center)
            var coreGo = new GameObject("Core");
            coreGo.transform.SetParent(transform, false);
            coreGo.transform.localPosition = Vector3.zero;
            coreGo.transform.localScale = Vector3.one * _baseScale;
            _coreSr = coreGo.AddComponent<SpriteRenderer>();
            _coreSr.sprite = _coreSprite;
            _coreSr.color = _coreColor;
            _coreSr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _coreSr.sortingOrder = SortingConfig.Z_SKY + 5;
            _coreSr.material = _unlitMaterial;

            // Glow (outer soft circle)
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(transform, false);
            glowGo.transform.localPosition = Vector3.zero;
            glowGo.transform.localScale = Vector3.one * _baseScale * 2.2f;
            _glowSr = glowGo.AddComponent<SpriteRenderer>();
            _glowSr.sprite = _glowSprite;
            _glowSr.color = _glowColor;
            _glowSr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _glowSr.sortingOrder = SortingConfig.Z_SKY + 4;
            _glowSr.material = _unlitMaterial;

            // Remove any existing SpriteRenderer on root (from Projectile prefab)
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr != null)
                rootSr.enabled = false;
        }

        private static void EnsureSprites()
        {
            if (_coreSprite != null) return;

            // Core: solid circle with soft edges
            _coreSprite = CreateCircleSprite(32, CorePixelFunc);
            // Glow: radial gradient, very soft
            _glowSprite = CreateCircleSprite(64, GlowPixelFunc);
        }

        private static void EnsureMaterial()
        {
            if (_unlitMaterial != null) return;
            _unlitMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        private static Sprite CreateCircleSprite(int size, System.Func<float, Color> pixelFunc)
        {
            var tex = new Texture2D(size, size);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            float center = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / center;
                    pixels[y * size + x] = pixelFunc(dist);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Color CorePixelFunc(float dist)
        {
            if (dist > 1f) return Color.clear;
            // Hot white center fading to yellow
            float alpha = 1f - Mathf.Pow(dist, 1.5f);
            float whiteness = 1f - Mathf.Pow(dist, 0.8f);
            return new Color(
                Mathf.Lerp(1f, 1f, whiteness),
                Mathf.Lerp(0.6f, 1f, whiteness),
                Mathf.Lerp(0.1f, 0.9f, whiteness),
                alpha
            );
        }

        private static Color GlowPixelFunc(float dist)
        {
            if (dist > 1f) return Color.clear;
            // Soft radial falloff — orange glow
            float alpha = Mathf.Pow(1f - dist, 2.5f) * 0.7f;
            return new Color(1f, 0.35f, 0.05f, alpha);
        }
    }
}
