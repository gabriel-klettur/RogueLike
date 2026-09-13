using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World.Sky;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.World.Ambience
{
    /// <summary>
    /// Fireflies: a few dozen slow blinking motes over the night's ground, following the
    /// camera, gone at the first light and in the rain.
    ///
    /// One code-built <see cref="ParticleSystem"/> in world space, the shape the weather
    /// layers use, so the field costs one emitter however many fireflies are out. Additive
    /// and drawn on the VFX layer, which the ambient light leaves alone: a firefly is a
    /// light source, and at night it is the one thing that is allowed to be at full white —
    /// which is exactly what the bloom's night threshold exists to catch.
    ///
    /// The blink is a colour-over-lifetime curve rather than a random size: a real firefly
    /// pulses on a rhythm, and with a dozen of them on different phases the field breathes
    /// rather than flickers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FireflyField : MonoBehaviour
    {
        /// <summary>Fireflies born per second per screen at full density.</summary>
        public const float RatePerScreen = 2.6f;

        /// <summary>Daylight above which there are none. Sunset's last minutes still count as dusk.</summary>
        public const float MaxDaylight = 0.06f;

        /// <summary>Precipitation (0..1) at which the field is fully suppressed.</summary>
        public const float RainCutoff = 0.35f;

        private ParticleSystem _system;
        private ParticleSystemRenderer _renderer;
        private Camera _camera;
        private bool _built;

        /// <summary>The live emission density, 0..1. Test seam.</summary>
        public float Density { get; private set; }

        /// <summary>The particle system, for the fixture.</summary>
        public ParticleSystem System => _system;

        private void Awake() => EnsureBuilt();

        /// <summary>
        /// How many fireflies for the hour and the weather, 0..1. Pure.
        /// </summary>
        public static float DensityFor(float daylight01, float precipitation01, bool indoors, bool enabled)
        {
            if (!enabled || indoors) return 0f;
            if (daylight01 > MaxDaylight) return 0f;
            float rain = Mathf.Clamp01(precipitation01 / RainCutoff);
            return 1f - rain;
        }

        /// <summary>Build the emitter. Public for Edit Mode, where Awake never runs.</summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _system = GetComponent<ParticleSystem>();
            if (_system == null) _system = gameObject.AddComponent<ParticleSystem>();
            // AddComponent starts the system immediately, and a playing system refuses a new
            // duration — configure it stopped, then play.
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _system.main;
            main.loop            = true;
            main.duration        = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed      = 0f;
            main.startSize       = new ParticleSystem.MinMaxCurve(0.09f, 0.15f);
            main.startColor      = new Color(0.82f, 1.0f, 0.42f, 1f);
            main.maxParticles    = 80;
            main.playOnAwake     = false;

            var emission = _system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = _system.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale     = new Vector3(20f, 10f, 0.1f);

            // The wander: a slow, low-frequency noise so a firefly drifts and turns rather
            // than vibrating in place.
            var noise = _system.noise;
            noise.enabled      = true;
            noise.separateAxes = false;
            noise.strength     = 0.32f;
            noise.frequency    = 0.28f;
            noise.scrollSpeed  = 0.18f;
            noise.damping      = true;

            // The blink. Eight keys is the gradient's ceiling; three pulses in a life of
            // five to eight seconds, each one off different frames from its neighbours
            // because each firefly's life starts at its own moment.
            var col = _system.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f,    0.00f), new GradientAlphaKey(1f,    0.12f),
                    new GradientAlphaKey(0.05f, 0.30f), new GradientAlphaKey(1f,    0.45f),
                    new GradientAlphaKey(0.05f, 0.62f), new GradientAlphaKey(1f,    0.78f),
                    new GradientAlphaKey(0.05f, 0.90f), new GradientAlphaKey(0f,    1.00f),
                });
            col.color = g;

            _renderer = GetComponent<ParticleSystemRenderer>();
            _renderer.renderMode       = ParticleSystemRenderMode.Billboard;
            _renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            _renderer.sortingOrder     = 10;
            _renderer.sharedMaterial   = ParticleMaterialCache.Get(WeatherTextures.Dot(16, 0.55f), additive: true);

            _system.Play();
        }

        private void Update()
        {
            float precipitation = 0f;
            bool  indoors = false;
            var wm = WeatherManager.Instance;
            if (wm != null)
            {
                precipitation = Mathf.Max(wm.DensityOf(WeatherType.Rain), wm.DensityOf(WeatherType.Snow));
                indoors = wm.IsIndoors;
            }
            Tick(SunShadowState.Daylight, precipitation, indoors);
        }

        /// <summary>Set the field for this frame's conditions. Public so a test can drive it.</summary>
        public void Tick(float daylight01, float precipitation01, bool indoors)
        {
            if (_system == null) return;
            Density = DensityFor(daylight01, precipitation01, indoors, WorldLookSettings.Fireflies);
            var emission = _system.emission;
            emission.rateOverTime = Density * RatePerScreen;
        }

        private void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;
            float h = _camera.orthographicSize * 2f * 1.2f;
            float w = h * _camera.aspect;
            var p = _camera.transform.position;
            transform.position = new Vector3(p.x, p.y, 0f);
            var shape = _system.shape;
            shape.scale = new Vector3(w, h, 0.1f);
        }
    }
}
