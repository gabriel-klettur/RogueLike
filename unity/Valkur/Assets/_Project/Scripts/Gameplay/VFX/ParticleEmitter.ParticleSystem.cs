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
            bool isBurst = IsSingleBurst(kind);
            bool isBurstLoop = IsBurstWithInterval(kind);
            bool isContinuous = !isBurst && !isBurstLoop;

            float lifeSec = Mathf.Max(0.05f, p.lifespan);

            // ---- Main ----
            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = isContinuous;
            main.stopAction = isBurst ? ParticleSystemStopAction.Disable : ParticleSystemStopAction.None;
            main.startLifetime = lifeSec;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, p.speed * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(p.sizeMin * scale, p.sizeMax * scale);
            main.startColor = BuildColorParameter(p);
            // Gravity: Python gravity > 0 means falling down (Y inverted → positive in Unity)
            main.gravityModifier = p.gravity > 0f ? p.gravity / UNITY_GRAVITY : 0f;
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
            sol.enabled = isBurst;
            if (isBurst)
            {
                // Expand then shrink for impact/explosion feel
                var curve = new AnimationCurve(
                    new Keyframe(0f, 0.3f),
                    new Keyframe(0.3f, 1.0f),
                    new Keyframe(1.0f, 0f));
                sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
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
                    shape.radius = 0.15f * scale;
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

                default:
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.15f * scale;
                    break;
            }
        }

        private void ConfigureRenderer(ParticleVfxParams p)
        {
            var renderer = _ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;

            renderer.sortingLayerName = "VFX";
            renderer.sortingOrder = 0;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            // Choose material based on blend mode
            if (p.additive)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                var mat = new Material(shader);
                // Additive blend: src = SrcAlpha, dst = One
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_EMISSION");
                renderer.material = mat;
            }
            else
            {
                // Alpha blend — use URP Particles/Unlit if available, fallback to Sprites-Default
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                renderer.material = new Material(shader);
            }
        }

        // ------------------------------------------------------------------ burst loop coroutine

        private IEnumerator BurstLoop(float intervalSec)
        {
            while (true)
            {
                _ps.Play();
                yield return new WaitForSeconds(intervalSec);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsSingleBurst(string kind)
        {
            return kind is "explosion" or "smoke_burst" or "slash" or "firework";
        }

        private static bool IsBurstWithInterval(string kind)
        {
            // Emitters that burst periodically when placed in the world
            // (as opposed to continuous-rate emitters)
            return false; // extend if needed
        }

        private ParticleSystem.MinMaxGradient BuildColorParameter(ParticleVfxParams p)
        {
            var cols = (p.colors != null && p.colors.Length > 0) ? p.colors : null;
            if (cols == null)
                return new ParticleSystem.MinMaxGradient(p.color);

            if (cols.Length == 1)
                return new ParticleSystem.MinMaxGradient(cols[0]);

            // Two-colour random: Unity picks between min and max color per particle
            return new ParticleSystem.MinMaxGradient(cols[0], cols[cols.Length - 1]);
        }

        private ParticleSystem.MinMaxGradient BuildFadeOutGradient(ParticleVfxParams p)
        {
            var cols = (p.colors != null && p.colors.Length > 0) ? p.colors : null;
            Color baseColor = (cols != null && cols.Length > 0) ? cols[0] : p.color;

            var gradient = new Gradient();
            int n = (cols != null) ? Mathf.Min(cols.Length, 8) : 1;
            var colorKeys = new GradientColorKey[n];
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0f : (float)i / (n - 1);
                colorKeys[i] = new GradientColorKey(cols != null ? cols[i] : baseColor, t);
            }
            gradient.SetKeys(colorKeys, new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.5f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
            return new ParticleSystem.MinMaxGradient(gradient);
        }
    }
}
