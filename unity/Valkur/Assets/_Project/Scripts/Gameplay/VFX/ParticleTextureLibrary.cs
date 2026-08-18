using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Builds and caches the procedural billboard textures used by <see cref="ParticleEmitter"/>.
    ///
    /// Valkur ships no particle sprite art: every shape in <see cref="ParticleTextureShape"/> is
    /// generated once at runtime into a small RGBA texture whose alpha channel carries the shape
    /// and whose RGB stays white, so the preset's own colour gradient does all the tinting.
    ///
    /// Textures are keyed by (shape, quantised softness) and marked <see cref="HideFlags.DontSave"/>
    /// so they never leak into a scene or an EditMode test fixture.
    /// </summary>
    public static class ParticleTextureLibrary
    {
        /// <summary>Edge length of every generated texture. 128 is ample for a billboard.</summary>
        private const int RESOLUTION = 128;

        /// <summary>Softness is quantised to this many steps before keying the cache.</summary>
        private const int SOFTNESS_STEPS = 16;

        private static readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();

        // Domain Reload is OFF — runtime-created textures are destroyed when leaving play mode,
        // which would leave the static cache holding dead references on the next Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _cache.Clear();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Resolves <see cref="ParticleTextureShape.Auto"/> into a concrete shape from the
        /// preset's <paramref name="kind"/> and blend mode. Non-Auto values pass through.
        /// </summary>
        public static ParticleTextureShape ResolveShape(ParticleTextureShape requested, string kind, bool additive)
        {
            if (requested != ParticleTextureShape.Auto) return requested;

            switch (kind ?? "")
            {
                case "smoke":
                case "smoke_emitter":
                case "smoke_burst":
                    return ParticleTextureShape.Smoke;

                case "slash":
                case "dash":
                case "firework":
                    return ParticleTextureShape.Spark;

                case "portal":
                    return additive ? ParticleTextureShape.Glow : ParticleTextureShape.SoftDot;

                case "aura":
                case "healing_aura":
                case "arcane_flame":
                    return ParticleTextureShape.Glow;

                case "falling_leaf":
                case "water_flow":
                case "water_fountain":
                    return ParticleTextureShape.SoftDot;

                default:
                    return additive ? ParticleTextureShape.Glow : ParticleTextureShape.SoftDot;
            }
        }

        /// <summary>
        /// Returns the cached texture for a shape, generating it on first request.
        /// Returns <c>null</c> for <see cref="ParticleTextureShape.None"/> (legacy untextured quad)
        /// and for <see cref="ParticleTextureShape.Auto"/> — resolve Auto first.
        /// </summary>
        public static Texture2D Get(ParticleTextureShape shape, float softness)
        {
            if (shape == ParticleTextureShape.None || shape == ParticleTextureShape.Auto)
                return null;

            int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(softness) * SOFTNESS_STEPS), 0, SOFTNESS_STEPS);
            int key = ((int)shape * (SOFTNESS_STEPS + 1)) + step;

            // Unity's overloaded null also catches a texture destroyed by a play-mode exit.
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var tex = Generate(shape, (float)step / SOFTNESS_STEPS);
            _cache[key] = tex;
            return tex;
        }

        // ------------------------------------------------------------------ generation

        private static Texture2D Generate(ParticleTextureShape shape, float softness)
        {
            var tex = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = $"ParticleTex_{shape}_{softness:0.00}",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };

            var pixels = new Color32[RESOLUTION * RESOLUTION];
            float inv = 2f / (RESOLUTION - 1);

            for (int y = 0; y < RESOLUTION; y++)
            {
                float ny = (y * inv) - 1f; // -1 .. 1
                for (int x = 0; x < RESOLUTION; x++)
                {
                    float nx = (x * inv) - 1f;
                    float alpha = Mathf.Clamp01(EvaluateAlpha(shape, nx, ny, softness));
                    pixels[(y * RESOLUTION) + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>
        /// Alpha of the shape at normalised coordinates in [-1, 1]. This is the pure shape
        /// function used to fill the texture; it is public so tests and tooling can assert
        /// the falloff without a GPU read-back (generated textures are upload-only).
        /// </summary>
        public static float EvaluateAlpha(ParticleTextureShape shape, float nx, float ny, float softness)
        {
            float r = Mathf.Sqrt((nx * nx) + (ny * ny));
            if (r >= 1f && shape != ParticleTextureShape.Star) return 0f;

            switch (shape)
            {
                case ParticleTextureShape.SoftDot:
                    return Mathf.Pow(1f - r, Mathf.Lerp(8f, 1.2f, softness));

                case ParticleTextureShape.Glow:
                {
                    float skirt = Mathf.Pow(1f - r, Mathf.Lerp(4f, 1f, softness));
                    float core = 1f - Mathf.SmoothStep(0f, 0.18f, r);
                    return skirt + (core * 0.9f);
                }

                case ParticleTextureShape.Spark:
                    return Mathf.Pow(1f - r, Mathf.Lerp(16f, 6f, softness));

                case ParticleTextureShape.Smoke:
                {
                    float mask = Mathf.Pow(1f - r, Mathf.Lerp(3f, 1.1f, softness));
                    float noise = FractalNoise(nx, ny);
                    return mask * Mathf.Lerp(0.55f, 1f, noise);
                }

                case ParticleTextureShape.Ring:
                {
                    const float RADIUS = 0.72f;
                    float width = Mathf.Lerp(0.06f, 0.28f, softness);
                    float d = (r - RADIUS) / width;
                    return Mathf.Exp(-d * d);
                }

                case ParticleTextureShape.Star:
                {
                    float core = Mathf.Pow(Mathf.Max(0f, 1f - r), Mathf.Lerp(10f, 4f, softness));
                    float armWidth = Mathf.Lerp(0.015f, 0.07f, softness);
                    float horizontal = Streak(ny, nx, armWidth);
                    float vertical = Streak(nx, ny, armWidth);
                    return core + (horizontal + vertical) * 0.85f;
                }

                default:
                    return 0f;
            }
        }

        /// <summary>One anamorphic flare arm: thin across <paramref name="across"/>, fading along <paramref name="along"/>.</summary>
        private static float Streak(float across, float along, float width)
        {
            float t = across / width;
            float thin = Mathf.Exp(-t * t);
            float fade = Mathf.Max(0f, 1f - Mathf.Abs(along));
            return thin * fade * fade;
        }

        // ------------------------------------------------------------------ deterministic noise

        /// <summary>Three-octave value noise in 0..1. Deterministic — no <c>Random</c>, so textures are reproducible.</summary>
        private static float FractalNoise(float x, float y)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float frequency = 3f;

            for (int octave = 0; octave < 3; octave++)
            {
                sum += ValueNoise(x * frequency, y * frequency) * amplitude;
                frequency *= 2.1f;
                amplitude *= 0.5f;
            }
            return Mathf.Clamp01(sum / 0.875f);
        }

        private static float ValueNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;

            // Smoothstep interpolation weights
            float u = xf * xf * (3f - (2f * xf));
            float v = yf * yf * (3f - (2f * yf));

            float a = Hash(xi, yi);
            float b = Hash(xi + 1, yi);
            float c = Hash(xi, yi + 1);
            float d = Hash(xi + 1, yi + 1);

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFF) / (float)0x7FFFFFF;
            }
        }
    }
}
