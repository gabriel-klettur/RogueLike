using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Epic procedural fireball visual — orchestrator partial.
    ///
    /// Renders a multi-layer flame (white-hot center + hot core + glow + outer halo)
    /// with flicker, motion stretch, ghost/trail, ember emission, dynamic URP 2D
    /// point light, a continuous "ball of particles" core PS, orbiting sparks PS,
    /// and a TrailRenderer fire trail. On impact, spawns FireballImpactFX.
    ///
    /// All visuals are procedural (no sprite assets required). URP/Light2D usage is
    /// via reflection so the assembly stays decoupled from the URP runtime.
    ///
    /// Pooling contract (Domain Reload OFF):
    ///   • Awake  — create all child GameObjects / Components once per pool slot.
    ///   • OnEnable  — reset state, clear PS / trail for the new shot.
    ///   • OnDisable — stop PS, clear trail, destroy transient light.
    /// </summary>
    public partial class FireballVisual : MonoBehaviour, IProjectileVisual
    {
        // ── Tuning ────────────────────────────────────────────────────
        private const float CoreScale       = 0.55f;   // bumped from 0.40 — orb must dominate
        private const float GlowScale       = 0.95f;
        private const float HaloScale       = 1.70f;
        private const float HotCoreScale    = 0.32f;   // bumped from 0.20 — white-hot center more prominent
        private const int   GhostCount      = 5;
        private const float GhostSpacing    = 0.10f;
        private const float EmberInterval   = 0.018f;
        private const float EmberLifetime   = 0.45f;
        private const float LightOuterRadius = 2.6f;
        private const float LightInnerRadius = 0.4f;
        private const float LightIntensity   = 2.4f;

        // Core particle shimmer (now Local-space, packed inside the orb — gives texture
        // to the ball without spraying particles outward).
        private const float CoreParticleEmitRate    = 80f;
        private const float CoreParticleLifetimeMin = 0.10f;
        private const float CoreParticleLifetimeMax = 0.20f;

        // Orbiting sparks (clearly separate from core, fast tangential rotation).
        private const float SparkOrbitEmitRate      = 8f;
        private const float SparkOrbitRadiusMul     = 1.2f;   // × core radius
        private const float SparkOrbitalSpeedMin    = 3.0f;   // rad/s — was 1.5
        private const float SparkOrbitalSpeedMax    = 5.0f;   // rad/s — was 3.0

        // Trail — narrower so it doesn't eclipse the orb.
        private const float TrailTime               = 0.30f;
        private const float TrailStartWidthMul      = 0.25f;  // × GlowScale — was 0.5

        // ── Inspector toggles ─────────────────────────────────────────
        [SerializeField]
        [Tooltip("When true the legacy ghost-sprite trail runs instead of TrailRenderer. Disable to use the new fire trail.")]
        private bool _useLegacyGhostTrail = false;

        [SerializeField]
        [Tooltip("When true the TrailRenderer fire trail is active (ignored if useLegacyGhostTrail is true).")]
        private bool _useNewTrail = true;

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
            BuildCoreParticles();
            BuildOrbitingSparks();
            BuildTrail();
            _seed = Random.Range(0f, 100f);
        }

        private void OnEnable()
        {
            _impacted   = false;
            _emberTimer = 0f;
            _lastPos    = transform.position;
            ResetParticlesOnEnable();
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

            // Ghost trail (legacy path — disabled by default; kept for designer fallback)
            if (_useLegacyGhostTrail && _ghostSrs != null && _ghostSrs.Length > 0)
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
            TickLightFlicker(t);

            // Core-particle inherit-velocity hook
            TickCoreParticleVelocity(delta);

            _lastPos = pos;
        }

        private void OnDisable()
        {
            StopParticlesOnDisable();

            // Pool-safe cleanup of the dynamic light when projectile is despawned.
            if (_light2DGo != null)
            {
                Destroy(_light2DGo);
                _light2DGo = null;
                _light2DComponent = null;
            }
        }

        // ── Build (sprites + light) ───────────────────────────────────

        private void BuildVisual()
        {
            int order = SortingConfig.Z_SKY;

            _haloSr    = CreateChild("Halo",    SharedHaloSprite,    _haloColor,  HaloScale,    order + 2);
            _glowSr    = CreateChild("Glow",    SharedGlowSprite,    _glowColor,  GlowScale,    order + 3);
            _coreSr    = CreateChild("Core",    SharedCoreSprite,    _coreColor,  CoreScale,    order + 5);
            _hotCoreSr = CreateChild("HotCore", SharedHotCoreSprite, _hotColor,   HotCoreScale, order + 6);

            _ghostSrs = new SpriteRenderer[GhostCount];
            for (int i = 0; i < GhostCount; i++)
            {
                _ghostSrs[i] = CreateChild($"Ghost{i}", SharedGlowSprite, _glowColor, GlowScale, order + 1);
                // Hidden by default; designer can re-enable via _useLegacyGhostTrail
                _ghostSrs[i].enabled = _useLegacyGhostTrail;
            }

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
            go.transform.localScale    = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite           = sprite;
            sr.color            = color;
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder     = order;
            sr.material         = SharedUnlitMaterial;
            return sr;
        }

        private void SpawnEmber()
        {
            var go = new GameObject("Ember");
            go.transform.position = transform.position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite           = SharedEmberSprite;
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder     = SortingConfig.Z_SKY + 4;
            sr.material         = SharedUnlitMaterial;

            float h = Random.value;
            sr.color = Color.Lerp(new Color(1f, 0.95f, 0.55f, 1f),
                                  new Color(1f, 0.40f, 0.10f, 1f), h);

            Vector2 back   = -(Vector2)transform.right;
            Vector2 jitter = Random.insideUnitCircle * 1.2f;
            Vector2 vel    = back * Random.Range(0.5f, 1.6f) + jitter;
            go.AddComponent<FireballEmber>().Init(vel, EmberLifetime, Random.Range(0.06f, 0.14f));
        }
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
            _vel   = velocity;
            _life  = Mathf.Max(0.05f, lifetime);
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

            _vel   *= 1f - 2.5f * dt;
            _vel.y += 1.6f * dt; // heat rises
            transform.position += (Vector3)(_vel * dt);

            float scaleT = 1f - t * 0.6f;
            transform.localScale = Vector3.one * _scale * scaleT;
            if (_sr != null)
            {
                var c = _sr.color;
                c.a    = (1f - t) * (1f - t);
                _sr.color = c;
            }
        }
    }
}
