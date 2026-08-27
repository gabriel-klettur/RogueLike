using System.Reflection;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Universal procedural projectile visual driven by a <see cref="SpellElement"/>
    /// preset. Renders 4 stacked layers (halo + glow + core + hot core) with element
    /// specific palette, ember/trail emission, dynamic Light2D and motion stretch.
    /// On impact spawns <see cref="ElementalImpactFX"/> with the matching palette.
    ///
    /// Same architectural rules as <see cref="FireballVisual"/>: fully procedural
    /// (no asset deps), URP via reflection, sortingLayerID forced everywhere.
    /// </summary>
    public class ElementalProjectileVisual : MonoBehaviour, IProjectileVisual
    {
        [SerializeField] private SpellElement element = SpellElement.Dark;
        [SerializeField] private bool playImpactAudio = true;

        // Layer renderers
        private SpriteRenderer _hotCoreSr;
        private SpriteRenderer _coreSr;
        private SpriteRenderer _glowSr;
        private SpriteRenderer _haloSr;
        private SpriteRenderer[] _ghostSrs;
        private SpriteRenderer _accentSr;       // element-specific (snowflake, bolt, halo)

        // Runtime
        private float _seed;
        private Vector3 _lastPos;
        private float _emberTimer;
        private bool _impacted;
        private GameObject _light2DGo;
        private Component _light2DComponent;
        private ElementPalette _palette;
        private float _spinAngle;

        // Configurable from spawning code BEFORE first Update, otherwise the default
        // (Dark) is used.
        public void SetElement(SpellElement e)
        {
            if (_palette.element == e && _coreSr != null) return;
            element = e;
            if (_coreSr != null)
            {
                // Already built — rebuild visual rig with new palette.
                ClearVisual();
                BuildVisual();
            }
        }

        public void OnImpact(Vector3 worldPos)
        {
            if (_impacted) return;
            _impacted = true;
            ElementalImpactFX.Spawn(worldPos, _palette);
            if (playImpactAudio)
            {
                var audio = ServiceLocator.Get<IAudioService>();
                if (audio != null) audio.PlaySfxById(_palette.impactSfxId);
            }
        }

        private void Awake()
        {
            _palette = ElementPalette.For(element);
            ElementalSprites.EnsureAll();
            ResolveLight2D();
            BuildVisual();
            _seed = Random.Range(0f, 100f);
        }

        private void OnEnable()
        {
            _impacted = false;
            _emberTimer = 0f;
            _lastPos = transform.position;
        }

        private void OnDisable()
        {
            // Pool-safe cleanup of dynamic light + element-specific spawners.
            if (_light2DGo != null)
            {
                Destroy(_light2DGo);
                _light2DGo = null;
                _light2DComponent = null;
            }
        }

        private void Update()
        {
            float t = Time.time + _seed;

            // Multi-octave flicker. Lightning flickers much faster; ice barely flickers.
            float flickerRate = _palette.flickerRate;
            float flicker = 1f + 0.18f * Mathf.Sin(t * flickerRate) + 0.10f * Mathf.Sin(t * flickerRate * 1.7f + 1.2f);
            float flickerSlow = 1f + 0.20f * Mathf.Sin(t * flickerRate * 0.55f + 0.4f);

            // Motion stretch
            Vector3 pos = transform.position;
            Vector3 delta = pos - _lastPos;
            float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            float speedFactor = Mathf.Clamp01(speed / 12f);
            float stretchX = 1f + speedFactor * _palette.stretch;
            float stretchY = 1f - speedFactor * _palette.stretch * 0.32f;

            if (_haloSr != null)
                _haloSr.transform.localScale = new Vector3(_palette.haloScale * stretchX * flickerSlow,
                                                           _palette.haloScale * stretchY * flickerSlow, 1f);
            if (_glowSr != null)
            {
                _glowSr.transform.localScale = new Vector3(_palette.glowScale * stretchX * flicker,
                                                           _palette.glowScale * stretchY * flicker, 1f);
                var c = _palette.glow;
                _glowSr.color = new Color(c.r, c.g, c.b, c.a * (0.85f + 0.15f * Mathf.Sin(t * 18f)));
            }
            if (_coreSr != null)
                _coreSr.transform.localScale = new Vector3(_palette.coreScale * stretchX * flicker,
                                                           _palette.coreScale * stretchY * flicker, 1f);
            if (_hotCoreSr != null)
                _hotCoreSr.transform.localScale = Vector3.one * _palette.hotCoreScale * flicker;

            // Accent layer (element-specific): rotates / flickers per palette
            if (_accentSr != null)
            {
                _spinAngle += _palette.accentSpinSpeed * Time.deltaTime;
                _accentSr.transform.localRotation = Quaternion.Euler(0f, 0f, _spinAngle);
                float aa = _palette.accent.a * (0.7f + 0.3f * Mathf.Sin(t * (flickerRate * 0.8f) + 0.7f));
                _accentSr.color = new Color(_palette.accent.r, _palette.accent.g, _palette.accent.b, aa);
            }

            // Ghost trail
            if (_ghostSrs != null && _ghostSrs.Length > 0)
            {
                for (int i = 0; i < _ghostSrs.Length; i++)
                {
                    var g = _ghostSrs[i];
                    if (g == null) continue;
                    float u = (i + 1) / (float)(_ghostSrs.Length + 1);
                    g.transform.localPosition = new Vector3(-(_palette.ghostSpacing * (i + 1)), 0f, 0f);
                    float gAlpha = (1f - u) * 0.55f * speedFactor;
                    float gScale = (1f - u * 0.5f) * _palette.glowScale * (0.9f + 0.1f * Mathf.Sin(t * 20f + i));
                    g.transform.localScale = Vector3.one * gScale;
                    var gc = _palette.glow;
                    g.color = new Color(gc.r, gc.g, gc.b, gAlpha);
                }
            }

            // Continuous ember/spark/shard emission while moving
            _emberTimer -= Time.deltaTime;
            if (!_impacted && delta.sqrMagnitude > 0.0001f && _emberTimer <= 0f)
            {
                _emberTimer = _palette.emberInterval;
                SpawnEmber();
            }

            // Light2D modulation
            if (_light2DComponent != null && _l2dIntensity != null)
            {
                try
                {
                    float intensity = _palette.lightIntensity *
                        (0.85f + 0.15f * Mathf.Sin(t * 24f) + 0.10f * Mathf.Sin(t * 13f));
                    _l2dIntensity.SetValue(_light2DComponent, intensity);
                }
                catch { /* reflection safety */ }
            }

            _lastPos = pos;
        }

        // ── Build ─────────────────────────────────────────────────────

        private void ClearVisual()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var ch = transform.GetChild(i);
                if (ch != null) Destroy(ch.gameObject);
            }
            _hotCoreSr = _coreSr = _glowSr = _haloSr = _accentSr = null;
            _ghostSrs = null;
            _light2DGo = null;
            _light2DComponent = null;
        }

        private void BuildVisual()
        {
            _palette = ElementPalette.For(element);
            int order = SortingConfig.Z_SKY;

            _haloSr   = CreateChild("Halo",    _palette.haloSprite,    _palette.halo,    _palette.haloScale,    order + 2);
            _glowSr   = CreateChild("Glow",    _palette.glowSprite,    _palette.glow,    _palette.glowScale,    order + 3);
            _coreSr   = CreateChild("Core",    _palette.coreSprite,    _palette.core,    _palette.coreScale,    order + 5);
            _hotCoreSr = CreateChild("HotCore", _palette.hotCoreSprite, _palette.hotCore, _palette.hotCoreScale, order + 6);

            // Element-specific accent (snowflake / bolt / sparkle / wisp)
            if (_palette.accentSprite != null)
                _accentSr = CreateChild("Accent", _palette.accentSprite, _palette.accent, _palette.accentScale, order + 4);

            // Ghost trail
            int ghostCount = _palette.ghostCount;
            _ghostSrs = new SpriteRenderer[ghostCount];
            for (int i = 0; i < ghostCount; i++)
                _ghostSrs[i] = CreateChild($"Ghost{i}", _palette.glowSprite, _palette.glow, _palette.glowScale, order + 1);

            // Hide root placeholder sprite (added by ProjectilePrefabFactory).
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr != null) rootSr.enabled = false;

            CreateDynamicLight();
        }

        private SpriteRenderer CreateChild(string name, Sprite sprite, Color color, float scale, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_ENTITIES);
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = order;
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private void CreateDynamicLight()
        {
            if (_light2DType == null) return;

            _light2DGo = new GameObject("ElementLight");
            _light2DGo.transform.SetParent(transform, false);
            _light2DGo.transform.localPosition = Vector3.zero;
            try
            {
                _light2DComponent = _light2DGo.AddComponent(_light2DType);
                if (_l2dLightType != null)
                {
                    var enumType = _l2dLightType.PropertyType;
                    _l2dLightType.SetValue(_light2DComponent, System.Enum.ToObject(enumType, 3)); // 3 = Point (URP 14: Sprite=2)
                }
                if (_l2dColor != null)     _l2dColor.SetValue(_light2DComponent, _palette.lightColor);
                if (_l2dIntensity != null) _l2dIntensity.SetValue(_light2DComponent, _palette.lightIntensity);
                if (_l2dOuter != null)     _l2dOuter.SetValue(_light2DComponent, _palette.lightOuter);
                if (_l2dInner != null)     _l2dInner.SetValue(_light2DComponent, _palette.lightInner);
                if (_l2dFalloff != null)   _l2dFalloff.SetValue(_light2DComponent, 0.85f);
            }
            catch
            {
                if (_light2DGo != null) Destroy(_light2DGo);
                _light2DGo = null;
                _light2DComponent = null;
            }
        }

        // ── Trail ember ───────────────────────────────────────────────

        private void SpawnEmber()
        {
            var go = new GameObject("Spark");
            go.transform.position = transform.position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _palette.emberSprite;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_ENTITIES);
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = SortingConfig.Z_SKY + 4;
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            sr.color = Color.Lerp(_palette.core, _palette.glow, Random.value);

            // Velocity: backward + jitter (per-element drag/buoyancy)
            Vector2 back = -(Vector2)transform.right;
            Vector2 jitter = Random.insideUnitCircle * _palette.emberJitter;
            Vector2 vel = back * Random.Range(0.4f, 1.4f) + jitter;
            var ember = go.AddComponent<ElementalEmber>();
            ember.Init(vel, _palette.emberLifetime, Random.Range(0.05f, 0.13f),
                       _palette.emberDrag, _palette.emberBuoyancy);
        }

        // ── URP Light2D reflection ────────────────────────────────────

        private static System.Type _light2DType;
        private static PropertyInfo _l2dLightType;
        private static PropertyInfo _l2dColor;
        private static PropertyInfo _l2dIntensity;
        private static PropertyInfo _l2dOuter;
        private static PropertyInfo _l2dInner;
        private static PropertyInfo _l2dFalloff;
        private static bool _l2dResolved;

        internal static System.Type GetLight2DType()                  { ResolveLight2D(); return _light2DType; }
        internal static PropertyInfo GetLight2DLightTypeProp()        { ResolveLight2D(); return _l2dLightType; }
        internal static PropertyInfo GetLight2DColorProp()            { ResolveLight2D(); return _l2dColor; }
        internal static PropertyInfo GetLight2DIntensityProp()        { ResolveLight2D(); return _l2dIntensity; }
        internal static PropertyInfo GetLight2DOuterProp()            { ResolveLight2D(); return _l2dOuter; }
        internal static PropertyInfo GetLight2DInnerProp()            { ResolveLight2D(); return _l2dInner; }
        internal static PropertyInfo GetLight2DFalloffProp()          { ResolveLight2D(); return _l2dFalloff; }

        private static void ResolveLight2D()
        {
            if (_l2dResolved) return;
            _l2dResolved = true;
            _light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (_light2DType == null) return;
            var flags = BindingFlags.Public | BindingFlags.Instance;
            _l2dLightType = _light2DType.GetProperty("lightType", flags);
            _l2dColor     = _light2DType.GetProperty("color", flags);
            _l2dIntensity = _light2DType.GetProperty("intensity", flags);
            _l2dOuter     = _light2DType.GetProperty("pointLightOuterRadius", flags);
            _l2dInner     = _light2DType.GetProperty("pointLightInnerRadius", flags);
            _l2dFalloff   = _light2DType.GetProperty("falloffIntensity", flags);
        }
    }
}
