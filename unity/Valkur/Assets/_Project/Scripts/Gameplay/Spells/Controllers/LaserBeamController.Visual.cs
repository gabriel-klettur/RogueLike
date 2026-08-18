using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    public partial class LaserBeamController
    {
        private void BuildVisual(SpellContext ctx)
        {
            float width = DEFAULT_BEAM_WIDTH * (ctx.Spell.scale > 0 ? ctx.Spell.scale : 1f);

            _beamColor = ctx.Spell.particleColor != Color.clear && ctx.Spell.particleColor.a > 0
                ? ctx.Spell.particleColor
                : new Color(0f, 0.9f, 1f, 1f);

            bool lightningMode = IsLightningBeam(ctx.Spell);

            if (!lightningMode)
            {
                // Outer glow line (wider, soft alpha).
                _glowLine = BuildLine("LaserBeam_Glow", width * GLOW_WIDTH_MULT,
                    new Color(_beamColor.r, _beamColor.g, _beamColor.b, GLOW_ALPHA),
                    sortingOrder: 4, BeamTextureKind.Glow, GLOW_SOFTNESS);

                // Inner bright core line (narrower, full alpha, slightly washed-out toward white).
                Color coreCol = Color.Lerp(_beamColor, Color.white, 0.35f);
                coreCol.a = CORE_ALPHA;
                _coreLine = BuildLine("LaserBeam_Core", width * CORE_WIDTH_MULT,
                    coreCol, sortingOrder: 5, BeamTextureKind.Energy, CORE_SOFTNESS);

                // Remembered because RunBeam modulates width every frame and must not
                // compound its own output.
                _authoredGlowWidth = width * GLOW_WIDTH_MULT;
                _authoredCoreWidth = width * CORE_WIDTH_MULT;
            }
            else
            {
                // Lightning beam: bolt sprites emitted along the beam edge each
                // frame. Same gameplay as a regular laser, completely different
                // visual.
                _lightningGo = new GameObject("LaserBeam_Lightning");
                _lightningGo.transform.SetParent(transform, false);
                _lightningPS = BuildLightningEmitter(_lightningGo, _beamColor, width);
            }

            // Impact burst at the laser tip — continuous particles in laser color.
            _impactGo = new GameObject("LaserBeam_Impact");
            _impactGo.transform.SetParent(transform, false);
            _impactBurst = BuildImpactBurst(_impactGo, _beamColor, width);

            // Trail particles along the beam path — emit perpendicular drift to
            // sell the energy travelling through the line.
            _trailGo = new GameObject("LaserBeam_Trail");
            _trailGo.transform.SetParent(transform, false);
            _trailPS = BuildTrailParticles(_trailGo, _beamColor, width);

            // Muzzle emitter at the beam origin — looks like the fireball spawn
            // flash but persistent. Vibrates each frame via small position jitter
            // applied in RunBeam, plus a noise module on the ParticleSystem itself.
            _muzzleGo = new GameObject("LaserBeam_Muzzle");
            _muzzleGo.transform.SetParent(transform, false);
            _muzzlePS = BuildMuzzleEmitter(_muzzleGo, _beamColor, width);
            _muzzleBeamWidth = width;
        }

        /// <summary>
        /// Builds a continuous emitter that lives at the beam's visual origin and
        /// pumps small bright particles outward in a tight circle. Loops for the
        /// duration of the channel — caller is expected to call Stop() on
        /// <c>_fading = true</c> so existing particles fade out naturally.
        /// </summary>
        private static ParticleSystem BuildMuzzleEmitter(GameObject host, Color color, float beamWidth)
        {
            var ps = host.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(beamWidth * 1.0f, beamWidth * 2.4f);
            main.startColor = color;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = 90f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = beamWidth * 1.5f;
            shape.radiusThickness = 1f;
            shape.randomDirectionAmount = 1f;

            // Bright core → desaturate + fade alpha across lifetime.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(Color.Lerp(color, Color.white, 0.45f), 0f),
                    new GradientColorKey(color, 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

            // Noise module → vibrating energy feel without per-frame transform spam.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1.4f;
            noise.frequency = 1.6f;
            noise.scrollSpeed = 1.2f;

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // Above core (5) and glow (4) so the muzzle visually sits on top
                // of the line where the beam emerges.
                renderer.sortingLayerName = SortingConfig.LAYER_VFX;
                renderer.sortingOrder = 7;
                // Shared additive + textured, via the same cache the particles use. Was a
                // per-emitter alpha material with no texture and no teardown: the muzzle
                // could not glow, drew hard-edged squares, and leaked one Material per beam.
                renderer.sharedMaterial = ParticleMaterialCache.Get(
                    ParticleTextureLibrary.Get(ParticleTextureShape.Glow, 0.85f), additive: true);
            }

            ps.Play();
            return ps;
        }

        /// <summary>
        /// Builds an Edge-shape ParticleSystem that emits along the beam line.
        /// Each frame the controller orients/scales it to span origin → end.
        /// </summary>
        private static ParticleSystem BuildTrailParticles(GameObject host, Color color, float beamWidth)
        {
            var ps = host.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.35f;
            main.startSpeed = 0.6f;        // slow perpendicular drift
            main.startSize = beamWidth * 0.9f;
            main.startColor = color;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            var emission = ps.emission;
            emission.rateOverTime = 60f;   // density along the beam

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
            shape.radius = 0.5f;           // overwritten each frame to half-length
            shape.randomDirectionAmount = 1f; // random perpendicular spread

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.Lerp(color, Color.white, 0.3f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // Trail renders on the VFX layer alongside the beam line — beam now
                // emerges in FRONT of the caster (VISUAL_FORWARD_OFFSET) so we want
                // the trail particles on top of world geometry, not behind it.
                renderer.sortingLayerName = SortingConfig.LAYER_VFX;
                renderer.sortingOrder = 5;
                renderer.sharedMaterial = ParticleMaterialCache.Get(
                    ParticleTextureLibrary.Get(ParticleTextureShape.Spark, 0.4f), additive: true);
            }

            ps.Play();
            return ps;
        }

        private static bool IsLightningBeam(Valkur.Data.SpellDefinition spell)
        {
            return spell != null
                && !string.IsNullOrEmpty(spell.vfxPreset)
                && spell.vfxPreset == LIGHTNING_BEAM_PRESET;
        }

        /// <summary>
        /// Builds a ParticleSystem that emits short-lived zig-zag bolt sprites
        /// along an Edge shape spanning the beam's visible length. Used in
        /// lightning-mode in place of the LineRenderer pair. Each frame the
        /// controller positions / orients the host so the edge spans
        /// visualOrigin → visibleEnd.
        /// </summary>
        private static ParticleSystem BuildLightningEmitter(GameObject host, Color color, float beamWidth)
        {
            var ps = host.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
            main.startSpeed = 0f;             // bolts stay where they're spawned
            main.startSize = new ParticleSystem.MinMaxCurve(beamWidth * 1.5f, beamWidth * 4.0f);
            main.startColor = color;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.rateOverTime = 90f;     // dense enough to look like a continuous beam

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
            shape.radius = 0.5f;             // overwritten each frame to halfLength
            shape.randomDirectionAmount = 0f;

            // Color fade: bright core → tint → fade alpha.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(Color.Lerp(color, Color.white, 0.6f), 0f),
                    new GradientColorKey(color, 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(new Keyframe(0f, 1.2f), new Keyframe(1f, 0.6f)));

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sortingLayerName = SortingConfig.LAYER_VFX;
                renderer.sortingOrder = 6;
                // Use the procedural Bolt sprite from ElementalSprites for the
                // zig-zag look — matches the lightning_emitter preset.
                ElementalSprites.EnsureAll();
                var boltSprite = ElementalSprites.Bolt;
                if (boltSprite != null)
                {
                    renderer.sharedMaterial = ParticleMaterialCache.Get(
                        boltSprite.texture, additive: true);
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                }
            }

            ps.Play();
            return ps;
        }

        /// <summary>Builds a uniform-width LineRenderer (start/end widths equal) for the beam.</summary>
        private LineRenderer BuildLine(string name, float width, Color color, int sortingOrder,
                                       BeamTextureKind kind, float softness)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 6;       // rounded ends -> more "laser" look
            lr.numCornerVertices = 0;
            lr.alignment = LineAlignment.View;
            // Tile, not Stretch: the texture repeats along the beam at a fixed world size,
            // so a long beam shows more of the energy pattern instead of smearing one copy
            // of it. RunBeam sets the tiling from the visible length each frame.
            lr.textureMode = LineTextureMode.Tile;

            // Uniform thickness from origin to impact (no taper).
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;

            // Shared, additive, and textured. Previously this allocated an alpha-blended
            // Material per beam per line: alpha meant the beam occluded the world instead of
            // adding light to it, and with no texture the LineRenderer drew a hard-edged
            // rectangle with no falloff across its width.
            lr.sharedMaterial = BeamMaterialCache.Get(BeamTextureLibrary.Get(kind, softness));

            // Render in the VFX sorting layer so the beam sits ON TOP of the world
            // (tiles, walls, entities). The visual origin is pushed forward by
            // VISUAL_FORWARD_OFFSET in RunBeam so the beam emerges in front of the
            // caster rather than behind, matching the slash spawn convention.
            lr.sortingLayerName = SortingConfig.LAYER_VFX;
            lr.sortingOrder = sortingOrder;
            return lr;
        }

        /// <summary>
        /// Builds a small ParticleSystem that simulates the explosion happening at
        /// the laser's impact point. Particles spray outward in the laser's color.
        /// </summary>
        private static ParticleSystem BuildImpactBurst(GameObject host, Color color, float beamWidth)
        {
            var ps = host.AddComponent<ParticleSystem>();
            // A freshly-added ParticleSystem auto-plays (playOnAwake defaults to true).
            // Mutating `main.duration` while it's playing logs an error, so we stop and
            // clear it before configuring the main module, then Play() at the end.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.25f;
            main.startSpeed = 2.5f;
            main.startSize = beamWidth * 1.4f;
            main.startColor = color;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = 80f;

            // Cone, not Circle. RunBeam already rotates this host every frame so its local
            // +X faces back down the beam — but a Circle emits equally in all directions, so
            // that rotation produced no visible change at all and the spray looked the same
            // whichever way the beam pointed. A cone makes the sparks come OFF the surface,
            // back toward the caster, which is what sells the beam as hitting something.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = beamWidth * 0.5f;
            shape.radiusThickness = 1f;
            // Unity's cone emits along its local +Z; rotate it onto the host's +X so the
            // per-frame rotation in RunBeam actually aims it.
            shape.rotation = new Vector3(0f, 90f, 0f);

            // Fade-out via color over lifetime.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.Lerp(color, Color.white, 0.5f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.2f)
            );
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Renderer: additive-ish unlit material with same shader as beam.
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // Was the literal "VFX" rather than the constant every other renderer here uses.
                renderer.sortingLayerName = SortingConfig.LAYER_VFX;
                renderer.sortingOrder = 6;
                renderer.sharedMaterial = ParticleMaterialCache.Get(
                    ParticleTextureLibrary.Get(ParticleTextureShape.Glow, 0.55f), additive: true);
            }

            ps.Play();
            return ps;
        }
    }
}
