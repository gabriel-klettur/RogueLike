using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Magic shield with epic bubble-dome visual: rotating outer ring + halo glow +
    /// shimmering hexagonal dome + dynamic Light2D rim light. Sets caster invincible
    /// for the duration, then fades out.
    /// </summary>
    public class ShieldController : MonoBehaviour
    {
        private float _remaining;
        private float _duration;
        private Transform _caster;
        private Health _casterHealth;

        private SpriteRenderer _ring, _halo, _dome, _core;
        private GameObject _lightGo;
        private Component _light;

        public void Initialize(float duration, Transform caster)
        {
            _remaining = duration;
            _duration = duration;
            _caster = caster;
            _casterHealth = caster != null ? caster.GetComponent<Health>() : null;

            if (_casterHealth != null)
                _casterHealth.SetInvincible(true);

            BuildVisual();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_shield_create");

            // Disable any default sprite
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        private void BuildVisual()
        {
            ElementalSprites.EnsureAll();
            transform.localScale = Vector3.one * 1.2f;
            _ring = Make("Ring", ElementalSprites.Ring, new Color(0.55f, 0.85f, 1f, 0.85f), 1.20f, 51);
            _halo = Make("Halo", ElementalSprites.Halo, new Color(0.30f, 0.65f, 1f, 0.45f), 1.55f, 50);
            _dome = Make("Dome", ElementalSprites.Glow, new Color(0.65f, 0.92f, 1f, 0.40f), 1.10f, 52);
            _core = Make("Core", ElementalSprites.Core, new Color(0.95f, 1f, 1f, 0.85f), 0.30f, 53);

            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                _lightGo = new GameObject("ShieldLight");
                _lightGo.transform.SetParent(transform, false);
                try
                {
                    _light = _lightGo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 2));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, new Color(0.55f, 0.85f, 1f, 1f));
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.4f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 1.8f);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.4f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
                }
                catch { }
            }
        }

        private SpriteRenderer Make(string name, Sprite sprite, Color color, float scale, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerID = SortingLayer.NameToID(Valkur.Core.SortingConfig.LAYER_VFX);
            sr.sortingLayerName = Valkur.Core.SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;

            if (_remaining <= 0f)
            {
                if (_casterHealth != null) _casterHealth.SetInvincible(false);
                if (_lightGo != null) Destroy(_lightGo);
                Destroy(gameObject);
                return;
            }

            // Follow caster
            if (_caster != null) transform.position = _caster.position;

            float t = Time.time;
            float fade = (_remaining < 1f) ? Mathf.Clamp01(_remaining) : 1f;
            float pulse = 0.85f + 0.15f * Mathf.Sin(t * 6f);
            float shimmer = 0.85f + 0.15f * Mathf.PerlinNoise(t * 4f, 0.27f);

            if (_ring != null) { _ring.transform.localRotation = Quaternion.Euler(0f, 0f, t * 60f); var c = _ring.color; c.a = 0.85f * fade * shimmer; _ring.color = c; }
            if (_halo != null) { var c = _halo.color; c.a = 0.45f * fade * pulse; _halo.color = c; }
            if (_dome != null) { _dome.transform.localScale = Vector3.one * 1.10f * pulse; var c = _dome.color; c.a = 0.40f * fade; _dome.color = c; }
            if (_core != null) { var c = _core.color; c.a = 0.85f * fade * shimmer; _core.color = c; }
            if (_light != null)
            {
                try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.4f * fade * pulse); }
                catch { }
            }
        }

        private void OnDestroy()
        {
            if (_casterHealth != null)
                _casterHealth.SetInvincible(false);
        }
    }
}
