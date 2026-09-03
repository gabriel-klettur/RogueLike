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
    /// Fully procedural (no asset deps), URP via reflection, sortingLayerID forced
    /// everywhere.
    /// </summary>
    public class ElementalProjectileVisual : MonoBehaviour, IProjectileVisual
    {
        [SerializeField] private SpellElement element = SpellElement.Dark;
        [SerializeField] private bool playImpactAudio = true;

        /// <summary>
        /// Draw order inside <see cref="SortingConfig.LAYER_PROJECTILES"/>. These used to be
        /// offsets from <c>SortingConfig.Z_SKY</c> (600) applied on the ENTITIES layer — the
        /// same mistake <c>LightningBoltFX</c> made: Z_SKY is a Z depth, not a sorting order,
        /// and Entities sits below Decorations, WallsTop, ObjectsHigh, Projectiles and VFX, so
        /// a spell in flight drew UNDER every wall top and every other effect on screen.
        /// </summary>
        private const int OrderGhost = 0;
        private const int OrderHalo = 1;
        private const int OrderGlow = 2;
        private const int OrderAccent = 3;
        private const int OrderCore = 4;
        private const int OrderHotCore = 5;
        private const int OrderEmber = 3;

        /// <summary>Speed at which motion stretch and the ghost trail reach full strength.</summary>
        private const float StretchReferenceSpeed = 12f;

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

        /// <summary>
        /// Container for every layer that has to face the direction of travel. The rig's owner
        /// may spin its own transform — a boomerang does, two turns a second — and the trail,
        /// the motion stretch and the ember spray are all statements about the DIRECTION the
        /// projectile is going, not about how the blade is currently oriented. Parented to the
        /// root and given a world rotation each frame, so a spinning owner costs it nothing.
        /// </summary>
        private Transform _aura;
        private Vector3 _travelDir = Vector3.right;

        /// <summary>
        /// Set when the spawning executor authored a sprite for the projectile itself. The rig
        /// hides the root renderer by default (it is the prefab's placeholder), which silently
        /// made <c>SpellDefinition.sprite</c> a control that did nothing for every spell drawn
        /// by this rig.
        /// </summary>
        private bool _keepRootSprite;

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

        /// <summary>
        /// Keep the root <see cref="SpriteRenderer"/> visible: the spawner put an authored
        /// sprite on it and that sprite IS the projectile. Safe to call before or after the rig
        /// is built.
        /// </summary>
        public void KeepRootSprite()
        {
            _keepRootSprite = true;
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr != null) rootSr.enabled = true;
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
            float speedFactor = Mathf.Clamp01(speed / StretchReferenceSpeed);
            float stretchX = 1f + speedFactor * _palette.stretch;
            float stretchY = 1f - speedFactor * _palette.stretch * 0.32f;

            // Face the aura along travel. Everything that follows is expressed in ITS local
            // space — stretch on local X, the ghost trail at negative local X — so a rig whose
            // owner spins keeps trailing behind itself instead of whirling around itself.
            if (delta.sqrMagnitude > 1e-8f) _travelDir = delta.normalized;
            if (_aura != null)
                _aura.rotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(_travelDir.y, _travelDir.x) * Mathf.Rad2Deg);

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
            _aura = null;
            _light2DGo = null;
            _light2DComponent = null;
        }

        private void BuildVisual()
        {
            _palette = ElementPalette.For(element);

            var auraGo = new GameObject("Aura");
            auraGo.transform.SetParent(transform, false);
            auraGo.transform.localPosition = Vector3.zero;
            _aura = auraGo.transform;

            _haloSr    = CreateChild("Halo",    _palette.haloSprite,    _palette.halo,    _palette.haloScale,    OrderHalo);
            _glowSr    = CreateChild("Glow",    _palette.glowSprite,    _palette.glow,    _palette.glowScale,    OrderGlow);
            _coreSr    = CreateChild("Core",    _palette.coreSprite,    _palette.core,    _palette.coreScale,    OrderCore);
            _hotCoreSr = CreateChild("HotCore", _palette.hotCoreSprite, _palette.hotCore, _palette.hotCoreScale, OrderHotCore);

            // Element-specific accent (snowflake / bolt / blade / wisp). Alpha, not additive:
            // it is the only layer with a SHAPE, and the whole point of a blade or a snowflake
            // is its silhouette — on additive it dissolves into the glow behind it.
            if (_palette.accentSprite != null)
                _accentSr = CreateChild("Accent", _palette.accentSprite, _palette.accent,
                                        _palette.accentScale, OrderAccent,
                                        ElementalSprites.SharedUnlitMaterial);

            // Ghost trail
            int ghostCount = _palette.ghostCount;
            _ghostSrs = new SpriteRenderer[ghostCount];
            for (int i = 0; i < ghostCount; i++)
                _ghostSrs[i] = CreateChild($"Ghost{i}", _palette.glowSprite, _palette.glow, _palette.glowScale, OrderGhost);

            // Hide the prefab's placeholder sprite — unless the spawner authored a real one.
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr != null) rootSr.enabled = _keepRootSprite;

            CreateDynamicLight();
        }

        /// <summary>
        /// One layer of the rig. Additive by default: on the alpha material the brightest pixel
        /// a glow can produce is its own colour, so a stack meant to read as a hot centre inside
        /// a soft bloom could never blow out and a wide faint halo was a net luminance LOSS over
        /// pale ground. Pass <c>SharedUnlitMaterial</c> for a layer whose silhouette matters.
        /// </summary>
        private SpriteRenderer CreateChild(string name, Sprite sprite, Color color, float scale,
                                           int order, Material material = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_aura != null ? _aura : transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_PROJECTILES);
            sr.sortingLayerName = SortingConfig.LAYER_PROJECTILES;
            sr.sortingOrder = order;
            sr.sharedMaterial = material != null ? material : ElementalSprites.SharedAdditiveMaterial;
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
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_PROJECTILES);
            sr.sortingLayerName = SortingConfig.LAYER_PROJECTILES;
            sr.sortingOrder = OrderEmber;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.color = Color.Lerp(_palette.core, _palette.glow, Random.value);

            // Velocity: backward along TRAVEL + jitter (per-element drag/buoyancy). Reading the
            // root's own right vector instead would spray the trail in a circle on any rig
            // whose owner spins.
            Vector2 back = -(Vector2)_travelDir;
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
