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
        /// <summary>1 keeps the historical constant size; see <see cref="SizeTo"/>.</summary>
        private float _sizeMultiplier = 1f;

        public static ElementalImpactFX Spawn(Vector3 pos, SpellElement element)
            => Spawn(pos, ElementPalette.For(element));

        /// <summary>
        /// The same burst, sized so its shockwave lands on <paramref name="damageRadius"/>.
        ///
        /// <para>An impact drawn at a constant while the circle underneath it comes from data is
        /// how "the particles do not match the area" happens: the shockwave ends at scale 3.6
        /// and <c>ElementalSprites.Ring</c> peaks at normalized radius 0.78, so the drawn edge
        /// has always been at 1.40 world units whatever the spell said. A radius of 0 keeps that
        /// historical size, so every existing caller is untouched.</para>
        /// </summary>
        public static ElementalImpactFX Spawn(Vector3 pos, SpellElement element, float damageRadius)
        {
            var fx = Spawn(pos, ElementPalette.For(element));
            if (fx != null) fx.SizeTo(damageRadius);
            return fx;
        }

        /// <summary>
        /// Scale the whole burst so the shockwave's bright band stops exactly on a radius.
        /// Clamped, because a very small field would otherwise produce a burst nobody can see
        /// and a very large one would fill the screen with a single flash.
        /// </summary>
        public void SizeTo(float damageRadius)
        {
            if (damageRadius <= 0f) return;
            const float RING_BAND_RADIUS = 0.39f;   // half of the sprite's 0.78 peak
            float wanted = damageRadius / (ShockwaveEnd * RING_BAND_RADIUS);
            _sizeMultiplier = Mathf.Clamp(wanted, 0.35f, 3f);
        }

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
            // LAYER_VFX with a small order, never Z_SKY as a sortingOrder on Entities. Z_SKY is
            // a Z DEPTH (600); used as an order on the Entities layer it puts the flash at 612
            // while every entity in the shipped world Y-sorts into the thousands, so the one
            // frame an impact exists for drew UNDER the character it hit. Third sighting of the
            // same mistake in this repository after LightningBoltFX and FacingIndicator.
            _flashSr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            _flashSr.sortingLayerName = SortingConfig.LAYER_VFX;
            _flashSr.sortingOrder = 12;
            _flashSr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;

            // Shockwave ring
            var ring = new GameObject("Shockwave");
            ring.transform.SetParent(transform, false);
            ring.transform.localScale = Vector3.one * ShockwaveStart;
            _ringSr = ring.AddComponent<SpriteRenderer>();
            _ringSr.sprite = _palette.ringSprite;
            _ringSr.color = _palette.glow;
            _ringSr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            _ringSr.sortingLayerName = SortingConfig.LAYER_VFX;
            _ringSr.sortingOrder = 11;
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
                sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = 7;
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
                float scale = Mathf.Lerp(ShockwaveStart, ShockwaveEnd, EaseOutCubic(u)) * _sizeMultiplier;
                _ringSr.transform.localScale = Vector3.one * scale;
                var c = _palette.glow;
                _ringSr.color = new Color(c.r, c.g, c.b, c.a * (1f - u));
            }
            if (_flashSr != null)
            {
                float scale = Mathf.Lerp(FlashScaleStart, FlashScaleEnd, u) * _sizeMultiplier;
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
