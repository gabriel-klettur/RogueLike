using System.Reflection;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Epic fireball impact: shockwave ring expansion + bright flash + radial ember burst
    /// + Light2D pulse + camera shake. Self-destructs after the longest sub-effect ends.
    ///
    /// Spawned from FireballVisual.OnImpact() via FireballImpactFX.Spawn(pos, color).
    /// </summary>
    public class FireballImpactFX : MonoBehaviour
    {
        // ── Tuning ────────────────────────────────────────────────────
        private const float Duration         = 0.55f;     // seconds
        private const float ShockwaveStart   = 0.25f;     // initial scale
        private const float ShockwaveEnd     = 3.4f;      // final scale
        private const float FlashScaleStart  = 0.50f;
        private const float FlashScaleEnd    = 2.8f;
        private const int   EmberBurstCount  = 22;
        private const float EmberBurstSpeed  = 5.5f;
        private const float EmberLifeMin     = 0.35f;
        private const float EmberLifeMax     = 0.75f;
        private const float LightPulseRadius = 5.0f;
        private const float LightPulsePeak   = 5.5f;
        private const float ShakeAmplitude   = 0.18f;     // world units
        private const float ShakeDuration    = 0.22f;

        private SpriteRenderer _flashSr;
        private SpriteRenderer _ringSr;
        private GameObject _light2DGo;
        private Component _light2DComponent;
        private float _t;
        private Color _baseColor;

        /// <summary>Factory: spawns a configured FireballImpactFX at the given world position.</summary>
        public static FireballImpactFX Spawn(Vector3 pos, Color baseColor)
        {
            var go = new GameObject("FireballImpactFX");
            go.transform.position = pos;
            var fx = go.AddComponent<FireballImpactFX>();
            fx._baseColor = baseColor;
            fx.Build();
            fx.SpawnEmberBurst();
            CameraShake.Trigger(ShakeAmplitude, ShakeDuration);
            return fx;
        }

        private void Build()
        {
            // Bright flash core
            var flash = new GameObject("Flash");
            flash.transform.SetParent(transform, false);
            flash.transform.localScale = Vector3.one * FlashScaleStart;
            _flashSr = flash.AddComponent<SpriteRenderer>();
            _flashSr.sprite = FireballVisual.SharedHotCoreSprite;
            _flashSr.color = new Color(1f, 0.95f, 0.7f, 1f);
            _flashSr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _flashSr.sortingOrder = SortingConfig.Z_SKY + 12;
            _flashSr.material = FireballVisual.SharedUnlitMaterial;

            // Expanding shockwave ring
            var ring = new GameObject("Shockwave");
            ring.transform.SetParent(transform, false);
            ring.transform.localScale = Vector3.one * ShockwaveStart;
            _ringSr = ring.AddComponent<SpriteRenderer>();
            _ringSr.sprite = FireballVisual.SharedRingSprite;
            _ringSr.color = new Color(1f, 0.55f, 0.15f, 1f);
            _ringSr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _ringSr.sortingOrder = SortingConfig.Z_SKY + 11;
            _ringSr.material = FireballVisual.SharedUnlitMaterial;

            // Bright Light2D pulse
            var l2dType = FireballVisual.GetLight2DType();
            if (l2dType != null)
            {
                _light2DGo = new GameObject("ImpactLight");
                _light2DGo.transform.SetParent(transform, false);
                _light2DGo.transform.localPosition = Vector3.zero;
                try
                {
                    _light2DComponent = _light2DGo.AddComponent(l2dType);
                    var lt = FireballVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light2DComponent, System.Enum.ToObject(lt.PropertyType, 2));
                    var col = FireballVisual.GetLight2DColorProp();
                    if (col != null) col.SetValue(_light2DComponent, new Color(1f, 0.6f, 0.2f, 1f));
                    var inten = FireballVisual.GetLight2DIntensityProp();
                    if (inten != null) inten.SetValue(_light2DComponent, LightPulsePeak);
                    var outer = FireballVisual.GetLight2DOuterProp();
                    if (outer != null) outer.SetValue(_light2DComponent, LightPulseRadius);
                    var inner = FireballVisual.GetLight2DInnerProp();
                    if (inner != null) inner.SetValue(_light2DComponent, 0.4f);
                    var fall = FireballVisual.GetLight2DFalloffProp();
                    if (fall != null) fall.SetValue(_light2DComponent, 0.85f);
                }
                catch
                {
                    if (_light2DGo != null) SafeDestroy.Of(_light2DGo);
                    _light2DComponent = null;
                }
            }
        }

        private void SpawnEmberBurst()
        {
            var unlit = FireballVisual.SharedUnlitMaterial;
            var ember = FireballVisual.SharedEmberSprite;
            for (int i = 0; i < EmberBurstCount; i++)
            {
                float angle = (i / (float)EmberBurstCount) * Mathf.PI * 2f
                              + Random.Range(-0.15f, 0.15f);
                float spd = EmberBurstSpeed * Random.Range(0.55f, 1.15f);
                Vector2 vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spd;

                var go = new GameObject("BurstEmber");
                go.transform.position = transform.position;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ember;
                sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                sr.sortingOrder = SortingConfig.Z_SKY + 9;
                sr.material = unlit;
                sr.color = Color.Lerp(new Color(1f, 0.95f, 0.55f, 1f),
                                      new Color(1f, 0.35f, 0.05f, 1f), Random.value);

                go.AddComponent<FireballEmber>().Init(vel,
                    Random.Range(EmberLifeMin, EmberLifeMax),
                    Random.Range(0.10f, 0.20f));
            }
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float u = Mathf.Clamp01(_t / Duration);

            // Shockwave: ease-out expansion, alpha fades out
            float ringT = 1f - Mathf.Pow(1f - u, 2f); // ease-out quadratic
            if (_ringSr != null)
            {
                float s = Mathf.Lerp(ShockwaveStart, ShockwaveEnd, ringT);
                _ringSr.transform.localScale = Vector3.one * s;
                float a = (1f - u) * (1f - u);
                var c = _ringSr.color;
                c.a = a;
                _ringSr.color = c;
            }

            // Flash: quick punch — peak in first 25%, then fade
            if (_flashSr != null)
            {
                float flashAlpha;
                float flashScale;
                if (u < 0.25f)
                {
                    float k = u / 0.25f;
                    flashScale = Mathf.Lerp(FlashScaleStart, FlashScaleEnd * 0.7f, k);
                    flashAlpha = 1f;
                }
                else
                {
                    float k = (u - 0.25f) / 0.75f;
                    flashScale = Mathf.Lerp(FlashScaleEnd * 0.7f, FlashScaleEnd, k);
                    flashAlpha = 1f - k;
                }
                _flashSr.transform.localScale = Vector3.one * flashScale;
                var c = _flashSr.color;
                c.a = flashAlpha;
                _flashSr.color = c;
            }

            // Light pulse: peak at start, exponential decay
            if (_light2DComponent != null)
            {
                var inten = FireballVisual.GetLight2DIntensityProp();
                if (inten != null)
                {
                    try
                    {
                        float decay = Mathf.Exp(-u * 5f);
                        inten.SetValue(_light2DComponent, LightPulsePeak * decay);
                    }
                    catch { /* reflection safety */ }
                }
            }

            if (u >= 1f) SafeDestroy.Of(gameObject);
        }
    }

    /// <summary>
    /// Lightweight, self-installing camera shake helper.
    /// Applies a transient offset to Camera.main each LateUpdate and removes it on the next
    /// LateUpdate, so it composes safely with camera-follow scripts (which write the
    /// camera's "rest" position in their own Update/LateUpdate).
    /// </summary>
    internal class CameraShake : MonoBehaviour
    {
        private static CameraShake _instance;

        private Camera _cam;
        private Vector3 _appliedOffset;
        private float _amplitude;
        private float _duration;
        private float _t;

        public static void Trigger(float amplitude, float duration)
        {
            EnsureInstance();
            if (_instance == null) return;
            // Restart whichever effect is stronger.
            if (amplitude > _instance._amplitude) _instance._amplitude = amplitude;
            if (duration  > _instance._duration ) _instance._duration  = duration;
            _instance._t = 0f;
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var cam = Camera.main;
            if (cam == null) return;
            var go = new GameObject("CameraShake");
            // DontDestroyOnLoad is illegal in EditMode (and unnecessary for EditMode tests).
            if (Application.isPlaying) DontDestroyOnLoad(go);
            _instance = go.AddComponent<CameraShake>();
            _instance._cam = cam;
        }

        private void LateUpdate()
        {
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            // Remove the offset applied last frame so we don't drift.
            if (_appliedOffset != Vector3.zero)
            {
                _cam.transform.position -= _appliedOffset;
                _appliedOffset = Vector3.zero;
            }

            if (_duration <= 0f || _t >= _duration) return;

            _t += Time.unscaledDeltaTime;
            float falloff = 1f - Mathf.Clamp01(_t / _duration);
            float x = (Random.value * 2f - 1f) * _amplitude * falloff;
            float y = (Random.value * 2f - 1f) * _amplitude * falloff;
            _appliedOffset = new Vector3(x, y, 0f);
            _cam.transform.position += _appliedOffset;
        }
    }
}
