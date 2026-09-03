using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// THE FIRE — the shipped torch flame's recipe, in violet, spread over the whole damage
    /// disc instead of standing on a stick.
    ///
    /// <para>THE SILHOUETTE COMES FROM THE QUAD, THE SOFTNESS FROM THE TEXTURE. Three
    /// attempts, each photographed against a flat background, and each one wrong in a way that
    /// was invisible in the numbers:</para>
    /// <list type="number">
    /// <item>Flame-shaped CUT-OUTS (KiSprites' tongue variants). At 16 PPU a hard-edged tongue
    /// a world unit tall is a shape the eye resolves one at a time, so forty of them read as a
    /// scatter of violet CONES — a decal pattern, not a fire.</item>
    /// <item><c>PP_torch_flame</c>'s own texture, <see cref="ParticleTextureShape.Glow"/> at
    /// softness 0.55. Glow's alpha is `skirt + core * 0.9`, which sums past 1 across the middle
    /// of the sprite: every particle is a saturated disc with a visible EDGE. A torch gets away
    /// with it because its particles are 0.22-0.38 u and those edges are sub-pixel; at the size
    /// this rig needs to cover a five-unit disc they are the loudest thing on screen, and the
    /// fire rendered as a heap of violet BUBBLES.</item>
    /// <item>SoftDot at a low softness. No plateau, so no edges — but the falloff is so tight
    /// that a 1 u particle draws a 0.15 u dot, and the patch went back to being sparse.</item>
    /// </list>
    /// <para>What works is a BROAD SoftDot falloff on a quad that is TALLER THAN IT IS WIDE.
    /// Round particles cannot read as fire at any softness or density — fire is a vertical
    /// thing, and a circle says nothing about which way it is going. Stretching the quad
    /// roughly 1:2.8 turns the same soft blob into a lick, and the rising velocity then reads
    /// as the lick climbing rather than as a dot drifting. The texture stays radially
    /// symmetric; it is the QUAD that carries the shape, which is why this is not the cut-out
    /// idea again — there is no hard edge anywhere in it.</para>
    ///
    /// <para>Everything else is <c>PP_torch_flame</c>'s: the size-over-life swell, the alpha
    /// envelope, the noise settings and the four-stop ramp, with the footprint widened to the
    /// damage circle and the hue moved onto this spell's palette.</para>
    ///
    /// <para>The ramp is the torch's four stops with the hue moved: near-white, the palette's
    /// core, its glow, then a dark tail. Keeping four stops is what makes a flame look like it
    /// is COOLING as it rises; a two-stop fade just makes it disappear.</para>
    ///
    /// <para>Every emitter here follows the two rules the rest of the project's particle code
    /// records: the system is stopped before it is configured (Unity silently refuses
    /// <c>main.duration</c> on a playing system), and the material comes from
    /// <see cref="ParticleMaterialCache"/> with its own texture — a
    /// <c>ParticleSystemRenderer</c> pointed at <c>ElementalSprites.SharedUnlitMaterial</c>
    /// draws hard white squares, and an unrelated healing aura writes <c>mainTexture</c>
    /// through that same static.</para>
    /// </summary>
    public partial class ArcaneFlameController
    {
        // ── Lifetimes ───────────────────────────────────────────────────────────
        //
        // The tail after the emitters stop is sized by the LONGEST of these: whatever is still
        // in the air when the effect ends has to be allowed to finish, or the exit deletes live
        // particles mid-flight — the failure the old rig had on every one of its five exits.
        private const float BodyLifetime  = 0.60f;   // PP_torch_flame ships 0.55
        private const float CoreLifetime  = 0.42f;
        private const float EmberLifetime = 1.25f;
        private const float SmokeLifetime = 2.00f;
        internal const float LongestParticleLifetime = SmokeLifetime;

        // ── Density ─────────────────────────────────────────────────────────────
        //
        // Rates are authored for the SHIPPED 2.5 u radius and scaled from there. A bigger patch
        // of fire has MORE flames, not taller ones — the same statement KiAuraFX makes when its
        // intensity dial moves density and behaviour while barely moving height. The exponent
        // is below 2 so a large zone is not scaled by raw area, and the result is clamped so no
        // authored radius can walk the rig out of its particle budget.
        private const float ReferenceRadius = 2.5f;
        private const float DensityExponent = 1.35f;
        private const float DensityClampMin = 0.55f;
        private const float DensityClampMax = 1.60f;

        private const float BodyRate  = 70f;
        private const float CoreRate  = 55f;
        private const float EmberRate = 9f;   // PP_torch_embers is the same layer on a stick
        private const float SmokeRate = 4f;
        // Budget (vfx-authoring SKILL.md — the number to hold is emitRate x lifespan):
        // 70x0.60 + 55x0.42 + 9x1.25 + 4x2.00 = 42.0+23.1+11.3+8.0 = 84.4 live at the shipped
        // radius. That is ABOVE the skill's "player aura / trail <= 60" band and below its
        // "signature spell <= 120", and the choice is deliberate rather than a slip.
        //
        // A torch is 26 x 0.55 = 14.3 live over a flame half a unit wide. This disc is five
        // units across, roughly ten times that area, so torch density here would be ~143
        // particles. The skill's own "bigger, fewer, softer" rule buys most of the difference —
        // these particles are several times a torch flame's — but not all of it: below about
        // eighty the licks stop overlapping and the patch reads as scattered wisps rather than
        // as ground on fire, which is what the photographs at 49.9 and 59.1 both showed. The
        // spell can afford it: maxInstances is 1, the field lives five seconds on a seven
        // second cooldown, and there is never a second one on screen.

        // ── Flame geometry, in WORLD UNITS and deliberately not scaled by radius ─
        //
        // A character is ~2.5 u tall and a flame here rises less than a unit over its life. It
        // covers the feet of anything standing in the fire and no more, which is both what a
        // burning patch looks like and the reason this rig needs no depth split: nothing in it
        // is tall enough to paint a body out. VortexFunnelFX needed NECK_CLEAR_HEIGHT precisely
        // because its funnel was not short.
        // A lick is roughly 1:2.8. The WIDTH range is narrow and the HEIGHT range is wide, so
        // the licks vary in length rather than in fatness — varying both makes some of them
        // square again, and a square one is a bubble.
        private const float BodyWidthMin  = 0.47f;
        private const float BodyWidthMax  = 0.62f;
        private const float BodyHeightMin = 1.10f;
        private const float BodyHeightMax = 1.60f;
        private const float CoreWidthMin  = 0.30f;
        private const float CoreWidthMax  = 0.40f;
        private const float CoreHeightMin = 0.70f;
        private const float CoreHeightMax = 1.15f;

        // ── The texture ─────────────────────────────────────────────────────────
        //
        // SoftDot, HIGH softness. SoftDot is `(1-r)^Lerp(8, 1.2, softness)` — no plateau, so no
        // edge at any setting — and the parameter is an EXPONENT, so a high softness is the
        // BROAD falloff. Measured: at 0.30 a particle keeps 5 % of its alpha at half radius and
        // draws a dot a seventh of its own size; at 0.95 it keeps 39 % and fills the quad. The
        // broad one is the only one that overlaps its neighbours into a single body of fire.
        // The core layer is a little tighter so it still has a centre to be the hot part of.
        private const float BodySoftness = 0.95f;
        private const float CoreSoftness = 0.85f;

        // ── Where the fire is emitted from ──────────────────────────────────────
        //
        // The full damage circle, minus how far the noise will throw a particle over its own
        // lifetime. That reach is roughly `3.67 x strength x lifetime` — CLAUDE.md's measured
        // rule, which is why it is a factor and not a constant. Without the subtraction the
        // fire draws flames on ground that does not hurt, which is invariant 1 broken from the
        // other side.
        private const float NoiseReachFactor     = 3.67f;
        private const float EmissionRadiusMul    = 1.00f;
        private const float MinEmissionRadiusMul = 0.30f;

        private ParticleSystem _flameBody, _flameCore, _embers, _smoke;
        private readonly List<ParticleSystem> _emitters = new List<ParticleSystem>(4);
        /// <summary>
        /// Each emitter's authored rate, already scaled for this instance's radius. The
        /// envelope multiplies THIS rather than reading the live rate back, or one frame of the
        /// ignition ramp would become the new baseline and the fire would never reach full.
        /// </summary>
        private readonly List<float> _emitterBaseRates = new List<float>(4);

        /// <summary>Rate multiplier for this instance's radius. See the density note above.</summary>
        private float DensityScale => Mathf.Clamp(
            Mathf.Pow(_radius / ReferenceRadius, DensityExponent), DensityClampMin, DensityClampMax);

        /// <summary>
        /// The emission disc for a layer, already pulled in by how far its noise will throw a
        /// particle over its own lifetime.
        /// </summary>
        private float EmissionRadius(float noiseStrength, float lifetime)
        {
            float wander = NoiseReachFactor * noiseStrength * lifetime;
            return Mathf.Max(_radius * MinEmissionRadiusMul, _radius * EmissionRadiusMul - wander);
        }

        private void BuildFire()
        {
            _emitters.Clear();
            _emitterBaseRates.Clear();

            // Two flame layers, the same split a torch has between its body and its root: the
            // body is bigger, slower and cooler, the core is smaller, lower and near-white. One
            // layer alone gives every particle the same size and speed, and a crowd of those
            // reads as a texture scrolling upward rather than as fire.
            // The peak alphas are low ON PURPOSE. On an additive surface alpha is coverage,
            // so a particle at 0.85 is already opaque on its own and the layer stops being a
            // volume the moment two of them overlap. At these values a single flame is faint
            // and the fire is what a dozen of them ADD UP TO — which is the only way a crowd
            // of round sprites stops being a crowd of round sprites.
            _flameBody = BuildFlameLayer("FlameBody", BodyRate, BodyLifetime,
                BodyWidthMin, BodyWidthMax, BodyHeightMin, BodyHeightMax,
                riseMin: 0.91f, riseMax: 1.30f,
                noise: 0.13f, hotBias: 0f, peakAlpha: 0.55f, softness: BodySoftness, order: 11);
            _flameCore = BuildFlameLayer("FlameCore", CoreRate, CoreLifetime,
                CoreWidthMin, CoreWidthMax, CoreHeightMin, CoreHeightMax,
                riseMin: 0.67f, riseMax: 0.95f,
                noise: 0.10f, hotBias: 0.45f, peakAlpha: 0.80f, softness: CoreSoftness, order: 12);

            _embers = BuildEmbers();
            _smoke = BuildSmoke();

            Register(_flameBody); Register(_flameCore); Register(_embers); Register(_smoke);
        }

        private void Register(ParticleSystem ps)
        {
            if (ps == null) return;
            _emitters.Add(ps);
            _emitterBaseRates.Add(ps.emission.rateOverTime.constant);
        }

        /// <summary>
        /// One layer of flame, built to <c>PP_torch_flame</c>'s recipe: soft round glows rising
        /// on a wobbling noise field, growing quickly then shrinking away, cooling through the
        /// ramp as they go.
        /// </summary>
        private ParticleSystem BuildFlameLayer(string name, float rate, float life,
            float widthMin, float widthMax, float heightMin, float heightMax,
            float riseMin, float riseMax,
            float noise, float hotBias, float peakAlpha, float softness, int order)
        {
            var ps = NewSystem(name);
            float scaledRate = rate * DensityScale;

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = life;
            // Zero, and the rise is owned by velocityOverLifetime instead. A Circle shape emits
            // ALONG ITS RADIUS, so any startSpeed here would throw the fire outward across the
            // floor rather than up off it.
            main.startSpeed = 0f;
            // The QUAD is what makes this a flame rather than a bubble. See the class note.
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(widthMin, widthMax);
            main.startSizeY = new ParticleSystem.MinMaxCurve(heightMin, heightMax);
            main.startSizeZ = 1f;
            // A lick leans, it does not spin, and a stretched quad turned even a quarter turn
            // is a flame lying on its side. Roughly +/-6 degrees, in radians.
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.10f, 0.10f);
            // WHITE, not the palette: colorOverLifetime MULTIPLIES the start colour, so tinting
            // both gives a violet times a violet — darker than either layer asks for, and on an
            // additive surface darker simply means less light. The ramp owns the hue.
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = Mathf.CeilToInt(scaledRate * life * 2f) + 16;

            var emission = ps.emission;
            emission.rateOverTime = scaledRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = EmissionRadius(noise, life);
            shape.radiusThickness = 1f;   // fill the disc, not just its rim

            // Up. All three axes are two-constant curves in the SAME mode: assigning only y
            // leaves x and z as single constants and Unity rejects the mismatch with "Particle
            // Velocity curves must all be in the same mode", once per frame, forever.
            //
            // And a flame must not out-travel its own size, or the patch drifts off the circle
            // it is burning. Measured on the version before this one, at 1.05-1.95 u/s the fire
            // climbed as far as it was tall and visibly sat ABOVE the boundary ring with the
            // lower half of the damage circle empty.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.18f, 0.18f);
            vel.y = new ParticleSystem.MinMaxCurve(riseMin, riseMax);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ApplyFireGradient(ps, peakAlpha, hotBias);

            // PP_torch_flame's own curve: swell fast, then shrink to nearly nothing. The swell
            // is what makes a flame look fed from below rather than sprayed.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));

            // The waver. With round particles this is what makes the fire's OUTLINE move, which
            // is the single difference between fire and a light. The torch preset's numbers.
            var ns = ps.noise;
            ns.enabled = true;
            // separateAxes FIRST, or the module reads the scalar `strength`, which defaults to
            // 1 and shoves a particle a full unit on every axis regardless of what is set here.
            ns.separateAxes = true;
            ns.strengthX = noise;
            ns.strengthY = noise * 0.45f;
            ns.strengthZ = 0f;
            ns.frequency = 1.6f;
            ns.damping = true;
            ns.scrollSpeed = 0.9f;

            ConfigureFireRenderer(ps, ParticleTextureLibrary.Get(ParticleTextureShape.SoftDot, softness),
                order, additive: true);
            ps.Play();
            return ps;
        }

        /// <summary>
        /// Embers, which outlive the flame that threw them. This is the only layer saying the
        /// air above the patch is still hot, and it is what carries the effect upward past the
        /// flame height without making the flames themselves tall. <c>PP_torch_embers</c> is the
        /// same idea on a stick.
        /// </summary>
        private ParticleSystem BuildEmbers()
        {
            var ps = NewSystem("Embers");
            float scaledRate = EmberRate * DensityScale;
            const float noise = 0.10f;

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = EmberLifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.135f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            // World space: an ember that has left the fire is no longer attached to it, so it
            // must not be dragged if anything ever moves this root.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(scaledRate * EmberLifetime * 2f) + 10;

            var emission = ps.emission;
            emission.rateOverTime = scaledRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = EmissionRadius(noise, EmberLifetime);
            shape.radiusThickness = 1f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.30f, 0.30f);
            vel.y = new ParticleSystem.MinMaxCurve(0.85f, 1.70f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ApplyFireGradient(ps, peakAlpha: 1f, hotBias: 0.35f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.70f, 0.85f), new Keyframe(1f, 0f)));

            var ns = ps.noise;
            ns.enabled = true;
            ns.separateAxes = true;
            ns.strengthX = noise;
            ns.strengthY = 0.06f;
            ns.strengthZ = 0f;
            ns.frequency = 1.1f;
            ns.damping = true;

            ConfigureFireRenderer(ps, ParticleTextureLibrary.Get(ParticleTextureShape.Spark, 0.35f),
                order: 14, additive: true);
            ps.Play();
            return ps;
        }

        /// <summary>
        /// Smoke. ALPHA, not additive, and this is not a tidy-up that could be folded into the
        /// shared additive material: a dark puff on an additive surface adds almost nothing and
        /// the layer would vanish with nothing failing. Same rule the vortex's ground debris
        /// and KiAuraFX's ground debris record — one non-additive layer is what separates
        /// "affecting the world" from "lit".
        /// </summary>
        private ParticleSystem BuildSmoke()
        {
            var ps = NewSystem("Smoke");
            float scaledRate = SmokeRate * DensityScale;

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = SmokeLifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(_radius * 0.26f, _radius * 0.48f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.26f, 0.10f, 0.44f, 0.30f), new Color(0.42f, 0.18f, 0.66f, 0.22f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = Mathf.CeilToInt(scaledRate * SmokeLifetime * 2f) + 8;

            var emission = ps.emission;
            emission.rateOverTime = scaledRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = _radius * 0.55f;
            shape.radiusThickness = 1f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.14f, 0.14f);
            vel.y = new ParticleSystem.MinMaxCurve(0.35f, 0.70f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // White through the gradient: the hue is already in startColor here, because smoke
            // is the one layer whose colour is a MASS rather than a temperature.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.24f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.45f), new Keyframe(1f, 1.35f)));

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            ConfigureFireRenderer(ps, ParticleTextureLibrary.Get(ParticleTextureShape.Smoke, 0.9f),
                order: 9, additive: false);
            ps.Play();
            return ps;
        }

        private ParticleSystem NewSystem(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent starts the system immediately (playOnAwake defaults true) and Unity
            // REFUSES main.duration on a playing system — it logs "Setting the duration while
            // system is still playing is not supported" and silently keeps the old value.
            // Stop -> configure -> Play, in that order, always.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// <summary>
        /// <c>PP_torch_flame</c>'s FOUR-STOP ramp with the hue moved onto this spell's palette:
        /// near-white, the palette's core, its glow, then a dark tail. Four stops is what makes
        /// a flame look like it is COOLING as it rises — the torch goes white, amber, deep
        /// orange, near-black, and dropping either middle stop turns the fire into a coloured
        /// smear that fades out.
        ///
        /// <para><paramref name="hotBias"/> starts the ramp further toward the hot core for the
        /// layers meant to be hottest — the roots and the embers — without giving them a second,
        /// separately authored set of colours that could drift away from the body's.</para>
        /// </summary>
        private void ApplyFireGradient(ParticleSystem ps, float peakAlpha, float hotBias = 0f)
        {
            Color white = new Color(1f, 0.97f, 1f);
            Color hot  = Color.Lerp(_palette.hotCore, white, Mathf.Clamp01(0.30f + hotBias));
            Color mid  = _palette.core;
            Color cool = _palette.glow;
            // The dark tail, deepened rather than multiplied toward black: a plain multiply
            // desaturates, and the last thing a violet flame should turn is grey.
            Color ash  = new Color(cool.r * 0.30f, cool.g * 0.14f, cool.b * 0.38f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(hot,  0f),
                    new GradientColorKey(mid,  0.30f),
                    new GradientColorKey(cool, 0.70f),
                    new GradientColorKey(ash,  1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peakAlpha, 0.10f),
                    new GradientAlphaKey(peakAlpha * 0.75f, 0.50f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;
        }

        private static void ConfigureFireRenderer(ParticleSystem ps, Texture texture, int order, bool additive)
        {
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sharedMaterial = ParticleMaterialCache.Get(texture, additive);
            psr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            psr.sortingLayerName = SortingConfig.LAYER_VFX;
            psr.sortingOrder = order;
        }
    }
}
