using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Element preset — drives palette, trail behaviour, ember type and impact style
    /// for <see cref="ElementalProjectileVisual"/>.
    /// </summary>
    public enum SpellElement
    {
        Dark,
        Ice,
        Light,
        Lightning,
        Boomerang,
        Arcane,
        Fire,
    }

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
            sr.material = ElementalSprites.SharedUnlitMaterial;
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
                    _l2dLightType.SetValue(_light2DComponent, System.Enum.ToObject(enumType, 2)); // Point
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
            sr.material = ElementalSprites.SharedUnlitMaterial;
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

    /// <summary>
    /// Element-specific palette + behaviour. Returned by <see cref="For"/>.
    /// Sprites resolve lazily via <see cref="ElementalSprites"/>.
    /// </summary>
    internal struct ElementPalette
    {
        public SpellElement element;
        public Color hotCore, core, glow, halo, accent, lightColor;
        public float coreScale, glowScale, haloScale, hotCoreScale, accentScale;
        public int ghostCount;
        public float ghostSpacing;
        public float emberInterval, emberLifetime, emberJitter, emberDrag, emberBuoyancy;
        public float flickerRate;
        public float stretch;
        public float lightIntensity, lightOuter, lightInner;
        public float accentSpinSpeed;
        public Sprite hotCoreSprite, coreSprite, glowSprite, haloSprite, emberSprite, accentSprite, ringSprite;
        public string impactSfxId;

        public static ElementPalette For(SpellElement e)
        {
            ElementalSprites.EnsureAll();
            switch (e)
            {
                case SpellElement.Ice:        return Ice();
                case SpellElement.Light:      return Light();
                case SpellElement.Lightning:  return Lightning();
                case SpellElement.Boomerang:  return Boomerang();
                case SpellElement.Arcane:     return Arcane();
                case SpellElement.Fire:       return Fire();
                case SpellElement.Dark:
                default:                      return Dark();
            }
        }

        // Dark: deep purple/black void with violet halo, slow swirling wisps.
        private static ElementPalette Dark() => new ElementPalette
        {
            element = SpellElement.Dark,
            hotCore = new Color(0.85f, 0.55f, 1.00f, 1f),
            core    = new Color(0.55f, 0.20f, 0.85f, 1f),
            glow    = new Color(0.30f, 0.05f, 0.55f, 0.70f),
            halo    = new Color(0.10f, 0.00f, 0.25f, 0.30f),
            accent  = new Color(0.65f, 0.30f, 1.00f, 0.55f),
            lightColor = new Color(0.55f, 0.20f, 1.00f, 1f),
            coreScale = 0.42f, glowScale = 1.05f, haloScale = 1.85f, hotCoreScale = 0.22f, accentScale = 0.95f,
            ghostCount = 6, ghostSpacing = 0.11f,
            emberInterval = 0.04f, emberLifetime = 0.65f, emberJitter = 0.9f, emberDrag = 1.4f, emberBuoyancy = -0.4f,
            flickerRate = 12f, stretch = 0.45f,
            lightIntensity = 1.6f, lightOuter = 2.4f, lightInner = 0.3f,
            accentSpinSpeed = -65f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Wisp,
            accentSprite  = ElementalSprites.Wisp,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_dark_impact",
        };

        // Ice: cyan/white frost with snowflake accent + sharp shards.
        private static ElementPalette Ice() => new ElementPalette
        {
            element = SpellElement.Ice,
            hotCore = new Color(0.92f, 0.99f, 1.00f, 1f),
            core    = new Color(0.65f, 0.92f, 1.00f, 1f),
            glow    = new Color(0.35f, 0.75f, 1.00f, 0.65f),
            halo    = new Color(0.20f, 0.55f, 1.00f, 0.25f),
            accent  = new Color(0.85f, 0.98f, 1.00f, 0.85f),
            lightColor = new Color(0.55f, 0.85f, 1.00f, 1f),
            coreScale = 0.40f, glowScale = 0.95f, haloScale = 1.65f, hotCoreScale = 0.20f, accentScale = 0.85f,
            ghostCount = 5, ghostSpacing = 0.10f,
            emberInterval = 0.03f, emberLifetime = 0.55f, emberJitter = 0.6f, emberDrag = 0.6f, emberBuoyancy = -1.2f,
            flickerRate = 8f, stretch = 0.30f,
            lightIntensity = 1.5f, lightOuter = 2.2f, lightInner = 0.4f,
            accentSpinSpeed = 40f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Snowflake,
            accentSprite  = ElementalSprites.Snowflake,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_ice_impact",
        };

        // Light/Holy: warm white-yellow with sparkle starburst accent.
        private static ElementPalette Light() => new ElementPalette
        {
            element = SpellElement.Light,
            hotCore = new Color(1.00f, 1.00f, 0.95f, 1f),
            core    = new Color(1.00f, 0.95f, 0.65f, 1f),
            glow    = new Color(1.00f, 0.85f, 0.40f, 0.65f),
            halo    = new Color(1.00f, 0.95f, 0.65f, 0.30f),
            accent  = new Color(1.00f, 1.00f, 0.85f, 0.85f),
            lightColor = new Color(1.00f, 0.90f, 0.55f, 1f),
            coreScale = 0.42f, glowScale = 1.00f, haloScale = 1.75f, hotCoreScale = 0.22f, accentScale = 1.10f,
            ghostCount = 5, ghostSpacing = 0.10f,
            emberInterval = 0.025f, emberLifetime = 0.50f, emberJitter = 0.8f, emberDrag = 1.0f, emberBuoyancy = 0.6f,
            flickerRate = 20f, stretch = 0.40f,
            lightIntensity = 2.4f, lightOuter = 2.8f, lightInner = 0.5f,
            accentSpinSpeed = 90f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.SparkleStar,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_light_impact",
        };

        // Lightning: blue-white plasma with crackling bolt accent + fast flicker.
        private static ElementPalette Lightning() => new ElementPalette
        {
            element = SpellElement.Lightning,
            hotCore = new Color(1.00f, 1.00f, 1.00f, 1f),
            core    = new Color(0.75f, 0.95f, 1.00f, 1f),
            glow    = new Color(0.40f, 0.75f, 1.00f, 0.75f),
            halo    = new Color(0.25f, 0.55f, 1.00f, 0.30f),
            accent  = new Color(1.00f, 1.00f, 1.00f, 0.95f),
            lightColor = new Color(0.65f, 0.85f, 1.00f, 1f),
            coreScale = 0.35f, glowScale = 0.85f, haloScale = 1.50f, hotCoreScale = 0.18f, accentScale = 1.10f,
            ghostCount = 4, ghostSpacing = 0.12f,
            emberInterval = 0.02f, emberLifetime = 0.30f, emberJitter = 1.4f, emberDrag = 3.0f, emberBuoyancy = 0f,
            flickerRate = 70f, stretch = 0.55f,
            lightIntensity = 2.2f, lightOuter = 2.4f, lightInner = 0.3f,
            accentSpinSpeed = 0f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.Bolt,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_lightning_impact",
        };

        // Boomerang: green/wood spinning blade with leaf trail.
        private static ElementPalette Boomerang() => new ElementPalette
        {
            element = SpellElement.Boomerang,
            hotCore = new Color(0.95f, 1.00f, 0.65f, 1f),
            core    = new Color(0.55f, 0.85f, 0.30f, 1f),
            glow    = new Color(0.40f, 0.65f, 0.20f, 0.55f),
            halo    = new Color(0.25f, 0.45f, 0.10f, 0.20f),
            accent  = new Color(0.85f, 0.75f, 0.40f, 1f),
            lightColor = new Color(0.65f, 0.95f, 0.45f, 1f),
            coreScale = 0.30f, glowScale = 0.75f, haloScale = 1.20f, hotCoreScale = 0.15f, accentScale = 0.80f,
            ghostCount = 3, ghostSpacing = 0.13f,
            emberInterval = 0.05f, emberLifetime = 0.45f, emberJitter = 0.5f, emberDrag = 1.6f, emberBuoyancy = -0.3f,
            flickerRate = 6f, stretch = 0.20f,
            lightIntensity = 0.9f, lightOuter = 1.4f, lightInner = 0.2f,
            accentSpinSpeed = 720f,                   // very fast spin
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.Blade,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_boomerang_impact",
        };

        // Arcane: bright magenta/cyan dual-tone with star accent.
        private static ElementPalette Arcane() => new ElementPalette
        {
            element = SpellElement.Arcane,
            hotCore = new Color(1.00f, 0.95f, 1.00f, 1f),
            core    = new Color(0.95f, 0.45f, 1.00f, 1f),
            glow    = new Color(0.75f, 0.30f, 1.00f, 0.65f),
            halo    = new Color(0.45f, 0.20f, 0.85f, 0.30f),
            accent  = new Color(0.95f, 0.85f, 1.00f, 0.85f),
            lightColor = new Color(0.85f, 0.45f, 1.00f, 1f),
            coreScale = 0.40f, glowScale = 0.95f, haloScale = 1.65f, hotCoreScale = 0.20f, accentScale = 1.00f,
            ghostCount = 5, ghostSpacing = 0.10f,
            emberInterval = 0.025f, emberLifetime = 0.55f, emberJitter = 0.9f, emberDrag = 1.0f, emberBuoyancy = 0.3f,
            flickerRate = 18f, stretch = 0.40f,
            lightIntensity = 2.0f, lightOuter = 2.5f, lightInner = 0.4f,
            accentSpinSpeed = 120f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.SparkleStar,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_arcane_impact",
        };

        // Fire: orange/red flame with hot yellow core, rising embers, deep orange light.
        private static ElementPalette Fire() => new ElementPalette
        {
            element = SpellElement.Fire,
            hotCore = new Color(1.00f, 0.95f, 0.55f, 1f),
            core    = new Color(1.00f, 0.55f, 0.10f, 1f),
            glow    = new Color(1.00f, 0.30f, 0.05f, 0.75f),
            halo    = new Color(0.65f, 0.10f, 0.00f, 0.30f),
            accent  = new Color(1.00f, 0.80f, 0.30f, 0.85f),
            lightColor = new Color(1.00f, 0.55f, 0.20f, 1f),
            coreScale = 0.42f, glowScale = 1.05f, haloScale = 1.80f, hotCoreScale = 0.22f, accentScale = 0.95f,
            ghostCount = 5, ghostSpacing = 0.10f,
            emberInterval = 0.02f, emberLifetime = 0.60f, emberJitter = 1.0f, emberDrag = 1.2f, emberBuoyancy = 1.4f,
            flickerRate = 22f, stretch = 0.45f,
            lightIntensity = 2.4f, lightOuter = 2.8f, lightInner = 0.4f,
            accentSpinSpeed = 60f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.Sparkle,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_fire_impact",
        };
    }

    /// <summary>
    /// Trailing ember/spark/shard with parameterized drag and buoyancy. Drives all
    /// element trails (fiery embers rise, ice shards fall, lightning sparks scatter).
    /// </summary>
    internal class ElementalEmber : MonoBehaviour
    {
        private Vector2 _vel;
        private float _life, _age, _scale, _drag, _buoyancy;
        private SpriteRenderer _sr;

        public void Init(Vector2 velocity, float lifetime, float scale, float drag, float buoyancy)
        {
            _vel = velocity;
            _life = Mathf.Max(0.05f, lifetime);
            _scale = scale;
            _drag = drag;
            _buoyancy = buoyancy;
            transform.localScale = Vector3.one * _scale;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            float t = _age / _life;
            if (t >= 1f) { Destroy(gameObject); return; }

            _vel *= 1f - _drag * dt;
            _vel.y += _buoyancy * dt;
            transform.position += (Vector3)(_vel * dt);

            float scaleT = 1f - t * 0.6f;
            transform.localScale = Vector3.one * _scale * scaleT;
            if (_sr != null)
            {
                var c = _sr.color;
                c.a = (1f - t) * (1f - t);
                _sr.color = c;
            }
        }
    }

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
            CameraShake.Trigger(ShakeAmplitude, ShakeDuration);
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
            _flashSr.material = ElementalSprites.SharedUnlitMaterial;

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
            _ringSr.material = ElementalSprites.SharedUnlitMaterial;

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
                    if (lt != null) lt.SetValue(_light2DComponent, System.Enum.ToObject(lt.PropertyType, 2));
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
                sr.material = ElementalSprites.SharedUnlitMaterial;
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

    /// <summary>
    /// Shared procedural sprite library used by elemental visuals. Generates radial
    /// gradients, rings, snowflakes, lightning bolts, sparkles, blades and wisps once
    /// and caches them statically.
    /// </summary>
    internal static class ElementalSprites
    {
        private static Sprite _hotCore, _core, _glow, _halo, _ring, _sparkle, _sparkleStar, _snowflake, _bolt, _blade, _wisp;
        private static Material _unlitMaterial;

        public static Sprite HotCore       { get { EnsureAll(); return _hotCore; } }
        public static Sprite Core          { get { EnsureAll(); return _core; } }
        public static Sprite Glow          { get { EnsureAll(); return _glow; } }
        public static Sprite Halo          { get { EnsureAll(); return _halo; } }
        public static Sprite Ring          { get { EnsureAll(); return _ring; } }
        public static Sprite Sparkle       { get { EnsureAll(); return _sparkle; } }
        public static Sprite SparkleStar   { get { EnsureAll(); return _sparkleStar; } }
        public static Sprite Snowflake     { get { EnsureAll(); return _snowflake; } }
        public static Sprite Bolt          { get { EnsureAll(); return _bolt; } }
        public static Sprite Blade         { get { EnsureAll(); return _blade; } }
        public static Sprite Wisp          { get { EnsureAll(); return _wisp; } }

        public static Material SharedUnlitMaterial { get { EnsureAll(); return _unlitMaterial; } }

        public static void EnsureAll()
        {
            if (_hotCore == null)     _hotCore     = Radial(32, HotPx);
            if (_core == null)        _core        = Radial(48, CorePx);
            if (_glow == null)        _glow        = Radial(96, GlowPx);
            if (_halo == null)        _halo        = Radial(128, HaloPx);
            if (_ring == null)        _ring        = Radial(128, RingPx);
            if (_sparkle == null)     _sparkle     = Radial(16, SparkPx);
            if (_sparkleStar == null) _sparkleStar = Star(48);
            if (_snowflake == null)   _snowflake   = MakeSnowflake(48);
            if (_bolt == null)        _bolt        = MakeBolt(48);
            if (_blade == null)       _blade       = MakeBlade(48);
            if (_wisp == null)        _wisp        = MakeWisp(48);

            if (_unlitMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                         ?? Shader.Find("Sprites/Default");
                _unlitMaterial = new Material(sh);
            }
        }

        // Radial gradient generator
        private static Sprite Radial(int size, System.Func<float, Color> fn)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    px[y * size + x] = fn(Mathf.Sqrt(dx * dx + dy * dy));
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Color HotPx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 1.1f); return new Color(1f, 1f, 1f, a); }
        private static Color CorePx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 1.6f); return new Color(1f, 1f, 1f, a); }
        private static Color GlowPx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 2.4f) * 0.85f; return new Color(1f, 1f, 1f, a); }
        private static Color HaloPx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 3.2f) * 0.55f; return new Color(1f, 1f, 1f, a); }
        private static Color SparkPx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 1.8f); return new Color(1f, 1f, 1f, a); }
        private static Color RingPx(float d)
        {
            if (d > 1f) return Color.clear;
            float ringPos = 0.78f, thickness = 0.18f;
            float diff = Mathf.Abs(d - ringPos);
            float a = Mathf.Pow(Mathf.Clamp01(1f - diff / thickness), 1.6f);
            return new Color(1f, 1f, 1f, a);
        }

        // 4-pointed star (sparkle starburst)
        private static Sprite Star(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size]; float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float horiz = Mathf.Max(0f, 1f - Mathf.Abs(dy) * 8f) * Mathf.Max(0f, 1f - Mathf.Abs(dx));
                    float vert  = Mathf.Max(0f, 1f - Mathf.Abs(dx) * 8f) * Mathf.Max(0f, 1f - Mathf.Abs(dy));
                    float diagA = Mathf.Max(0f, 1f - Mathf.Abs(dx + dy) * 12f) * Mathf.Max(0f, 1f - Mathf.Sqrt(dx * dx + dy * dy));
                    float diagB = Mathf.Max(0f, 1f - Mathf.Abs(dx - dy) * 12f) * Mathf.Max(0f, 1f - Mathf.Sqrt(dx * dx + dy * dy));
                    float center = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
                    float a = Mathf.Clamp01(horiz + vert + 0.6f * (diagA + diagB) + 0.7f * center);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Snowflake: 6-armed star with cross-arms
        private static Sprite MakeSnowflake(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size]; float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    if (r <= 1f)
                    {
                        // 6 arms via 3 line equations
                        for (int k = 0; k < 3; k++)
                        {
                            float ang = k * Mathf.PI / 3f;
                            float cx = Mathf.Cos(ang), cy = Mathf.Sin(ang);
                            float along = dx * cx + dy * cy;
                            float perp = -dx * cy + dy * cx;
                            float arm = Mathf.Max(0f, 1f - Mathf.Abs(perp) * 14f) * Mathf.Max(0f, 1f - Mathf.Abs(along));
                            // small cross-arms at 0.4 and 0.7
                            float cross1 = Mathf.Max(0f, 1f - Mathf.Abs(Mathf.Abs(along) - 0.4f) * 22f) * Mathf.Max(0f, 1f - Mathf.Abs(perp) * 6f);
                            float cross2 = Mathf.Max(0f, 1f - Mathf.Abs(Mathf.Abs(along) - 0.7f) * 22f) * Mathf.Max(0f, 1f - Mathf.Abs(perp) * 6f);
                            a = Mathf.Max(a, arm + 0.7f * (cross1 + cross2));
                        }
                        a = Mathf.Clamp01(a);
                        a *= Mathf.Pow(1f - r, 0.4f); // soft fade outward
                    }
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Lightning bolt: stylised zig-zag
        private static Sprite MakeBolt(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            float c = size * 0.5f;
            // Polyline points (normalised -1..1)
            var pts = new[]
            {
                new Vector2(-0.05f, 0.95f),
                new Vector2( 0.20f, 0.30f),
                new Vector2(-0.10f, 0.10f),
                new Vector2( 0.15f, -0.20f),
                new Vector2(-0.20f, -0.45f),
                new Vector2( 0.05f, -0.95f),
            };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float bestDist = 10f;
                    for (int k = 0; k < pts.Length - 1; k++)
                        bestDist = Mathf.Min(bestDist, DistSegment(new Vector2(dx, dy), pts[k], pts[k + 1]));
                    float core = Mathf.Max(0f, 1f - bestDist * 18f);
                    float halo = Mathf.Max(0f, 1f - bestDist * 6f) * 0.45f;
                    float a = Mathf.Clamp01(core + halo);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Curved blade (boomerang)
        private static Sprite MakeBlade(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size]; float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    // Arc along radius ~0.75 with thickness band
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float arc = Mathf.Max(0f, 1f - Mathf.Abs(r - 0.78f) * 14f);
                    // Cut to lower-half + diagonal arms
                    float ang = Mathf.Atan2(dy, dx);
                    float wedge1 = Mathf.Clamp01(1f - Mathf.Abs(ang - Mathf.PI * 0.25f) * 1.6f);
                    float wedge2 = Mathf.Clamp01(1f - Mathf.Abs(ang - Mathf.PI * 0.75f) * 1.6f);
                    float a = arc * Mathf.Max(wedge1, wedge2);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Void wisp: smoky tendril (vertical anisotropic gradient)
        private static Sprite MakeWisp(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size]; float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    // Stretch vertically
                    float sx = dx;
                    float sy = dy * 0.55f;
                    float r = Mathf.Sqrt(sx * sx + sy * sy);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
                    // Wavy edges
                    a *= 0.9f + 0.1f * Mathf.Sin(dy * 8f + dx * 5f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static float DistSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
