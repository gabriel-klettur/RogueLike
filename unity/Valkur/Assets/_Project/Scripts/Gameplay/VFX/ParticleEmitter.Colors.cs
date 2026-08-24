using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticleEmitter
    {

        private void ConfigureRenderer(ParticleSystem ps, ParticleVfxParams p)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;

            // Authored depth, with the values this method hard-coded for years as the
            // defaults: an unset layer is "VFX", order 0, fudge 0. All three are written
            // unconditionally like every module in ConfigureParticleSystem — emitters are
            // reused across presets (the F1 preview emitter serves every one of them), so a
            // layer or a fudge one preset sets has to be cleared by the next one rather than
            // silently inherited.
            renderer.sortingLayerName = ResolveSortingLayerName(p.sortingLayer);
            renderer.sortingOrder = p.sortingOrder;
            renderer.sortingFudge = p.sortingFudge;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            // Texture, in priority order:
            //   1. a flipbook — Texture Sheet Animation writes per-particle UVs into this
            //      texture, so the material must sample the atlas page the frames live on;
            //   2. a hand-authored single sprite;
            //   3. the procedural shape (Auto derives it from kind + blend mode).
            // Null = the legacy untextured quad.
            Texture texture = ResolveFlipbookTexture(p);
            if (texture == null)
                texture = p.customSprite != null
                    ? p.customSprite.texture
                    : ParticleTextureLibrary.Get(
                        ParticleTextureLibrary.ResolveShape(p.textureShape, p.kind, p.additive),
                        p.textureSoftness);

            // sharedMaterial — never .material: the cache exists so emitters batch and so
            // EditMode tests stop leaking per-renderer material instances.
            renderer.sharedMaterial = ParticleMaterialCache.Get(texture, p.additive);
        }

        // ------------------------------------------------------------------ sorting

        /// <summary>
        /// Authored sorting-layer name → the effective name assigned. Static so a typo in one
        /// preset costs ONE warning line for the session instead of one per emitter: the
        /// vegetation pass places ~150 emitters off a handful of presets, and this codebase
        /// treats a warning that repeats for a steady state as a bug in the warning. It also
        /// spares us re-scanning <see cref="SortingLayer.layers"/> (which allocates a fresh
        /// array on every read) once per configured system.
        /// </summary>
        private static readonly Dictionary<string, string> _sortingLayerVerdicts =
            new Dictionary<string, string>();

        // Domain Reload is OFF, so without this the verdicts — and the "already warned"
        // state they carry — would survive into the next Play session. Clearing here also
        // means a sorting layer ADDED to ProjectSettings mid-session is re-evaluated on the
        // next Play rather than staying cached as missing forever.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSortingLayerVerdicts() => _sortingLayerVerdicts.Clear();

        /// <summary>
        /// Effective sorting-layer name for an authored value. Empty falls back to
        /// <see cref="SortingConfig.LAYER_VFX"/>, which is what this emitter hard-coded for
        /// every system it ever built; a name that does not exist in ProjectSettings > Tags
        /// and Layers falls back to it as well, because the alternative is worse than the
        /// typo — assigning an unknown name to <c>sortingLayerName</c> throws, and resolving
        /// it through the ID path would land the emitter on "Default", i.e. behind the entire
        /// world, which looks like the emitter simply failed to spawn.
        ///
        /// Validated against the <see cref="SortingLayer.layers"/> list rather than
        /// <see cref="SortingLayer.NameToID"/>: NameToID answers 0 both for an unknown name
        /// and for the real "Default" layer, so it cannot tell a typo from a deliberate
        /// choice.
        /// </summary>
        private static string ResolveSortingLayerName(string authored)
        {
            if (string.IsNullOrEmpty(authored)) return SortingConfig.LAYER_VFX;
            if (_sortingLayerVerdicts.TryGetValue(authored, out string cached)) return cached;

            bool exists = false;
            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name == authored) { exists = true; break; }
            }

            string resolved = exists ? authored : SortingConfig.LAYER_VFX;
            _sortingLayerVerdicts[authored] = resolved;
            if (!exists)
            {
                Debug.LogWarning(
                    $"[ParticleEmitter] Sorting layer '{authored}' does not exist in " +
                    $"ProjectSettings > Tags and Layers — falling back to " +
                    $"'{SortingConfig.LAYER_VFX}'. Fix the preset's Sorting Layer field, or " +
                    "add the layer. (Reported once per name per session.)");
            }
            return resolved;
        }

        /// <summary>
        /// The atlas page the flipbook frames were packed onto, or null when the preset has
        /// no flipbook. Takes it from the first non-null frame: Texture Sheet Animation in
        /// Sprites mode requires every frame to share one texture, so any of them answers.
        /// </summary>
        private static Texture ResolveFlipbookTexture(ParticleVfxParams p)
        {
            if (p.flipbookFrames == null) return null;
            for (int i = 0; i < p.flipbookFrames.Length; i++)
                if (p.flipbookFrames[i] != null) return p.flipbookFrames[i].texture;
            return null;
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

        private static bool IsBurstWithInterval(string kind)
        {
            // Emitters that burst periodically when placed in the world
            // (as opposed to continuous-rate emitters).
            return false; // extend if needed
        }

        private ParticleSystem.MinMaxGradient BuildColorParameter(ParticleVfxParams p)
        {
            // Intensity rides the START colour so it propagates for free: colourOverLifetime
            // MULTIPLIES the start colour, so overdriving here brightens the whole life.
            // RGB only — scaling alpha would change coverage, not brightness.
            float k = p.colorIntensity > 0f ? p.colorIntensity : 1f;
            // The day/night ambient rides the same channel for the same reason, and this is
            // the single point where it composes with BOTH gradient builders. Exactly white
            // for every preset that does not set respondsToAmbientLight, so the multiply is
            // the identity and today's rendering is bit-for-bit unchanged. See
            // ParticleEmitter.AmbientLight.cs — the tracking loop re-enters here as the
            // cycle advances, which is why the tint must not be baked anywhere else.
            Color amb = AmbientTint(p);
            float kr = k * amb.r, kg = k * amb.g, kb = k * amb.b;
            Color Tint(Color c) => new Color(c.r * kr, c.g * kg, c.b * kb, c.a);

            var cols = (p.colors != null && p.colors.Length > 0) ? p.colors : null;
            if (cols == null)
                return new ParticleSystem.MinMaxGradient(Tint(p.color));

            if (cols.Length == 1)
                return new ParticleSystem.MinMaxGradient(Tint(cols[0]));

            // Two-colour random: Unity picks between min and max color per particle
            return new ParticleSystem.MinMaxGradient(Tint(cols[0]), Tint(cols[cols.Length - 1]));
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