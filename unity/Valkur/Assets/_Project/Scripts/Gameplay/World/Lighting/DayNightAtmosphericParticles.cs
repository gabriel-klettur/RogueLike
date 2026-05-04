using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Camera-following <see cref="ParticleSystem"/> that emits a wide cloud of
    /// drifting dust particles whose color and density change with the live
    /// <see cref="DayNightCycle"/> phase:
    ///
    ///   • Dawn  — golden motes, high density, drift slowly upward (pollen-y).
    ///   • Day   — pale near-white dust, very low density (barely visible).
    ///   • Dusk  — warm copper / orange embers, medium density, drifting down.
    ///   • Night — soft cool-blue mist, medium density, gentle drift.
    ///
    /// Spawned and parented under <c>[VFX]</c> by <see cref="GameplaySceneSetup"/>.
    /// Particles render on the <c>VFX</c> sorting layer so they sit above the
    /// world but below screen-space HUD overlays.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class DayNightAtmosphericParticles : MonoBehaviour
    {
        // ── Tunables ─────────────────────────────────────────────────────────
        [SerializeField, Tooltip("World-units side length of the camera-anchored emission box. " +
                                  "Should comfortably cover the orthographic frustum at typical zoom.")]
        private float _emissionBoxSide = 30f;

        [SerializeField, Tooltip("Smoothing factor (Hz) for fading between phase configurations.")]
        private float _phaseLerpSpeed = 0.6f;

        // ── Phase configs ────────────────────────────────────────────────────
        // Each phase has rate / start-color / vertical drift baked in. Tweaked
        // to read as "ambient atmosphere" without distracting from combat.
        private struct PhaseConfig
        {
            public float emissionRate;        // particles per second
            public Color startColor;          // RGBA at spawn
            public float verticalVelocity;    // world units per second
            public float startSize;           // world units
        }

        private static readonly PhaseConfig CFG_DAY   = new PhaseConfig
        { emissionRate = 4f,  startColor = new Color(1.00f, 0.98f, 0.92f, 0.20f), verticalVelocity = -0.05f, startSize = 0.06f };
        private static readonly PhaseConfig CFG_DAWN  = new PhaseConfig
        { emissionRate = 18f, startColor = new Color(1.00f, 0.78f, 0.40f, 0.55f), verticalVelocity =  0.15f, startSize = 0.10f };
        private static readonly PhaseConfig CFG_DUSK  = new PhaseConfig
        { emissionRate = 14f, startColor = new Color(1.00f, 0.55f, 0.28f, 0.50f), verticalVelocity = -0.10f, startSize = 0.10f };
        private static readonly PhaseConfig CFG_NIGHT = new PhaseConfig
        { emissionRate = 10f, startColor = new Color(0.55f, 0.70f, 1.00f, 0.45f), verticalVelocity =  0.08f, startSize = 0.08f };

        // ── State ────────────────────────────────────────────────────────────
        private ParticleSystem _ps;
        private ParticleSystem.MainModule _main;
        private ParticleSystem.EmissionModule _emission;
        private ParticleSystem.ShapeModule _shape;
        private ParticleSystem.VelocityOverLifetimeModule _velocity;
        private Camera _trackedCamera;

        private PhaseConfig _activeConfig = CFG_DAY;
        private PhaseConfig _targetConfig = CFG_DAY;

        // Sprite generated once per session — a soft white circle that the
        // particle's start color tints into golden motes / blue mist / etc.
        private static Sprite _particleSprite;
        private static Material _particleMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _particleSprite   = null;
            _particleMaterial = null;
        }

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            ConfigureParticleSystem();
        }

        private void Start()
        {
            // Snap to the current cycle phase so the editor entering Play
            // doesn't emit a one-frame burst of the wrong color.
            if (DayNightCycle.Instance != null)
                _activeConfig = _targetConfig = ConfigFor(DayNightCycle.Instance.CurrentPhase);
            ApplyConfigImmediate(_activeConfig);
        }

        private void Update()
        {
            // Follow the active camera so particles always cover the visible
            // viewport. Re-resolved lazily — Camera.main can change after a
            // scene transition or vcam swap.
            if (_trackedCamera == null) _trackedCamera = Camera.main;
            if (_trackedCamera != null)
            {
                Vector3 p = _trackedCamera.transform.position;
                p.z = transform.position.z;
                transform.position = p;
            }

            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;

            // Honor the master "no filters" toggle: when lighting is off, fade
            // emission to zero so the world reads completely clean. We still
            // crossfade smoothly rather than hard-cut so toggling the switch
            // doesn't pop a cloud of particles in/out instantly.
            _targetConfig = cycle.LightingEnabled
                ? ConfigFor(cycle.CurrentPhase)
                : ConfigDisabled();

            // Smooth crossfade — we lerp the four scalar/color fields in
            // _activeConfig toward _targetConfig and re-apply each frame.
            float a = 1f - Mathf.Exp(-_phaseLerpSpeed * Time.deltaTime);
            _activeConfig.emissionRate     = Mathf.Lerp(_activeConfig.emissionRate,     _targetConfig.emissionRate,     a);
            _activeConfig.startColor       = Color.Lerp(_activeConfig.startColor,       _targetConfig.startColor,       a);
            _activeConfig.verticalVelocity = Mathf.Lerp(_activeConfig.verticalVelocity, _targetConfig.verticalVelocity, a);
            _activeConfig.startSize        = Mathf.Lerp(_activeConfig.startSize,        _targetConfig.startSize,        a);

            ApplyConfigLive(_activeConfig);
        }

        // Used when LightingEnabled is OFF: zero emission rate so no new
        // particles spawn; remaining live particles fade out naturally over
        // their lifetime.
        private static PhaseConfig ConfigDisabled() => new PhaseConfig
        {
            emissionRate     = 0f,
            startColor       = new Color(1f, 1f, 1f, 0f),
            verticalVelocity = 0f,
            startSize        = 0.06f,
        };

        private void ConfigureParticleSystem()
        {
            _main     = _ps.main;
            _emission = _ps.emission;
            _shape    = _ps.shape;
            _velocity = _ps.velocityOverLifetime;

            _main.startLifetime          = 6f;
            _main.startSpeed             = 0.05f;
            _main.simulationSpace        = ParticleSystemSimulationSpace.World;
            _main.maxParticles           = 256;
            _main.gravityModifier        = 0f;
            _main.scalingMode            = ParticleSystemScalingMode.Hierarchy;
            _main.startRotation          = 0f;

            _shape.enabled               = true;
            _shape.shapeType             = ParticleSystemShapeType.Box;
            _shape.scale                 = new Vector3(_emissionBoxSide, _emissionBoxSide, 0.1f);
            _shape.position              = Vector3.zero;

            _velocity.enabled            = true;
            _velocity.space              = ParticleSystemSimulationSpace.World;
            _velocity.x                  = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            _velocity.z                  = new ParticleSystem.MinMaxCurve(0f, 0f);

            // Renderer: soft additive billboards using a procedural circle sprite.
            var renderer = _ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode      = ParticleSystemRenderMode.Billboard;
            renderer.material        = ResolveParticleMaterial();
            renderer.sortingLayerName = SortingLayerExists("VFX") ? "VFX" : "Default";
            renderer.sortingOrder    = 5;

            // Position behind the gameplay plane so particles sit between the
            // tilemap and the player without writing to the wrong sorting bucket.
            transform.position = new Vector3(0f, 0f, -1f);
        }

        private static bool SortingLayerExists(string name)
        {
            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == name) return true;
            return false;
        }

        private static Material ResolveParticleMaterial()
        {
            if (_particleMaterial != null) return _particleMaterial;
            // Built-in particles additive shader keeps the dust glowy and
            // composites cleanly over both bright and dark backgrounds.
            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _particleMaterial = new Material(shader);
            _particleMaterial.mainTexture = ParticleTexture();
            // Additive blending if the shader supports the property.
            if (_particleMaterial.HasProperty("_Mode")) _particleMaterial.SetFloat("_Mode", 4f); // additive
            if (_particleMaterial.HasProperty("_BlendOp")) _particleMaterial.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
            if (_particleMaterial.HasProperty("_SrcBlend")) _particleMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_particleMaterial.HasProperty("_DstBlend")) _particleMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            return _particleMaterial;
        }

        private static Texture2D ParticleTexture()
        {
            if (_particleSprite != null && _particleSprite.texture != null) return _particleSprite.texture;
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px  = new Color32[N * N];
            float r = N * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                // Soft falloff — center fully opaque, edges fade to zero.
                float a  = Mathf.Clamp01(1f - d / r);
                a = a * a; // squared for a softer halo
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            _particleSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return tex;
        }

        private void ApplyConfigImmediate(PhaseConfig cfg) => ApplyConfigLive(cfg);

        private void ApplyConfigLive(PhaseConfig cfg)
        {
            _emission.rateOverTime = cfg.emissionRate;
            _main.startColor       = cfg.startColor;
            _main.startSize        = cfg.startSize;
            _velocity.y            = new ParticleSystem.MinMaxCurve(cfg.verticalVelocity * 0.7f,
                                                                    cfg.verticalVelocity * 1.3f);
            _shape.scale           = new Vector3(_emissionBoxSide, _emissionBoxSide, 0.1f);
        }

        private static PhaseConfig ConfigFor(DayNightCycle.DayPhase phase) => phase switch
        {
            DayNightCycle.DayPhase.Dawn  => CFG_DAWN,
            DayNightCycle.DayPhase.Dusk  => CFG_DUSK,
            DayNightCycle.DayPhase.Night => CFG_NIGHT,
            _                             => CFG_DAY,
        };
    }
}
