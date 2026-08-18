using System.Collections;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticleEmitter
    {
        // ------------------------------------------------------------------ particle system

        private void EnsureParticleSystem()
        {
            if (_ps != null) return;
            // includeInactive: a burst whose stopAction disabled the child would
            // otherwise look absent here, and we would build a second one beside it.
            _ps = GetComponentInChildren<ParticleSystem>(true);
            if (_ps == null)
            {
                var child = new GameObject("Particles");
                child.transform.SetParent(transform, false);
                _ps = child.AddComponent<ParticleSystem>();
                // Stop auto-play until fully configured
                var childMain = _ps.main;
                childMain.playOnAwake = false;
            }
        }

        private void ConfigureParticleSystem(ParticleVfxParams p, float scale)
        {
            string kind = p.kind ?? "";
            // Use the explicit loops attribute as the single source of truth.
            // loops=false → finite one-shot burst (explosion, smoke_burst, slash, firework by default).
            // loops=true  → continuous emitter that never self-disables.
            bool isBurst = !p.loops;
            bool isBurstLoop = IsBurstWithInterval(kind);

            float lifeSec = Mathf.Max(0.05f, p.lifespan);

            // ---- Main ----
            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = p.loops;
            main.stopAction = isBurst
                ? ParticleSystemStopAction.Disable
                : ParticleSystemStopAction.None;
            main.startLifetime = lifeSec;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, p.speed * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(p.sizeMin * scale, p.sizeMax * scale);
            main.startColor = BuildColorParameter(p);
            // Gravity: scalar gravity means falling down; vector gravity is pre-converted
            if (p.useGravityVector)
            {
                // Use velocity-over-lifetime for arbitrary gravity direction
                main.gravityModifier = 0f;
                var vel = _ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(p.gravityVector.x);
                vel.y = new ParticleSystem.MinMaxCurve(p.gravityVector.y);
            }
            else
            {
                main.gravityModifier = p.gravity > 0f ? p.gravity / UNITY_GRAVITY : 0f;
                var vel = _ps.velocityOverLifetime;
                vel.enabled = false;
            }
            main.simulationSpace = kind is "dash" ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;

            // ---- Emission ----
            var emission = _ps.emission;
            if (isBurst || isBurstLoop)
            {
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)p.count) });
            }
            else
            {
                emission.rateOverTime = Mathf.Max(1f, p.emitRate);
                // A previously applied burst preset leaves its Burst in the list, and the
                // list is independent of rateOverTime — the old burst would keep firing on
                // top of the new continuous emitter at every duration boundary.
                emission.burstCount = 0;
            }

            // ---- Shape ----
            ConfigureShape(p, scale);

            // ---- Size Over Lifetime ----
            var sol = _ps.sizeOverLifetime;
            if (p.sizeOverLife != null && p.sizeOverLife.Length > 0)
            {
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, BuildAnimationCurve(p.sizeOverLife));
            }
            else if (isBurst)
            {
                sol.enabled = true;
                // Expand then shrink for impact/explosion feel
                var curve = new AnimationCurve(
                    new Keyframe(0f, 0.3f),
                    new Keyframe(0.3f, 1.0f),
                    new Keyframe(1.0f, 0f));
                sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
            }
            else
            {
                sol.enabled = false;
            }

            // ---- Velocity Damping (drag) ----
            // Fetched unconditionally: emitters are reused across presets (the editor's
            // preview emitter is), so a module one preset turns on has to be turned off
            // by the next one or its drag silently clamps every effect chosen afterwards.
            var vlim = _ps.limitVelocityOverLifetime;
            if (p.drag > 0f)
            {
                vlim.enabled = true;
                vlim.separateAxes = false;
                vlim.dampen = Mathf.Clamp01(p.drag);
                vlim.limit = new ParticleSystem.MinMaxCurve(p.speed * scale);
            }
            else
            {
                vlim.enabled = false;
            }

            // ---- Noise (falling_leaf sway) ----
            var noise = _ps.noise;
            if (kind == "falling_leaf")
            {
                noise.enabled = true;
                noise.strength = new ParticleSystem.MinMaxCurve(p.swayAmp * scale);
                noise.frequency = p.swaySpeed;
                noise.damping = true;
                noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.2f);
            }
            else
            {
                noise.enabled = false;
            }

            // ---- Colour Over Lifetime ----
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            if (p.alphaOverLife != null && p.alphaOverLife.Length > 0)
                col.color = BuildGradientFromCurves(p);
            else
                col.color = BuildFadeOutGradient(p);

            // ---- Renderer ----
            ConfigureRenderer(p);
        }

        private void ConfigureShape(ParticleVfxParams p, float scale)
        {
            var shape = _ps.shape;
            shape.enabled = true;

            // Normalise to Unity's defaults before the switch. Each branch below sets only
            // the properties its own shape cares about, so on a reused emitter a cone's
            // rotation, a box's scale or an aura's edge-only radiusThickness would survive
            // into the next preset and deform it — a ring emitted flat on its side, an
            // explosion emitting from a shell instead of a volume. A freshly spawned
            // emitter never sees that, which is why it only ever showed up in the editor's
            // preview, where one emitter serves every preset.
            shape.position        = Vector3.zero;
            shape.rotation        = Vector3.zero;
            shape.scale           = Vector3.one;
            shape.radiusThickness = 1f;
            shape.angle           = 25f;

            switch (p.kind ?? "")
            {
                case "aura":
                case "healing_aura":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = p.radius * scale;
                    shape.radiusThickness = 0f;       // emit from edge
                    break;

                case "dash":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 0.1f * scale;
                    shape.radiusThickness = 1f;
                    break;

                case "slash":
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = p.arcRangeDegrees * 0.5f;
                    shape.radius = 0.2f * scale;
                    shape.radiusThickness = 1f;
                    shape.rotation = new Vector3(-90f, 0f, 0f); // face forward
                    break;

                case "explosion":
                case "smoke_burst":
                case "firework":
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.1f * scale;
                    break;

                case "smoke_emitter":
                case "smoke":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    // Use dispersion as radius if available (Python emission spread)
                    shape.radius = p.dispersion > 0f ? p.dispersion * scale : 0.15f * scale;
                    shape.radiusThickness = 1f;
                    break;

                case "arcane_flame":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 0.2f * scale;
                    shape.radiusThickness = 1f;
                    break;

                case "water_fountain":
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 15f;
                    shape.radius = 0.05f * scale;
                    shape.radiusThickness = 1f;
                    shape.rotation = new Vector3(-90f, 0f, 0f); // aim upward (Unity Y-up)
                    break;

                case "falling_leaf":
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(2f * scale, 0.1f, 0.1f);
                    break;

                case "water_flow":
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(3f * scale, 0.1f, 0.1f);
                    break;

                case "portal":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    float portalR = p.outerRadius > 0f ? p.outerRadius : p.radius;
                    shape.radius = portalR * scale;
                    shape.radiusThickness = 0f; // emit from edge
                    break;

                default:
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.15f * scale;
                    break;
            }
        }

    }
}