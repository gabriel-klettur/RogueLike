using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>The expanding half of <see cref="ArcaneWeaveFX"/>. Its own component so the
    /// chip's per-frame ballistics and the burst's per-frame envelope do not share a state
    /// machine that has to branch on which one it is.</summary>
    public sealed class ArcaneWeaveBurst : MonoBehaviour
    {
        private SpriteRenderer _flash, _ring;
        private float _age, _duration, _radius;
        private Vector3 _ringStretch;

        public void Initialize(float radius, float seconds, Vector2 axis, Color hot, Color ring)
        {
            _radius = radius;
            _duration = seconds;
            // Elongate along the barrier, but never past 2.5:1 — beyond that a ring stops
            // reading as a ring and becomes a bar, and a bar has no centre to have come from.
            _ringStretch = new Vector3(Mathf.Clamp(radius, 1f, 2.5f), 1f, 1f);

            float angle = axis.sqrMagnitude > 1e-4f
                ? Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg
                : 0f;

            _flash = MakeLayer("Flash", ElementalSprites.Glow, hot, 70, angle);
            _ring = MakeLayer("Shockwave", ElementalSprites.Ring, ring, 71, angle);
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

            // The flash is over almost at once and the ring keeps going: a single shared
            // envelope makes the whole burst read as one soft blob expanding.
            float flash = Mathf.Pow(1f - Mathf.Clamp01(t / 0.35f), 2f);
            _flash.transform.localScale = Vector3.one * (_radius * (0.7f + 0.9f * t));
            SetAlpha(_flash, flash * 0.95f);

            float ringScale = _radius * (0.35f + 1.55f * Mathf.Sqrt(t));
            _ring.transform.localScale = new Vector3(
                ringScale * _ringStretch.x, ringScale, 1f);
            SetAlpha(_ring, (1f - t) * 0.85f);
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            var color = renderer.color;
            color.a = Mathf.Clamp01(alpha);
            renderer.color = color;
        }
    }
}
