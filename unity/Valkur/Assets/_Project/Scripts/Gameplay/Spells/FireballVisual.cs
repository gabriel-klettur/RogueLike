using System.Reflection;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Epic procedural fireball visual.
    /// Renders a multi-layer flame (white-hot center + hot core + glow + outer halo)
    /// with flicker, motion stretch, ghost trail, ember emission, and a dynamic
    /// URP 2D point light. On impact, spawns a shockwave + flash + radial ember burst
    /// + light pulse + camera shake (handled by FireballImpactFX).
    ///
    /// All visuals are procedural (no sprite assets required) so it works without art
    /// dependencies. URP/Light2D usage is via reflection so the assembly stays decoupled
    /// from the URP runtime (same pattern as DayNightCycle / WorldLightLoader).
    /// </summary>
    public class FireballVisual : MonoBehaviour, IProjectileVisual
    {
        // ── Tuning ────────────────────────────────────────────────────
        private const float CoreScale       = 0.40f;
        private const float GlowScale       = 0.95f;
        private const float HaloScale       = 1.70f;
        private const float HotCoreScale    = 0.20f;
        private const int   GhostCount      = 5;
        private const float GhostSpacing    = 0.10f;
        private const float EmberInterval   = 0.018f;
        private const float EmberLifetime   = 0.45f;
        private const float LightOuterRadius = 2.6f;
        private const float LightInnerRadius = 0.4f;
        private const float LightIntensity   = 2.4f;

        // ── Layer renderers ───────────────────────────────────────────
        private SpriteRenderer _hotCoreSr;
        private SpriteRenderer _coreSr;
        private SpriteRenderer _glowSr;
        private SpriteRenderer _haloSr;
        private SpriteRenderer[] _ghostSrs;

        // ── Visual state ──────────────────────────────────────────────
        private readonly Color _hotColor   = new Color(1.0f, 0.95f, 0.75f, 1f);
        private readonly Color _coreColor  = new Color(1.0f, 0.85f, 0.30f, 1f);
        private readonly Color _glowColor  = new Color(1.0f, 0.40f, 0.05f, 0.65f);
        private readonly Color _haloColor  = new Color(1.0f, 0.20f, 0.02f, 0.22f);
        private float _seed;
        private Vector3 _lastPos;
        private float _emberTimer;
        private bool _impacted;
        private GameObject _light2DGo;
        private Component _light2DComponent;

        // ── Shared procedural assets ──────────────────────────────────
        private static Sprite _coreSprite;
        private static Sprite _glowSprite;
        private static Sprite _haloSprite;
        private static Sprite _hotCoreSprite;
        private static Sprite _emberSprite;
        private static Sprite _ringSprite;
        private static Material _unlitMaterial;

        // ── URP Light2D reflection (shared) ───────────────────────────
        private static System.Type _light2DType;
        private static PropertyInfo _l2dLightType;
        private static PropertyInfo _l2dColor;
        private static PropertyInfo _l2dIntensity;
        private static PropertyInfo _l2dOuter;
        private static PropertyInfo _l2dInner;
        private static PropertyInfo _l2dFalloff;
        private static bool _l2dResolved;

        // ── Public API ────────────────────────────────────────────────

        /// <summary>Spawn the epic impact FX at the given world position.</summary>
        public void OnImpact(Vector3 worldPos)
        {
            if (_impacted) return;
            _impacted = true;
            FireballImpactFX.Spawn(worldPos, _coreColor);
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            EnsureSharedAssets();
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

        private void Update()
        {
            float t = Time.time + _seed;

            // Multi-octave flicker per layer
            float flickerHot  = 1f + 0.18f * Mathf.Sin(t * 33f) + 0.10f * Mathf.Sin(t * 55f + 1.2f);
            float flickerCore = 1f + 0.14f * Mathf.Sin(t * 22f + 0.4f) + 0.08f * Mathf.Sin(t * 37f + 1.7f);
            float flickerGlow = 1f + 0.20f * Mathf.Sin(t * 14f + 0.4f) + 0.10f * Mathf.Sin(t * 26f);
            float flickerHalo = 1f + 0.30f * Mathf.Sin(t *  9f + 1.1f);

            // Motion stretch: scale flame along travel axis when projectile moves.
            // Projectile rotates the root so +X (transform.right) is travel direction.
            Vector3 pos = transform.position;
            Vector3 delta = pos - _lastPos;
            float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            float speedFactor = Mathf.Clamp01(speed / 12f);
            float stretchX = 1f + speedFactor * 0.55f;
            float stretchY = 1f - speedFactor * 0.18f;

            if (_haloSr != null)
                _haloSr.transform.localScale = new Vector3(HaloScale * stretchX * flickerHalo,
                                                           HaloScale * stretchY * flickerHalo, 1f);
            if (_glowSr != null)
            {
                _glowSr.transform.localScale = new Vector3(GlowScale * stretchX * flickerGlow,
                                                           GlowScale * stretchY * flickerGlow, 1f);
                _glowSr.color = new Color(_glowColor.r, _glowColor.g, _glowColor.b,
                    _glowColor.a * (0.85f + 0.15f * Mathf.Sin(t * 18f)));
            }
            if (_coreSr != null)
                _coreSr.transform.localScale = new Vector3(CoreScale * stretchX * flickerCore,
                                                           CoreScale * stretchY * flickerCore, 1f);
            if (_hotCoreSr != null)
                _hotCoreSr.transform.localScale = Vector3.one * HotCoreScale * flickerHot;

            // Ghost trail behind the core (local -X axis = behind, since rotation aligns +X to direction)
            if (_ghostSrs != null && _ghostSrs.Length > 0)
            {
                for (int i = 0; i < _ghostSrs.Length; i++)
                {
                    var g = _ghostSrs[i];
                    if (g == null) continue;
                    float u = (i + 1) / (float)(_ghostSrs.Length + 1);
                    g.transform.localPosition = new Vector3(-(GhostSpacing * (i + 1)), 0f, 0f);
                    float gAlpha = (1f - u) * 0.55f * speedFactor;
                    float gScale = (1f - u * 0.5f) * GlowScale * (0.9f + 0.1f * Mathf.Sin(t * 20f + i));
                    g.transform.localScale = Vector3.one * gScale;
                    g.color = new Color(_glowColor.r, _glowColor.g, _glowColor.b, gAlpha);
                }
            }

            // Continuous ember emission while moving
            _emberTimer -= Time.deltaTime;
            if (!_impacted && delta.sqrMagnitude > 0.0001f && _emberTimer <= 0f)
            {
                _emberTimer = EmberInterval;
                SpawnEmber();
            }

            // Light2D flicker
            if (_light2DComponent != null && _l2dIntensity != null)
            {
                try
                {
                    float intensity = LightIntensity * (0.85f + 0.15f * Mathf.Sin(t * 24f) + 0.10f * Mathf.Sin(t * 13f));
                    _l2dIntensity.SetValue(_light2DComponent, intensity);
                }
                catch { /* reflection safety */ }
            }

            _lastPos = pos;
        }

        private void OnDisable()
        {
            // Pool-safe cleanup of the dynamic light when projectile is despawned.
            if (_light2DGo != null)
            {
                Destroy(_light2DGo);
                _light2DGo = null;
                _light2DComponent = null;
            }
        }

        // ── Build ─────────────────────────────────────────────────────

        private void BuildVisual()
        {
            int order = SortingConfig.Z_SKY;

            _haloSr   = CreateChild("Halo",    _haloSprite,    _haloColor,  HaloScale,    order + 2);
            _glowSr   = CreateChild("Glow",    _glowSprite,    _glowColor,  GlowScale,    order + 3);
            _coreSr   = CreateChild("Core",    _coreSprite,    _coreColor,  CoreScale,    order + 5);
            _hotCoreSr = CreateChild("HotCore", _hotCoreSprite, _hotColor,  HotCoreScale, order + 6);

            _ghostSrs = new SpriteRenderer[GhostCount];
            for (int i = 0; i < GhostCount; i++)
                _ghostSrs[i] = CreateChild($"Ghost{i}", _glowSprite, _glowColor, GlowScale, order + 1);

            // Hide the placeholder root SpriteRenderer (added by ProjectilePrefabFactory).
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
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = order;
            sr.material = _unlitMaterial;
            return sr;
        }

        private void CreateDynamicLight()
        {
            if (_light2DType == null) return;

            _light2DGo = new GameObject("FireballLight");
            _light2DGo.transform.SetParent(transform, false);
            _light2DGo.transform.localPosition = Vector3.zero;

            try
            {
                _light2DComponent = _light2DGo.AddComponent(_light2DType);
                if (_l2dLightType != null)
                {
                    var enumType = _l2dLightType.PropertyType;
                    _l2dLightType.SetValue(_light2DComponent, System.Enum.ToObject(enumType, 2)); // 2 = Point
                }
                if (_l2dColor != null)     _l2dColor.SetValue(_light2DComponent, new Color(1f, 0.55f, 0.15f, 1f));
                if (_l2dIntensity != null) _l2dIntensity.SetValue(_light2DComponent, LightIntensity);
                if (_l2dOuter != null)     _l2dOuter.SetValue(_light2DComponent, LightOuterRadius);
                if (_l2dInner != null)     _l2dInner.SetValue(_light2DComponent, LightInnerRadius);
                if (_l2dFalloff != null)   _l2dFalloff.SetValue(_light2DComponent, 0.9f);
            }
            catch
            {
                if (_light2DGo != null) Destroy(_light2DGo);
                _light2DGo = null;
                _light2DComponent = null;
            }
        }

        private void SpawnEmber()
        {
            var go = new GameObject("Ember");
            go.transform.position = transform.position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _emberSprite;
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = SortingConfig.Z_SKY + 4;
            sr.material = _unlitMaterial;

            float h = Random.value;
            sr.color = Color.Lerp(new Color(1f, 0.95f, 0.55f, 1f),
                                  new Color(1f, 0.40f, 0.10f, 1f), h);

            // Velocity: backward (-X local) plus jitter
            Vector2 back = -(Vector2)transform.right;
            Vector2 jitter = Random.insideUnitCircle * 1.2f;
            Vector2 vel = back * Random.Range(0.5f, 1.6f) + jitter;
            go.AddComponent<FireballEmber>().Init(vel, EmberLifetime, Random.Range(0.06f, 0.14f));
        }

        // ── Procedural sprite generation ──────────────────────────────

        private static void EnsureSharedAssets()
        {
            if (_coreSprite == null)    _coreSprite    = MakeRadial(48, CorePixel);
            if (_glowSprite == null)    _glowSprite    = MakeRadial(96, GlowPixel);
            if (_haloSprite == null)    _haloSprite    = MakeRadial(128, HaloPixel);
            if (_hotCoreSprite == null) _hotCoreSprite = MakeRadial(32, HotCorePixel);
            if (_emberSprite == null)   _emberSprite   = MakeRadial(16, EmberPixel);
            if (_ringSprite == null)    _ringSprite    = MakeRadial(128, RingPixel);

            if (_unlitMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                         ?? Shader.Find("Sprites/Default");
                _unlitMaterial = new Material(sh);
            }
        }

        internal static Material SharedUnlitMaterial { get { EnsureSharedAssets(); return _unlitMaterial; } }
        internal static Sprite SharedGlowSprite     { get { EnsureSharedAssets(); return _glowSprite; } }
        internal static Sprite SharedRingSprite     { get { EnsureSharedAssets(); return _ringSprite; } }
        internal static Sprite SharedEmberSprite    { get { EnsureSharedAssets(); return _emberSprite; } }
        internal static Sprite SharedHotCoreSprite  { get { EnsureSharedAssets(); return _hotCoreSprite; } }

        private static Sprite MakeRadial(int size, System.Func<float, Color> fn)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * size + x] = fn(d);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Color CorePixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a = Mathf.Pow(1f - d, 1.6f);
            float white = Mathf.Pow(1f - d, 0.6f);
            return new Color(1f, Mathf.Lerp(0.55f, 1f, white), Mathf.Lerp(0.10f, 0.85f, white), a);
        }

        private static Color GlowPixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a = Mathf.Pow(1f - d, 2.4f) * 0.85f;
            return new Color(1f, 0.42f, 0.06f, a);
        }

        private static Color HaloPixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a = Mathf.Pow(1f - d, 3.2f) * 0.55f;
            return new Color(1f, 0.22f, 0.03f, a);
        }

        private static Color HotCorePixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a = Mathf.Pow(1f - d, 1.1f);
            float white = Mathf.Pow(1f - d, 0.4f);
            return new Color(1f, Mathf.Lerp(0.85f, 1f, white), Mathf.Lerp(0.55f, 1f, white), a);
        }

        private static Color EmberPixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a = Mathf.Pow(1f - d, 1.8f);
            return new Color(1f, 0.7f, 0.25f, a);
        }

        private static Color RingPixel(float d)
        {
            if (d > 1f) return Color.clear;
            float ringPos = 0.78f;
            float thickness = 0.18f;
            float diff = Mathf.Abs(d - ringPos);
            float a = Mathf.Clamp01(1f - diff / thickness);
            a = Mathf.Pow(a, 1.6f);
            return new Color(1f, 0.55f, 0.15f, a);
        }

        // ── Light2D reflection ────────────────────────────────────────

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

        // Expose Light2D reflection to FireballImpactFX without duplicating the lookup.
        internal static System.Type GetLight2DType()                  { ResolveLight2D(); return _light2DType; }
        internal static PropertyInfo GetLight2DLightTypeProp()        { ResolveLight2D(); return _l2dLightType; }
        internal static PropertyInfo GetLight2DColorProp()            { ResolveLight2D(); return _l2dColor; }
        internal static PropertyInfo GetLight2DIntensityProp()        { ResolveLight2D(); return _l2dIntensity; }
        internal static PropertyInfo GetLight2DOuterProp()            { ResolveLight2D(); return _l2dOuter; }
        internal static PropertyInfo GetLight2DInnerProp()            { ResolveLight2D(); return _l2dInner; }
        internal static PropertyInfo GetLight2DFalloffProp()          { ResolveLight2D(); return _l2dFalloff; }
    }

    /// <summary>
    /// Trailing ember: drifts with simple kinematics (drag + heat-rise buoyancy),
    /// fades and shrinks, then self-destructs.
    /// </summary>
    internal class FireballEmber : MonoBehaviour
    {
        private Vector2 _vel;
        private float _life;
        private float _age;
        private float _scale;
        private SpriteRenderer _sr;

        public void Init(Vector2 velocity, float lifetime, float scale)
        {
            _vel = velocity;
            _life = Mathf.Max(0.05f, lifetime);
            _scale = scale;
            transform.localScale = Vector3.one * _scale;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            float t = _age / _life;
            if (t >= 1f) { Destroy(gameObject); return; }

            _vel *= 1f - 2.5f * dt;
            _vel.y += 1.6f * dt; // heat rises
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
}
