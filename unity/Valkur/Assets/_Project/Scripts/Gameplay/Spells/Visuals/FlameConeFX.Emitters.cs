using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The two particle layers of the breath: the jet itself, and the embers it leaves behind.
    /// </summary>
    internal sealed partial class FlameConeFX
    {
        /// <summary>
        /// How long a jet particle lives. Its speed is then derived so that it covers the
        /// cone's reach in exactly that time — the particles have to STOP where the damage
        /// stops, or the fire promises ground it does not cover.
        /// </summary>
        private const float FIRE_LIFETIME = 0.36f;

        /// <summary>Jet particles per second, per world unit of reach, so a longer cone is not a thinner one.</summary>
        private const float FIRE_DENSITY = 26f;

        private const float EMBER_LIFETIME_MIN = 0.85f;
        private const float EMBER_LIFETIME_MAX = 1.60f;
        private const float EMBER_DENSITY = 5.5f;

        private void BuildEmitters()
        {
            _fire = BuildJet();
            _embers = BuildEmbers();
        }

        private ParticleSystem BuildJet()
        {
            var go = new GameObject("Jet");
            go.transform.SetParent(_emitterRoot, false);
            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent starts the system immediately, and Unity refuses main.duration on a
            // playing system — it logs and silently keeps the old value. Stop, configure, Play.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float travel = _length / FIRE_LIFETIME;

            var main = ps.main;
            main.duration = 999f;
            main.loop = true;
            main.startLifetime = FIRE_LIFETIME;
            main.startSpeed = new ParticleSystem.MinMaxCurve(travel * 0.78f, travel * 1.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(_length * 0.10f, _length * 0.19f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // WHITE, not the palette. colorOverLifetime MULTIPLIES the start colour, so
            // tinting both means an orange times an orange — a darker, redder particle than
            // either layer asks for, and on an additive surface a darker particle is simply
            // less light. The gradient is the single owner of the hue.
            main.startColor = Color.white;
            // World space, so a caster who walks tows the fire instead of dragging a decal.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.30f;   // fire rises
            main.maxParticles = Mathf.CeilToInt(FIRE_DENSITY * _length * FIRE_LIFETIME * 2.5f) + 16;

            var emission = ps.emission;
            emission.rateOverTime = 0f;      // the envelope opens it; see Tick

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = _halfArcRad * Mathf.Rad2Deg;
            shape.radius = Mathf.Max(0.02f, _length * MOUTH_WIDTH * 0.5f);
            shape.radiusThickness = 1f;

            // The saturated ramp, not the aura palette. Core is near-colourless (0.25
            // saturation, measured) and Edge is at 0.62 value, so the jet used to be born pale
            // and die dark — washed out where it is densest and adding nothing where it spreads.
            ApplyGradient(ps, Color.Lerp(FireHue(0f), Color.white, 0.50f),
                          FireHue(0.35f), FireHue(1f), 1f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.35f, 1f, 1.75f));

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-2.4f, 2.4f);

            // The jet slows as it spreads, which is what separates a breath from a spray of
            // pellets. Drag is a module the live-resize path must remember to rewrite, which is
            // why it is set from _length and nothing else.
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.16f;
            limit.limit = new ParticleSystem.MinMaxCurve(travel);

            ConfigureRenderer(ps, ElementalSprites.Glow, ORDER_PARTICLES);
            ps.Play();
            return ps;
        }

        private ParticleSystem BuildEmbers()
        {
            var go = new GameObject("Embers");
            go.transform.SetParent(_emitterRoot, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 999f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(EMBER_LIFETIME_MIN, EMBER_LIFETIME_MAX);
            // Slower than the jet on purpose: embers OUTLIVE the fire that threw them, which is
            // the only layer that says the air is still hot once the breath stops.
            main.startSpeed = new ParticleSystem.MinMaxCurve(_length * 0.55f, _length * 1.30f);
            main.startSize = new ParticleSystem.MinMaxCurve(_length * 0.022f, _length * 0.055f);
            main.startColor = Color.white;   // see the note on the jet
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.55f;
            main.maxParticles = Mathf.CeilToInt(EMBER_DENSITY * _length * EMBER_LIFETIME_MAX * 2f) + 12;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = _halfArcRad * Mathf.Rad2Deg * 1.15f;
            shape.radius = Mathf.Max(0.02f, _length * MOUTH_WIDTH * 0.6f);
            shape.radiusThickness = 1f;

            ApplyGradient(ps, Color.Lerp(FireHue(0f), Color.white, 0.72f),
                          FireHue(0.5f), FireHue(1f), 1f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));

            var noise = ps.noise;
            noise.enabled = true;
            // separateAxes FIRST, or the module reads the scalar `strength`, which defaults to 1
            // and shoves an ember a full unit on every axis regardless of what is authored here.
            noise.separateAxes = true;
            noise.strengthX = _length * 0.10f;
            noise.strengthY = _length * 0.06f;
            noise.strengthZ = 0f;
            noise.frequency = 1.4f;

            ConfigureRenderer(ps, ElementalSprites.Sparkle, ORDER_PARTICLES + 1);
            ps.Play();
            return ps;
        }

        /// <summary>
        /// Colour over lifetime, from the resolved palette rather than from a hardcoded
        /// fire/frost pair. The old controller branched on <c>element == "fire"</c> and carried
        /// a whole second set of frost constants for a spell that does not exist — while
        /// ignoring the <c>particleColor</c> the F4 panel exposes. One palette answers both.
        /// </summary>
        private static void ApplyGradient(ParticleSystem ps, Color hot, Color mid, Color cool, float peakAlpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(hot, 0f),
                    new GradientColorKey(mid, 0.35f),
                    new GradientColorKey(cool, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peakAlpha, 0.16f),
                    new GradientAlphaKey(peakAlpha * 0.65f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;
        }

        /// <summary>
        /// A material handed to a <c>ParticleSystemRenderer</c> must carry its OWN texture — a
        /// SpriteRenderer supplies one and a particle renderer does not. The old rig assigned
        /// <c>ElementalSprites.SharedUnlitMaterial</c>, whose <c>mainTexture</c> is null, so
        /// every particle of every breath drew as a hard opaque QUAD on an alpha blend: no soft
        /// falloff, and no way for fire to blow out to white.
        /// </summary>
        private static void ConfigureRenderer(ParticleSystem ps, Sprite sprite, int order)
        {
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sharedMaterial = ParticleMaterialCache.Get(sprite.texture, additive: true);
            psr.sortingLayerName = SortingConfig.LAYER_VFX;
            psr.sortingOrder = order;
        }
    }
}
