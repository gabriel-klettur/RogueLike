using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Tests.EditMode.Game.World.Weather
{
    /// <summary>
    /// The geometry of a falling layer, which is where a crosswind is either handled or
    /// silently ruins the effect.
    ///
    /// Wind does not rotate the curtain, it DISPLACES it, by an amount that grows with how
    /// long a particle stays airborne. Emit from a slab exactly as wide as the viewport in a
    /// crosswind and the upwind third of the screen is simply dry, while everything piles up
    /// on the other side. The fix — widen the slab upwind — then thins the on-screen density
    /// by the same factor, so the emission rate has to be multiplied back up by it, or turning
    /// the wind up makes the rain appear to stop.
    ///
    /// Both halves are asserted here, together, because either one alone is worse than neither.
    /// </summary>
    [TestFixture]
    public class WeatherLayoutTests
    {
        /// <summary>
        /// A minimal concrete effect. The layout helpers are protected statics, so reaching
        /// them means inheriting rather than reflecting — which also keeps the test honest
        /// about the surface a real effect actually uses.
        /// </summary>
        private sealed class ProbeEffect : WeatherEffect
        {
            public WeatherLayer Falling;
            public WeatherLayer Area;

            protected override void BuildLayers()
            {
                Falling = CreateLayer("Probe_Falling", 0.5f);
                Falling.WindFactor = 1f;
                Falling.BaseRate   = 100f;
                BoxShape(Falling);

                Area = CreateLayer("Probe_Area", 0.5f);
                Area.WindFactor = 0f;
                BoxShape(Area);
            }

            /// <summary>
            /// Every real layer emits from a Box; a bare ParticleSystem defaults to a Cone.
            /// The probe has to match, or the layout helpers would be measured against a shape
            /// no shipped effect uses.
            /// </summary>
            private static void BoxShape(WeatherLayer layer)
            {
                var shape = layer.System.shape;
                shape.enabled   = true;
                shape.shapeType = ParticleSystemShapeType.Box;
            }

            public void Layout(WeatherLayer layer, float marginW, float marginH, float fall)
                => LayoutFallingLayer(layer, marginW, marginH, fall, 0.55f, 1.05f);

            public void LayoutArea(WeatherLayer layer, float marginW, float marginH)
                => LayoutAreaLayer(layer, marginW, marginH);

            public void PushWind(WeatherLayer layer, float vx) => ApplyWindTo(layer, vx);
        }

        private GameObject _go;
        private ProbeEffect _probe;

        private const float MarginW = 12.5f;   // a 20x10 viewport plus the 2.5 u margin
        private const float MarginH = 7.5f;

        [SetUp]
        public void SetUp()
        {
            WeatherWind.WeatherSpeed = 0f;
            WeatherWind.SetDirection(-1f);
            _go    = new GameObject("Test_ProbeWeather");
            _probe = _go.AddComponent<ProbeEffect>();
            _probe.EnsureBuilt();   // Edit Mode never calls Awake
        }

        [TearDown]
        public void TearDown()
        {
            WeatherWind.WeatherSpeed = 0f;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── falling layers ───────────────────────────────────────────────────────────

        [Test]
        public void FallingLayer_SpawnsAboveTheTopEdge()
        {
            _probe.Layout(_probe.Falling, MarginW, MarginH, fall: 19f);
            var shape = _probe.Falling.System.shape;
            Assert.That(shape.position.y, Is.EqualTo(MarginH).Within(1e-3f),
                "particles must spawn outside the frame or they pop into existence on screen");
        }

        [Test]
        public void FallingLayer_LifetimeIsSizedToCrossTheViewport()
        {
            const float fall = 19f;
            _probe.Layout(_probe.Falling, MarginW, MarginH, fall);

            float full = (MarginH * 2f) / fall;
            var life = _probe.Falling.System.main.startLifetime;

            Assert.That(life.constantMax, Is.EqualTo(full * 1.05f).Within(1e-3f));
            // Randomised, not constant: a whole layer dying at the same height reads as a
            // hard line across the screen rather than as drops landing on different things.
            Assert.That(life.constantMin, Is.LessThan(life.constantMax * 0.9f));
        }

        [Test]
        public void Crosswind_WidensTheSpawnSlabUpwind()
        {
            _probe.Layout(_probe.Falling, MarginW, MarginH, fall: 19f);
            float calmWidth = _probe.Falling.System.shape.scale.x;
            float calmX     = _probe.Falling.System.shape.position.x;

            WeatherWind.WeatherSpeed = 20f;    // storm, blowing left
            _probe.Layout(_probe.Falling, MarginW, MarginH, fall: 19f);

            Assert.That(_probe.Falling.System.shape.scale.x, Is.GreaterThan(calmWidth),
                "the slab did not widen, so the upwind side of the screen stays dry");
            Assert.That(_probe.Falling.System.shape.position.x, Is.GreaterThan(calmX),
                "the slab must shift UPWIND — the wind blows left, so it moves right");
        }

        [Test]
        public void WideningTheSlab_IsPaidBackAsEmissionRate()
        {
            WeatherWind.WeatherSpeed = 20f;
            _probe.Layout(_probe.Falling, MarginW, MarginH, fall: 19f);

            float viewW = MarginW * 2f;
            float slabW = _probe.Falling.System.shape.scale.x;

            Assert.That(_probe.Falling.SpawnWidthScale, Is.GreaterThan(1f));
            Assert.That(_probe.Falling.SpawnWidthScale, Is.EqualTo(slabW / viewW).Within(1e-3f),
                "the rate compensation must be exactly the density the wider slab spread out");
        }

        [Test]
        public void SlabWidening_IsClampedSoASlowLayerInAStormDoesNotSimulateOffScreen()
        {
            // Snow at ~1 u/s is airborne for over ten seconds; the honest drift at storm wind
            // is more than a hundred units, almost all of it outside the frame.
            WeatherWind.WeatherSpeed = 60f;
            _probe.Layout(_probe.Falling, MarginW, MarginH, fall: 1.05f);

            Assert.That(_probe.Falling.SpawnWidthScale, Is.LessThanOrEqualTo(1f + 1.5f * 1.15f + 1e-3f));
        }

        [Test]
        public void WindBlownLayer_DiesAtWhicheverEdgeItReachesFirst()
        {
            const float fall = 1.05f;

            _probe.Layout(_probe.Falling, MarginW, MarginH, fall);
            float calmLife = _probe.Falling.System.main.startLifetime.constantMax;

            WeatherWind.WeatherSpeed = 40f;
            _probe.Layout(_probe.Falling, MarginW, MarginH, fall);
            float blownLife = _probe.Falling.System.main.startLifetime.constantMax;

            Assert.That(blownLife, Is.LessThan(calmLife),
                "a flake blown off the side must not stay alive for the whole fall it will never finish");
        }

        [Test]
        public void FlippingTheWind_MovesTheSlabToTheOtherSide()
        {
            WeatherWind.WeatherSpeed = 20f;

            WeatherWind.SetDirection(-1f);
            _probe.Layout(_probe.Falling, MarginW, MarginH, fall: 19f);
            float leftBlowX = _probe.Falling.System.shape.position.x;

            WeatherWind.SetDirection(1f);
            _probe.Layout(_probe.Falling, MarginW, MarginH, fall: 19f);
            float rightBlowX = _probe.Falling.System.shape.position.x;

            Assert.That(leftBlowX, Is.GreaterThan(0f));
            Assert.That(rightBlowX, Is.LessThan(0f));
        }

        // ── area layers ──────────────────────────────────────────────────────────────

        [Test]
        public void AreaLayer_CoversTheWholeVisibleBox()
        {
            // Top-down: the ground a splash lands on is the entire frame, not a horizon line.
            _probe.LayoutArea(_probe.Area, MarginW, MarginH);
            var shape = _probe.Area.System.shape;
            Assert.That(shape.scale.x, Is.EqualTo(MarginW * 2f).Within(1e-3f));
            Assert.That(shape.scale.y, Is.EqualTo(MarginH * 2f).Within(1e-3f));
            Assert.That(shape.position, Is.EqualTo(Vector3.zero));
        }

        // ── wind push ────────────────────────────────────────────────────────────────

        [Test]
        public void ApplyWind_ScalesByTheLayersWindFactor_AndSpreadsTheSpeed()
        {
            _probe.Falling.WindFactor = 1.4f;
            _probe.PushWind(_probe.Falling, -10f);

            var vx = _probe.Falling.System.velocityOverLifetime.x;
            float mid = (vx.constantMin + vx.constantMax) * 0.5f;

            Assert.That(mid, Is.EqualTo(-14f).Within(1e-3f));
            Assert.That(vx.constantMin, Is.Not.EqualTo(vx.constantMax),
                "one identical horizontal speed makes a layer read as a rigid sheet");
        }

        [Test]
        public void ApplyWind_LeavesAWindImmuneLayerAlone()
        {
            // Splashes and settled snow have landed; nothing blows them anywhere.
            _probe.PushWind(_probe.Area, -10f);
            Assert.That(_probe.Area.System.velocityOverLifetime.enabled, Is.False);
        }

        // ── tint ─────────────────────────────────────────────────────────────────────

        [Test]
        public void SetTint_FoldsAmbientIntoRgbAndTheFadeIntoAlpha()
        {
            var layer = _probe.Falling;
            layer.BaseColor       = new Color(0.8f, 0.9f, 1f, 0.5f);
            layer.AmbientResponse = 1f;
            layer.SetTint(new Color(0.5f, 0.5f, 0.5f, 1f), fadeAlpha: 0.5f);

            var c = layer.System.main.startColor.color;
            Assert.That(c.r, Is.EqualTo(0.4f).Within(1e-3f));
            Assert.That(c.a, Is.EqualTo(0.25f).Within(1e-3f));
        }

        [Test]
        public void AmbientResponseZero_KeepsALayerAtItsDaylightColour()
        {
            var layer = _probe.Falling;
            layer.BaseColor       = new Color(1f, 1f, 1f, 1f);
            layer.AmbientResponse = 0f;
            layer.SetTint(new Color(0.3f, 0.3f, 0.5f, 1f), fadeAlpha: 1f);

            var c = layer.System.main.startColor.color;
            Assert.That(c.r, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(c.b, Is.EqualTo(1f).Within(1e-3f));
        }
    }
}
