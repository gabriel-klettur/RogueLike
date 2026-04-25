using System.Collections;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticleEmitter
    {

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

        private static AnimationCurve BuildAnimationCurve(Keyframe2D[] keys)
        {
            var keyframes = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                keyframes[i] = new Keyframe(keys[i].time, keys[i].value);
            return new AnimationCurve(keyframes);
        }

        private ParticleSystem.MinMaxGradient BuildGradientFromCurves(ParticleVfxParams p)
        {
            var gradient = new Gradient();

            // Colour keys: from colorOverLife if present, else from colors array/single color
            GradientColorKey[] colorKeys;
            if (p.colorOverLife != null && p.colorOverLife.Length > 0)
            {
                int n = Mathf.Min(p.colorOverLife.Length, 8);
                colorKeys = new GradientColorKey[n];
                for (int i = 0; i < n; i++)
                    colorKeys[i] = new GradientColorKey(p.colorOverLife[i].color, p.colorOverLife[i].time);
            }
            else
            {
                var cols = (p.colors != null && p.colors.Length > 0) ? p.colors : null;
                Color baseColor = (cols != null) ? cols[0] : p.color;
                colorKeys = new[] { new GradientColorKey(baseColor, 0f), new GradientColorKey(baseColor, 1f) };
            }

            // Alpha keys: from alphaOverLife
            int an = Mathf.Min(p.alphaOverLife.Length, 8);
            var alphaKeys = new GradientAlphaKey[an];
            for (int i = 0; i < an; i++)
                alphaKeys[i] = new GradientAlphaKey(p.alphaOverLife[i].value, p.alphaOverLife[i].time);

            gradient.SetKeys(colorKeys, alphaKeys);
            return new ParticleSystem.MinMaxGradient(gradient);
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