using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Universal impact FX: shockwave ring + central flash + radial element burst
    /// + Light2D pulse + camera shake. Palette-driven.
    /// </summary>
    public class ElementalImpactFX : MonoBehaviour
    {
        private const float Duration = 0.55f;
        private const float ShockwaveStart = 0.30f;
        private const float ShockwaveEnd = 3.6f;
        private const float FlashScaleStart = 0.50f;
        private const float FlashScaleEnd = 2.8f;
        private const int   BurstCount = 22;
        private const float BurstSpeed = 5.5f;
        private const float ShakeAmplitude = 0.18f;
        private const float ShakeDuration = 0.22f;

        private SpriteRenderer _flashSr;
        private SpriteRenderer _ringSr;
        private GameObject _light2DGo;
        private Component _light2DComponent;
        private float _t;
        private ElementPalette _palette;

        public static ElementalImpactFX Spawn(Vector3 pos, SpellElement element)
            => Spawn(pos, ElementPalette.For(element));

        internal static ElementalImpactFX Spawn(Vector3 pos, ElementPalette palette)
        {
            var go = new GameObject($"ElementalImpactFX_{palette.element}");
            go.transform.position = pos;
            var fx = go.AddComponent<ElementalImpactFX>();
            fx._palette = palette;
            fx.Build();
            fx.SpawnBurst();
            Feel.CameraFeel.Cue(Data.Feel.CameraFeelCue.ImpactHeavy, Vector2.zero);
            return fx;
        }

        private void Build()
        {
            // Flash core
            var flash = new GameObject("Flash");
            flash.transform.SetParent(transform, false);
            flash.transform.localScale = Vector3.one * FlashScaleStart;
            _flashSr = flash.AddComponent<SpriteRenderer>();
            _flashSr.sprite = _palette.hotCoreSprite;
            _flashSr.color = _palette.hotCore;
            _flashSr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_ENTITIES);
            _flashSr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _flashSr.sortingOrder = SortingConfig.Z_SKY + 12;
            _flashSr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;

            // Shockwave ring
            var ring = new GameObject("Shockwave");
            ring.transform.SetParent(transform, false);
            ring.transform.localScale = Vector3.one * ShockwaveStart;
            _ringSr = ring.AddComponent<SpriteRenderer>();
            _ringSr.sprite = _palette.ringSprite;
            _ringSr.color = _palette.glow;
            _ringSr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_ENTITIES);
            _ringSr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _ringSr.sortingOrder = SortingConfig.Z_SKY + 11;
            _ringSr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;

            // Light2D pulse
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                _light2DGo = new GameObject("ImpactLight");
                _light2DGo.transform.SetParent(transform, false);
                _light2DGo.transform.localPosition = Vector3.zero;
                try
                {
                    _light2DComponent = _light2DGo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light2DComponent, System.Enum.ToObject(lt.PropertyType, 3));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light2DComponent, _palette.lightColor);
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light2DComponent, _palette.lightIntensity * 2.4f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light2DComponent, _palette.lightOuter * 1.8f);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light2DComponent, _palette.lightInner);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light2DComponent, 0.85f);
                }
                catch { _light2DComponent = null; }
            }
        }

        private void SpawnBurst()
        {
            for (int i = 0; i < BurstCount; i++)
            {
                float angle = (i / (float)BurstCount) * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 vel = dir * Random.Range(BurstSpeed * 0.6f, BurstSpeed * 1.2f);

                var go = new GameObject("Spark");
                go.transform.position = transform.position;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _palette.emberSprite;
                sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_ENTITIES);
                sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                sr.sortingOrder = SortingConfig.Z_SKY + 7;
                sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
                sr.color = Color.Lerp(_palette.core, _palette.glow, Random.value);

                var ember = go.AddComponent<ElementalEmber>();
                ember.Init(vel, Random.Range(0.35f, 0.75f), Random.Range(0.08f, 0.16f),
                           _palette.emberDrag, _palette.emberBuoyancy);
            }
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float u = Mathf.Clamp01(_t / Duration);

            if (_ringSr != null)
            {
                float scale = Mathf.Lerp(ShockwaveStart, ShockwaveEnd, EaseOutCubic(u));
                _ringSr.transform.localScale = Vector3.one * scale;
                var c = _palette.glow;
                _ringSr.color = new Color(c.r, c.g, c.b, c.a * (1f - u));
            }
            if (_flashSr != null)
            {
                float scale = Mathf.Lerp(FlashScaleStart, FlashScaleEnd, u);
                _flashSr.transform.localScale = Vector3.one * scale;
                var c = _palette.hotCore;
                _flashSr.color = new Color(c.r, c.g, c.b, c.a * (1f - u * u));
            }
            if (_light2DComponent != null)
            {
                try
                {
                    float pulse = Mathf.Lerp(_palette.lightIntensity * 2.4f, 0f, u);
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light2DComponent, pulse);
                }
                catch { }
            }

            if (_t >= Duration) Destroy(gameObject);
        }

        private static float EaseOutCubic(float x) { float i = 1f - x; return 1f - i * i * i; }
    }
}
