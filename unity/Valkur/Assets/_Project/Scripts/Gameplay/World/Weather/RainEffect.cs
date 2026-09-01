using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Rain, as five depth slices rather than one sheet of drops.
    ///
    /// Three of them are falling water — a dim, small, slow FAR layer, the MID layer that
    /// carries the density, and a sparse NEAR layer of big soft streaks that read as
    /// out-of-focus drops close to the lens. Nothing about a single-system downpour tells the
    /// eye how far away the water is, which is why the old one looked like a decal on the
    /// camera; three sets of drops moving at three speeds is the whole trick.
    ///
    /// The other two are what rain does to a place rather than what it looks like in the air:
    /// SPLASH ripples landing across the ground (in a top-down game the ground is the entire
    /// visible area, so they spawn over the whole viewport, not along a line), and a very
    /// faint MIST haze drifting through it.
    ///
    /// The slant is not authored — it is <see cref="WeatherWind"/>, read every frame, with a
    /// per-layer factor so a gust shears the depth stack instead of sliding it sideways.
    /// Turning the wind on now visibly bends the rain, which is what "Wind + Rain = storm"
    /// was always supposed to mean.
    ///
    /// At Heavy the storm also gets lightning; that lives in <see cref="WeatherGrade"/>,
    /// because a flash has to reach the global light and the screen grade, not the particles.
    /// </summary>
    public sealed class RainEffect : WeatherEffect
    {
        /// <summary>Fall speed of each streak layer, in world units/second, at the layer's mid range.</summary>
        private const float FarFall  = 13f;
        private const float MidFall  = 19f;
        private const float NearFall = 27f;

        private WeatherLayer _far;
        private WeatherLayer _mid;
        private WeatherLayer _near;
        private WeatherLayer _splash;
        private WeatherLayer _mist;

        private float _appliedWind = float.NaN;

        protected override WeatherIntensity DefaultIntensity => WeatherIntensity.Medium;

        protected override AudioClip ResolveAudioClip() => WeatherAudio.Rain();

        // ── build ────────────────────────────────────────────────────────────────────

        protected override void BuildLayers()
        {
            _mist   = BuildMist();
            _far    = BuildStreakLayer("Rain_Far",  depth: 0.15f, order: 4,
                                       texW: 24, texH: 4, coreBias: 1.35f,
                                       size: 0.09f, lengthScale: 4.5f,
                                       color: new Color(0.60f, 0.70f, 0.90f, 0.30f),
                                       rate: 120f, fall: FarFall, windFactor: 0.55f);
            _splash = BuildSplash();
            _mid    = BuildStreakLayer("Rain_Mid",  depth: 0.50f, order: 7,
                                       texW: 32, texH: 5, coreBias: 1.00f,
                                       size: 0.15f, lengthScale: 6f,
                                       color: new Color(0.74f, 0.84f, 1.00f, 0.44f),
                                       rate: 175f, fall: MidFall, windFactor: 0.90f);
            _near   = BuildStreakLayer("Rain_Near", depth: 1.00f, order: 11,
                                       texW: 40, texH: 8, coreBias: 0.70f,
                                       size: 0.32f, lengthScale: 8f,
                                       color: new Color(0.86f, 0.92f, 1.00f, 0.26f),
                                       rate: 40f, fall: NearFall, windFactor: 1.40f);
        }

        private WeatherLayer BuildStreakLayer(string name, float depth, int order,
                                              int texW, int texH, float coreBias,
                                              float size, float lengthScale, Color color,
                                              float rate, float fall, float windFactor)
        {
            var layer = CreateLayer(name, depth);
            layer.BaseRate        = rate;
            layer.BaseColor       = color;
            layer.WindFactor      = windFactor;
            layer.AmbientResponse = 0.95f;   // falling water is lit by the sky and almost nothing else

            var main = layer.System.main;
            main.maxParticles = Mathf.CeilToInt(rate * 3.5f);
            main.startSize    = size;
            main.startLifetime = 1f;         // replaced in LayoutForViewport

            var shape = layer.System.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            var velocity = layer.System.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            velocity.y       = new ParticleSystem.MinMaxCurve(-fall * 1.12f, -fall * 0.88f);
            velocity.z       = new ParticleSystem.MinMaxCurve(0f, 0f);

            // The tail fade is longer than the head fade: a drop that has just entered the
            // frame is a drop, while a drop about to leave it is about to hit something.
            ApplyLifetimeFade(layer, fadeIn: 0.06f, fadeOut: 0.22f);

            var renderer = layer.Renderer;
            renderer.renderMode    = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale   = lengthScale;
            // Stretch by speed as well as by size, so a gust lengthens the streaks it
            // accelerates instead of only tilting them.
            renderer.velocityScale = 0.030f;
            SetupRenderer(layer, WeatherTextures.Streak(texW, texH, coreBias), additive: false, sortingOrder: order);

            return layer;
        }

        /// <summary>
        /// Ripples where the drops land. Top-down means the ground is the whole frame, so
        /// these spawn over the entire viewport rather than along a horizon line — and their
        /// rate therefore has to scale with viewport AREA, or zooming out would thin them out
        /// while the falling layers (which spawn along an edge that grows with the view) held
        /// their density.
        /// </summary>
        private WeatherLayer BuildSplash()
        {
            var layer = CreateLayer("Rain_Splash", depth: 0.35f);
            layer.BaseRate                  = 95f;
            layer.BaseColor                 = new Color(0.80f, 0.89f, 1.00f, 0.34f);
            layer.WindFactor                = 0f;      // a landed drop is not blown anywhere
            layer.AmbientResponse           = 0.90f;
            layer.RateScalesWithViewportArea = true;

            var main = layer.System.main;
            main.maxParticles  = 260;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.48f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.16f, 0.30f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var shape = layer.System.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            // A ripple expands fast and then stalls; the ease-out is the whole read.
            var size = layer.System.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0.00f, 0.20f),
                new Keyframe(0.45f, 0.88f),
                new Keyframe(1.00f, 1.15f)));

            ApplyLifetimeFade(layer, fadeIn: 0.10f, fadeOut: 0.70f);

            layer.Renderer.renderMode = ParticleSystemRenderMode.Billboard;
            SetupRenderer(layer, WeatherTextures.Ring(24), additive: false, sortingOrder: 5);

            return layer;
        }

        /// <summary>
        /// The haze the downpour hangs in the air. Enormous, almost invisible quads drifting
        /// slowly — individually unreadable, which is the point: it is the only layer that
        /// puts anything BETWEEN the drops, and without it the gaps between streaks are as
        /// clear as a dry day.
        /// </summary>
        private WeatherLayer BuildMist()
        {
            var layer = CreateLayer("Rain_Mist", depth: 0.25f);
            layer.BaseRate                   = 3.0f;
            layer.BaseColor                  = new Color(0.72f, 0.80f, 0.94f, 0.050f);
            layer.WindFactor                 = 0.45f;
            layer.AmbientResponse            = 1.00f;
            layer.RateScalesWithViewportArea = true;

            var main = layer.System.main;
            main.maxParticles  = 48;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSize     = new ParticleSystem.MinMaxCurve(3.5f, 7.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var shape = layer.System.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            var velocity = layer.System.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            velocity.y       = new ParticleSystem.MinMaxCurve(-0.35f, -0.10f);
            velocity.z       = new ParticleSystem.MinMaxCurve(0f, 0f);

            ApplyLifetimeFade(layer, fadeIn: 0.30f, fadeOut: 0.40f);

            layer.Renderer.renderMode = ParticleSystemRenderMode.Billboard;
            SetupRenderer(layer, WeatherTextures.Dot(64, falloff: 1.7f), additive: false, sortingOrder: 3);

            return layer;
        }

        // ── viewport ─────────────────────────────────────────────────────────────────

        protected override void LayoutForViewport(float halfW, float halfH)
        {
            float marginW = halfW + ViewportMargin;
            float marginH = halfH + ViewportMargin;

            LayoutFallingLayer(_far,  marginW, marginH, FarFall,  0.55f, 1.05f);
            LayoutFallingLayer(_mid,  marginW, marginH, MidFall,  0.55f, 1.05f);
            LayoutFallingLayer(_near, marginW, marginH, NearFall, 0.60f, 1.05f);

            LayoutAreaLayer(_splash, marginW, marginH);
            LayoutAreaLayer(_mist,   marginW, marginH);
        }

        // ── per-frame wind ───────────────────────────────────────────────────────────

        /// <summary>
        /// The falling layers give back the density a widened spawn slab spread out; the
        /// splash rate follows the MID layer's slab, because a drop that lands is a drop that
        /// was in the air, and the ripples have to thin out with the curtain rather than keep
        /// falling at their fair-weather rate.
        /// </summary>
        protected override float RateMultiplier(WeatherLayer layer)
            => layer == _splash ? _mid.SpawnWidthScale : layer.SpawnWidthScale;

        protected override void OnTick(float deltaTime)
        {
            float wind = WeatherWind.VelocityX;

            ApplyWindTo(_far,  wind);
            ApplyWindTo(_mid,  wind);
            ApplyWindTo(_near, wind);
            ApplyWindTo(_mist, wind);

            // The layout depends on the wind too (the spawn slab widens with the drift), but
            // re-laying it out every frame would rewrite five shapes for a gust that moves in
            // tenths of a unit. Re-run it only once the wind has moved enough to matter, and
            // reopen the gated rate write when it does — the slab width IS a rate term.
            if (!float.IsNaN(_appliedWind) && Mathf.Abs(wind - _appliedWind) <= 0.75f) return;

            _appliedWind = wind;
            LayoutForViewport(HalfWidth, HalfHeight);
            InvalidateRates();
        }
    }
}
