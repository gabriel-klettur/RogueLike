using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Snow, as four depth slices.
    ///
    /// Snow is the weather where depth matters most, because every flake is the same object:
    /// the ONLY cue for how far away one is is how big, how bright and how fast it is. So the
    /// FAR layer is a dense field of tiny slow dim dots, the MID layer carries the readable
    /// fall, and the NEAR layer is a handful of large, faint, tumbling six-armed crystals
    /// close enough to the lens to be out of focus. A SETTLE layer adds specks that appear on
    /// the ground and slowly fade, which is what makes the snow look like it is landing
    /// somewhere rather than falling through an empty screen forever.
    ///
    /// Flakes take much less of the day/night tint than rain does
    /// (<see cref="WeatherLayer.AmbientResponse"/>): a snowfield is the brightest thing in a
    /// night scene, and a flake tinted all the way down to the ambient simply vanishes.
    ///
    /// Because snow falls slowly it is the effect the wind moves MOST — at storm wind a flake
    /// travels further sideways than downward, which the shared falling-layer layout handles
    /// by widening the spawn slab upwind and shortening the lifetime to whichever screen edge
    /// the flake reaches first.
    /// </summary>
    public sealed class SnowEffect : WeatherEffect
    {
        private const float FarFall  = 1.05f;
        private const float MidFall  = 1.65f;
        private const float NearFall = 2.60f;

        private WeatherLayer _far;
        private WeatherLayer _mid;
        private WeatherLayer _near;
        private WeatherLayer _settle;

        private float _appliedWind = float.NaN;

        /// <summary>
        /// Hoisted read buffer for <see cref="ParticleSystem.GetParticles(ParticleSystem.Particle[])"/>.
        /// Grown to the high-water mark and reused: the landing sweep runs every frame, and a
        /// fresh array per frame would be a kilobyte of garbage per flake alive.
        /// </summary>
        private ParticleSystem.Particle[] _readBuffer = System.Array.Empty<ParticleSystem.Particle>();

        protected override WeatherIntensity DefaultIntensity => WeatherIntensity.Medium;

        /// <summary>
        /// Silent on purpose. Falling snow makes no sound — a bed under it would be inventing
        /// one, and the silence is most of why snow reads as cold and still.
        /// </summary>
        protected override AudioClip ResolveAudioClip() => null;

        // ── build ────────────────────────────────────────────────────────────────────

        protected override void BuildLayers()
        {
            _settle = BuildSettle();

            _far = BuildFlakeLayer("Snow_Far", depth: 0.15f, order: 4,
                                   texture: WeatherTextures.Dot(16, falloff: 0.95f),
                                   sizeMin: 0.045f, sizeMax: 0.075f,
                                   color: new Color(0.84f, 0.89f, 1.00f, 0.55f),
                                   rate: 70f, fall: FarFall, windFactor: 0.55f,
                                   ambientResponse: 0.55f, maxParticles: 600,
                                   flutter: 0.20f, spin: 0f);

            _mid = BuildFlakeLayer("Snow_Mid", depth: 0.50f, order: 7,
                                   texture: WeatherTextures.Dot(24, falloff: 0.65f),
                                   sizeMin: 0.095f, sizeMax: 0.150f,
                                   color: new Color(0.96f, 0.98f, 1.00f, 0.82f),
                                   rate: 42f, fall: MidFall, windFactor: 0.85f,
                                   ambientResponse: 0.45f, maxParticles: 400,
                                   flutter: 0.42f, spin: 0f);

            _near = BuildFlakeLayer("Snow_Near", depth: 1.00f, order: 11,
                                    texture: WeatherTextures.Crystal(32),
                                    sizeMin: 0.24f, sizeMax: 0.40f,
                                    color: new Color(1.00f, 1.00f, 1.00f, 0.34f),
                                    rate: 9f, fall: NearFall, windFactor: 1.35f,
                                    ambientResponse: 0.35f, maxParticles: 110,
                                    flutter: 0.70f, spin: 55f);
        }

        private WeatherLayer BuildFlakeLayer(string name, float depth, int order, Texture2D texture,
                                             float sizeMin, float sizeMax, Color color, float rate,
                                             float fall, float windFactor, float ambientResponse,
                                             int maxParticles, float flutter, float spin)
        {
            var layer = CreateLayer(name, depth);
            layer.BaseRate        = rate;
            layer.BaseColor       = color;
            layer.WindFactor      = windFactor;
            layer.AmbientResponse = ambientResponse;

            var main = layer.System.main;
            main.maxParticles  = maxParticles;
            main.startSize     = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startLifetime = 6f;    // replaced in LayoutForViewport

            var shape = layer.System.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            var velocity = layer.System.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            velocity.y       = new ParticleSystem.MinMaxCurve(-fall * 1.18f, -fall * 0.82f);
            velocity.z       = new ParticleSystem.MinMaxCurve(0f, 0f);

            // The flutter. A flake does not fall, it tumbles — and the noise module is the
            // only cheap way to give each one an individual path. Strength is much higher on
            // X than on Y because a flake wanders sideways while still descending; letting it
            // wander vertically at the same amplitude makes flakes visibly rise, which reads
            // as an updraft rather than as air.
            if (flutter > 0f)
            {
                var noise = layer.System.noise;
                noise.enabled = true;
                // separateAxes FIRST: without it the per-axis strengths are not read at all
                // and the module falls back to the scalar `strength`, which is 1 by default —
                // so the old snow was being shoved a full unit per axis, vertical included,
                // by a module its author believed was set to 0.4 horizontal and 0.05 vertical.
                noise.separateAxes = true;
                noise.strengthX    = flutter;
                noise.strengthY    = flutter * 0.18f;
                noise.strengthZ    = 0f;
                noise.frequency    = 0.22f;
                noise.scrollSpeed  = 0.35f;
                noise.quality      = ParticleSystemNoiseQuality.Medium;
                // Damping ties strength to frequency; the flutter is authored as a world-space
                // amplitude and should not shrink if the frequency is ever retuned.
                noise.damping      = false;
            }

            if (spin > 0f)
            {
                var rot = layer.System.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-spin * Mathf.Deg2Rad, spin * Mathf.Deg2Rad);
            }

            ApplyLifetimeFade(layer, fadeIn: 0.08f, fadeOut: 0.26f);

            layer.Renderer.renderMode = ParticleSystemRenderMode.Billboard;
            SetupRenderer(layer, texture, additive: false, sortingOrder: order);

            return layer;
        }

        /// <summary>
        /// Flakes that have landed. They do not move: they appear across the ground, hold, and
        /// fade — which the eye integrates as accumulation, without anything having to be
        /// written to the tilemap or persisted.
        ///
        /// This is the layer that replaced the old "shrink to 30% then vanish" melt, which was
        /// a lie in the wrong direction: it made snow disappear on contact, so the world it
        /// fell on never changed and the effect read as a screen overlay.
        /// </summary>
        private WeatherLayer BuildSettle()
        {
            var layer = CreateLayer("Snow_Settle", depth: 0.30f);
            layer.BaseColor                  = new Color(0.98f, 0.99f, 1.00f, 0.30f);
            layer.WindFactor                 = 0f;
            layer.AmbientResponse            = 0.50f;
            // Rate 0: these are not emitted on a schedule, they are emitted ONE PER LANDING
            // from the sweep in CollectLandings. That is the whole difference between snow
            // that has settled and a second layer of snow that happens to be stationary — a
            // speck appears exactly where a flake stopped, so the pattern on the ground is
            // the pattern that fell, wind shadow and all.
            layer.BaseRate                   = 0f;
            layer.RateScalesWithViewportArea = false;

            var main = layer.System.main;
            main.maxParticles  = 600;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 6.0f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.055f, 0.13f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var shape = layer.System.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            // Slow in, slow out. A settled speck that pops is a speck the eye tracks as an
            // event; one that arrives over a third of a second is just snow that is already there.
            ApplyLifetimeFade(layer, fadeIn: 0.22f, fadeOut: 0.45f);

            layer.Renderer.renderMode = ParticleSystemRenderMode.Billboard;
            SetupRenderer(layer, WeatherTextures.Dot(16, falloff: 1.10f), additive: false, sortingOrder: 5);

            return layer;
        }

        // ── viewport + wind ──────────────────────────────────────────────────────────

        protected override void LayoutForViewport(float halfW, float halfH)
        {
            float marginW = halfW + ViewportMargin;
            float marginH = halfH + ViewportMargin;

            LayoutFallingLayer(_far,  marginW, marginH, FarFall,  0.60f, 1.05f);
            LayoutFallingLayer(_mid,  marginW, marginH, MidFall,  0.60f, 1.05f);
            LayoutFallingLayer(_near, marginW, marginH, NearFall, 0.65f, 1.05f);

            LayoutAreaLayer(_settle, marginW, marginH);
        }

        /// <summary>
        /// Falling layers give back the density their widened spawn slab spread out. The settle
        /// layer is not on a rate at all — see <see cref="BuildSettle"/>.
        /// </summary>
        protected override float RateMultiplier(WeatherLayer layer) => layer.SpawnWidthScale;

        // ── landings ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Find the flakes that stop this frame and record where.
        ///
        /// A landing is a particle about to expire. There is deliberately no physics here:
        /// Unity's particle collision would work, and it would be WRONG in this projection —
        /// a building's collider is its FOOTPRINT while its sprite is drawn rising above it,
        /// so colliding flakes would pile along the base of every house instead of on its
        /// roof. The randomised lifetimes already stop flakes at a spread of heights, and the
        /// per-sprite alpha cap in the shader is what decides that a landing over a roof sits
        /// ON the roof. The two together give the right answer; collision gives a confident
        /// wrong one.
        ///
        /// Each landing does two things: it stamps <see cref="SnowSplatMap"/>, which is the
        /// lasting record the world shader reads, and it emits one settled speck, which is the
        /// visible moment of arrival. Neither substitutes for the other — the map has no
        /// grain and the specks have no permanence.
        ///
        /// Only the MID and NEAR layers are swept. The far layer is background haze whose
        /// individual flakes are two pixels across; including it would triple the cost of the
        /// sweep to move the drift by an amount nothing can see.
        /// </summary>
        private void CollectLandings(float deltaTime)
        {
            if (Density <= 0.001f || deltaTime <= 0f) return;

            var map = SnowSplatMap.Instance;
            CollectFrom(_mid,  deltaTime, map);
            CollectFrom(_near, deltaTime, map);
        }

        private void CollectFrom(WeatherLayer layer, float deltaTime, SnowSplatMap map)
        {
            if (layer == null) return;

            var ps = layer.System;
            int alive = ps.particleCount;
            if (alive == 0) return;

            if (_readBuffer.Length < alive)
                _readBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(alive)];

            int read = ps.GetParticles(_readBuffer, alive);
            for (int i = 0; i < read; i++)
            {
                // About to die THIS step. The window is exactly one frame, so a given particle
                // passes the test once and is counted once — widening it would stamp the same
                // flake on several consecutive frames and pile the drift up far too fast.
                if (_readBuffer[i].remainingLifetime > deltaTime) continue;

                Vector3 landing = _readBuffer[i].position;   // world space: the layers simulate in it
                map?.Stamp(landing);
                EmitSettledSpeck(landing);
            }
        }

        /// <summary>
        /// Place one settled speck at a landing. Emitted with an explicit position and no
        /// velocity, so it stays exactly where the flake stopped instead of being re-rolled
        /// through the layer's emitter shape.
        /// </summary>
        private void EmitSettledSpeck(Vector3 worldPosition)
        {
            if (_settle == null) return;

            var emit = new ParticleSystem.EmitParams
            {
                position             = worldPosition,
                applyShapeToPosition = false,
            };
            _settle.System.Emit(emit, 1);
        }

        protected override void OnTick(float deltaTime)
        {
            CollectLandings(deltaTime);

            float wind = WeatherWind.VelocityX;

            ApplyWindTo(_far,  wind);
            ApplyWindTo(_mid,  wind);
            ApplyWindTo(_near, wind);

            if (!float.IsNaN(_appliedWind) && Mathf.Abs(wind - _appliedWind) <= 0.4f) return;

            // Tighter threshold than rain's: snow is airborne for many seconds, so the same
            // change in wind speed moves its spawn slab several times further.
            _appliedWind = wind;
            LayoutForViewport(HalfWidth, HalfHeight);
            InvalidateRates();
        }
    }
}
