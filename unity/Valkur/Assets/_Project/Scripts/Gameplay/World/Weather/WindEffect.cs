using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Wind: four depth slices of airborne debris blowing across the frame, and — more
    /// importantly — the thing that raises <see cref="WeatherWind.WeatherSpeed"/>, which rain
    /// and snow read every frame. Turning the wind on now bends the rain.
    ///
    /// The old effect emitted at a flat rate with a flat velocity, which is the one thing wind
    /// never does. Everything here is driven by the shared gust envelope: the emission rate,
    /// the blow speed, the streak length, and the audio bed's level and pitch all surge and
    /// fall together, several seconds at a time. A gust the player can HEAR arriving before
    /// the dust reaches them is most of what sells it.
    ///
    /// The slices are a dust haze, mid streaks, a sparse near layer of large soft blurs, and
    /// tumbling leaves — the only layer with its own colour, because it is the only one that
    /// is an object rather than air.
    /// </summary>
    public sealed class WindEffect : WeatherEffect
    {
        /// <summary>
        /// World units/second this effect contributes to the shared field at full density,
        /// before the gust envelope. Roughly a fifth of rain's fall speed, which is what makes
        /// a storm's rain slant hard without going horizontal.
        /// </summary>
        private const float MaxWeatherSpeed = 9f;

        // No per-slice fields: unlike rain and snow, every wind slice is laid out and pushed
        // identically, so the layout and the gust both walk `Layers` and nothing here needs a
        // handle to an individual one.
        private float _appliedSpeed = float.NaN;
        private float _appliedDirection;

        protected override WeatherIntensity DefaultIntensity => WeatherIntensity.Medium;

        protected override AudioClip ResolveAudioClip() => WeatherAudio.Wind();

        // ── build ────────────────────────────────────────────────────────────────────

        protected override void BuildLayers()
        {
            BuildBlownLayer("Wind_Dust", depth: 0.30f, order: 4,
                            texture: WeatherTextures.Dot(8, falloff: 1.25f),
                            stretch: false, lengthScale: 0f,
                            sizeMin: 0.030f, sizeMax: 0.065f,
                            color: new Color(0.92f, 0.88f, 0.76f, 0.26f),
                            rate: 110f, windFactor: 0.80f, maxParticles: 420,
                            ambientResponse: 1.0f, spread: 0.55f);

            BuildBlownLayer("Wind_Streak", depth: 0.60f, order: 6,
                            texture: WeatherTextures.Streak(32, 3, coreBias: 1.4f),
                            stretch: true, lengthScale: 5f,
                            sizeMin: 0.10f, sizeMax: 0.20f,
                            color: new Color(0.96f, 0.93f, 0.83f, 0.20f),
                            rate: 42f, windFactor: 1.00f, maxParticles: 220,
                            ambientResponse: 1.0f, spread: 0.85f);

            BuildBlownLayer("Wind_Near", depth: 1.00f, order: 11,
                            texture: WeatherTextures.Streak(48, 6, coreBias: 0.70f),
                            stretch: true, lengthScale: 7f,
                            sizeMin: 0.28f, sizeMax: 0.46f,
                            color: new Color(1.00f, 0.98f, 0.92f, 0.10f),
                            rate: 9f, windFactor: 1.45f, maxParticles: 80,
                            ambientResponse: 1.0f, spread: 1.1f);

            BuildLeaves();
        }

        private WeatherLayer BuildBlownLayer(string name, float depth, int order, Texture2D texture,
                                             bool stretch, float lengthScale,
                                             float sizeMin, float sizeMax, Color color, float rate,
                                             float windFactor, int maxParticles,
                                             float ambientResponse, float spread)
        {
            var layer = CreateLayer(name, depth);
            layer.BaseRate        = rate;
            layer.BaseColor       = color;
            layer.WindFactor      = windFactor;
            layer.AmbientResponse = ambientResponse;

            var main = layer.System.main;
            main.maxParticles  = maxParticles;
            main.startSize     = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startLifetime = 2.5f;   // replaced in LayoutForViewport

            var shape = layer.System.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            var velocity = layer.System.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            // Vertical spread widens with depth: air near the ground is turbulent, and a layer
            // whose particles all travel on exactly one horizontal line reads as a scan line.
            velocity.y = new ParticleSystem.MinMaxCurve(-spread, spread);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ApplyLifetimeFade(layer, fadeIn: 0.12f, fadeOut: 0.30f);

            var renderer = layer.Renderer;
            if (stretch)
            {
                renderer.renderMode    = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale   = lengthScale;
                // The gust is expressed as length as well as speed: a streak that only moves
                // faster reads as a frame-rate change, one that also draws longer reads as air.
                renderer.velocityScale = 0.055f;
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
            SetupRenderer(layer, texture, additive: false, sortingOrder: order);

            return layer;
        }

        /// <summary>
        /// The leaves. Sparse — a few on screen at a time — because debris is the detail the
        /// eye locks onto, and a constant stream of it reads as confetti rather than as wind.
        /// They are the only layer with a colour of their own, and the only one that spins.
        /// </summary>
        private WeatherLayer BuildLeaves()
        {
            var layer = CreateLayer("Wind_Leaf", depth: 0.75f);
            layer.BaseRate        = 3.2f;
            layer.BaseColor       = new Color(0.74f, 0.66f, 0.34f, 0.85f);
            layer.WindFactor      = 1.10f;
            layer.AmbientResponse = 0.95f;

            var main = layer.System.main;
            main.maxParticles  = 48;
            main.startSize     = new ParticleSystem.MinMaxCurve(0.16f, 0.30f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startLifetime = 3f;   // replaced in LayoutForViewport

            var shape = layer.System.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            var velocity = layer.System.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            velocity.y       = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            velocity.z       = new ParticleSystem.MinMaxCurve(0f, 0f);

            // A leaf carried by air does not travel in a line; it lifts, stalls and drops.
            var noise = layer.System.noise;
            noise.enabled      = true;
            noise.separateAxes = true;
            noise.strengthX    = 0.5f;
            noise.strengthY    = 1.4f;
            noise.strengthZ    = 0f;
            noise.frequency    = 0.5f;
            noise.scrollSpeed  = 1.1f;
            noise.quality      = ParticleSystemNoiseQuality.Medium;
            noise.damping      = false;

            var rot = layer.System.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-420f * Mathf.Deg2Rad, 420f * Mathf.Deg2Rad);

            ApplyLifetimeFade(layer, fadeIn: 0.08f, fadeOut: 0.18f);

            layer.Renderer.renderMode = ParticleSystemRenderMode.Billboard;
            SetupRenderer(layer, WeatherTextures.Leaf(24), additive: false, sortingOrder: 9);

            return layer;
        }

        // ── viewport ─────────────────────────────────────────────────────────────────

        protected override void LayoutForViewport(float halfW, float halfH)
        {
            float marginW = halfW + ViewportMargin;
            float marginH = halfH + ViewportMargin;
            float speed   = Mathf.Max(0.2f, WeatherWind.Speed);

            for (int i = 0; i < Layers.Count; i++)
                LayoutBlown(Layers[i], marginW, marginH, speed);
        }

        /// <summary>
        /// A tall thin slab just past the UPWIND edge, and a lifetime long enough to cross the
        /// frame. The upwind edge is derived from <see cref="WeatherWind.DirectionX"/> rather
        /// than hardcoded to the right side, so flipping the wind actually reverses the flow
        /// instead of leaving the emitter downwind of everything, blowing off the screen.
        /// </summary>
        private static void LayoutBlown(WeatherLayer layer, float marginW, float marginH, float speed)
        {
            if (layer == null) return;

            float sign = WeatherWind.DirectionX;

            var shape = layer.System.shape;
            shape.scale    = new Vector3(0.5f, marginH * 2f, 0.1f);
            shape.position = new Vector3(-sign * marginW, 0f, 0f);

            float travel = marginW * 2f;
            float life   = travel / Mathf.Max(0.2f, speed * layer.WindFactor);
            var main = layer.System.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.70f, life * 1.10f);
        }

        // ── gusts ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Emission surges with the gust. Below 1 at the trough on purpose: the lull between
        /// gusts has to be visibly emptier than the gust, or the surge has nothing to be a
        /// surge against.
        /// </summary>
        protected override float RateMultiplier(WeatherLayer layer)
            => Mathf.Lerp(0.35f, 1.85f, WeatherWind.Gust01);

        /// <inheritdoc/>
        protected override bool RateIsPerFrame => true;

        protected override float AudioPitch() => Mathf.Lerp(0.86f, 1.14f, WeatherWind.Gust01);

        protected override float AudioVolumeMultiplier() => Mathf.Lerp(0.45f, 1.15f, WeatherWind.Gust01);

        protected override void OnTick(float deltaTime)
        {
            // This effect IS the wind: raise the shared field by however hard it is currently
            // blowing, so rain and snow slant with it. Density rather than the raw level, so
            // the slant ramps in over the same fade the particles do.
            WeatherWind.WeatherSpeed = Density * MaxWeatherSpeed;

            float speed = WeatherWind.Speed;
            float vx    = WeatherWind.VelocityX;

            for (int i = 0; i < Layers.Count; i++)
                ApplyWindTo(Layers[i], vx);

            bool directionFlipped = !Mathf.Approximately(_appliedDirection, WeatherWind.DirectionX);
            bool speedMoved       = float.IsNaN(_appliedSpeed) || Mathf.Abs(speed - _appliedSpeed) > 0.6f;
            if (!directionFlipped && !speedMoved) return;

            _appliedSpeed     = speed;
            _appliedDirection = WeatherWind.DirectionX;
            LayoutForViewport(HalfWidth, HalfHeight);
        }

        /// <summary>
        /// Hand the shared field back when this effect stops existing. Without it a scene
        /// unload or a destroyed manager would leave rain permanently slanting at storm angle
        /// with nothing blowing.
        /// </summary>
        private void OnDisable() => WeatherWind.WeatherSpeed = 0f;
    }
}
