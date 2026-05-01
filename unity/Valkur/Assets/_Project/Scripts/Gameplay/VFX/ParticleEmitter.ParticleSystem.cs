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
            _ps = GetComponentInChildren<ParticleSystem>();
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
            if (p.drag > 0f)
            {
                var vlim = _ps.limitVelocityOverLifetime;
                vlim.enabled = true;
                vlim.dampen = Mathf.Clamp01(p.drag);
                vlim.limit = new ParticleSystem.MinMaxCurve(p.speed * scale);
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