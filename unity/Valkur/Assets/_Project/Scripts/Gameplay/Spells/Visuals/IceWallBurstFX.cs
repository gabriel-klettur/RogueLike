using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The flash and shockwave of ice breaking. One component drives both the little pop of
    /// a single crystal snapping and the full-length blast of the wall shattering — the only
    /// difference is the radius it is given.
    ///
    /// <para>The ring is stretched ALONG the wall's axis. A circular shockwave off a LINE of
    /// crystals reads as an explosion that happened at a point, which is the wrong story:
    /// the whole barrier let go at once.</para>
    /// </summary>
    public sealed class IceWallBurstFX : MonoBehaviour
    {
        private SpriteRenderer _flash;
        private SpriteRenderer _ring;
        private float _age;
        private float _duration;
        private float _radius;
        private Vector3 _ringScaleAxis;

        /// <summary>
        /// Spawn a burst at <paramref name="origin"/>. Unparented on purpose — the wall that
        /// produced it is usually destroyed in the same frame.
        /// </summary>
        public static IceWallBurstFX Spawn(Vector3 origin, float radius, float seconds, Vector2 axis)
        {
            if (!Application.isPlaying) return null;

            ElementalSprites.EnsureAll();

            var go = new GameObject("IceWallBurstFX");
            go.transform.position = origin;

            var fx = go.AddComponent<IceWallBurstFX>();
            fx._duration = Mathf.Max(0.05f, seconds);
            fx._radius = Mathf.Max(0.1f, radius);
            // Elongate along the barrier, but never so far that the ring stops reading as a
            // ring: past about 2.5:1 it is a bar, and a bar has no direction of travel.
            fx._ringScaleAxis = new Vector3(Mathf.Clamp(radius, 1f, 2.5f), 1f, 1f);
            fx.Build(axis);
            return fx;
        }

        private void Build(Vector2 axis)
        {
            float angle = axis.sqrMagnitude > 1e-4f
                ? Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg
                : 0f;

            _flash = MakeLayer("Flash", ElementalSprites.Glow, new Color(0.80f, 0.95f, 1f, 1f), 70, angle);
            _ring = MakeLayer("Shockwave", ElementalSprites.Ring, new Color(0.62f, 0.90f, 1f, 1f), 71, angle);
        }

        private SpriteRenderer MakeLayer(string name, Sprite sprite, Color color, int order, float angle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            renderer.sortingOrder = order;
            return renderer;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _duration);
            if (t >= 1f) { Destroy(gameObject); return; }

            // The flash is at its brightest immediately and is gone in the first third: it
            // is the moment of failure, not the aftermath.
            float flashFade = Mathf.Pow(1f - Mathf.Clamp01(t / 0.35f), 2f);
            _flash.transform.localScale = new Vector3(_radius * 1.6f, _radius * 1.6f, 1f) *
                                          Mathf.Lerp(0.5f, 1.15f, t);
            SetAlpha(_flash, flashFade * 0.95f);

            // The ring expands past the effect's radius and thins as it goes.
            float expand = Mathf.Lerp(0.25f, 1.55f, EaseOutCubic(t));
            _ring.transform.localScale = new Vector3(
                _ringScaleAxis.x * _radius * 2f * expand,
                _radius * 2f * expand,
                1f);
            SetAlpha(_ring, Mathf.Pow(1f - t, 1.8f) * 0.85f);
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            var color = renderer.color;
            color.a = Mathf.Clamp01(alpha);
            renderer.color = color;
        }

        private static float EaseOutCubic(float x)
        {
            float t = 1f - x;
            return 1f - t * t * t;
        }
    }
}
