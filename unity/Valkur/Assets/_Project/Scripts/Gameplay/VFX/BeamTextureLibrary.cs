using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Shapes a beam texture can take. Unlike <see cref="ParticleTextureShape"/> these are
    /// bands, not billboards: alpha varies across the beam's width and tiles seamlessly
    /// along its length, which is what a <see cref="LineRenderer"/> needs.
    /// </summary>
    public enum BeamTextureKind
    {
        /// <summary>Tight white-hot line. The thing the eye reads as "the beam".</summary>
        Core = 0,

        /// <summary>Wide soft halo. Gives the beam its colour and its presence.</summary>
        Glow = 1,

        /// <summary>Core modulated along its length, so scrolling it reads as energy flowing.</summary>
        Energy = 2,

        /// <summary>
        /// A self-contained charge: bright head, streak trailing behind it, and zero at BOTH
        /// ends of the U axis. Meant for <see cref="UnityEngine.LineTextureMode.Stretch"/> on
        /// a short line whose endpoints slide along the beam, so one copy spans the packet and
        /// its ends fade out instead of being cut off.
        /// </summary>
        Packet = 3,
    }

    /// <summary>
    /// Procedural band textures for beam <see cref="LineRenderer"/>s.
    ///
    /// A LineRenderer with no texture is a hard-edged rectangle: constant alpha right up to
    /// the edge, where it stops. That is why the laser read as a coloured bar rather than as
    /// light — no falloff across its width, and nothing to scroll along its length.
    ///
    /// These are generated once at runtime, cached by (kind, quantised softness), marked
    /// <see cref="HideFlags.DontSave"/>, and wrapped <see cref="TextureWrapMode.Repeat"/> so
    /// the material can tile and scroll them. Same contract as
    /// <see cref="ParticleTextureLibrary"/>: no art assets, no atlas entries, no Resources
    /// footprint, and a SubsystemRegistration reset because Domain Reload is off.
    /// </summary>
    public static class BeamTextureLibrary
    {
        /// <summary>Along the beam. Only Energy varies here, but the others still tile.</summary>
        private const int LENGTH = 64;

        /// <summary>Across the beam. This is the axis the falloff lives on.</summary>
        private const int WIDTH = 32;

        private const int SOFTNESS_STEPS = 8;

        private static readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _cache.Clear();

        /// <summary>
        /// The cached texture for a kind, generating it on first request.
        /// </summary>
        public static Texture2D Get(BeamTextureKind kind, float softness)
        {
            int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(softness) * SOFTNESS_STEPS), 0, SOFTNESS_STEPS);
            int key = ((int)kind * (SOFTNESS_STEPS + 1)) + step;

            // Unity's overloaded null also catches a texture destroyed by a play-mode exit.
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var tex = Generate(kind, (float)step / SOFTNESS_STEPS);
            _cache[key] = tex;
            return tex;
        }

        private static Texture2D Generate(BeamTextureKind kind, float softness)
        {
            var tex = new Texture2D(LENGTH, WIDTH, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = $"BeamTex_{kind}_{softness:0.00}",
                hideFlags = HideFlags.DontSave,
                // Repeat along the length is what makes tiling and scrolling possible at all.
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };

            var pixels = new Color32[LENGTH * WIDTH];
            for (int y = 0; y < WIDTH; y++)
            {
                // -1 at one edge, 0 at the centre line, +1 at the other.
                float across = ((y / (float)(WIDTH - 1)) * 2f) - 1f;

                for (int x = 0; x < LENGTH; x++)
                {
                    float along = x / (float)LENGTH;   // 0..1, wraps seamlessly
                    float a = Mathf.Clamp01(EvaluateAlpha(kind, across, along, softness));
                    pixels[(y * LENGTH) + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>
        /// Alpha at a point on the band. <paramref name="across"/> is -1..1 edge to edge,
        /// <paramref name="along"/> is 0..1 and wraps. Public so tests and tooling can assert
        /// the falloff without a GPU read-back — generated textures are upload-only.
        /// </summary>
        public static float EvaluateAlpha(BeamTextureKind kind, float across, float along, float softness)
        {
            float d = Mathf.Abs(across);
            if (d >= 1f) return 0f;

            switch (kind)
            {
                case BeamTextureKind.Core:
                    // Steep: the core should be a hard bright line with just enough edge to
                    // avoid aliasing against the glow behind it.
                    return Mathf.Pow(1f - d, Mathf.Lerp(6f, 2f, softness));

                case BeamTextureKind.Glow:
                    // Shallow, and never quite reaching 1 — the glow is the halo around the
                    // core, not a second core.
                    return Mathf.Pow(1f - d, Mathf.Lerp(3f, 1.1f, softness)) * 0.85f;

                case BeamTextureKind.Energy:
                {
                    float band = Mathf.Pow(1f - d, Mathf.Lerp(5f, 2f, softness));
                    // Two sine waves at coprime frequencies so the pattern does not visibly
                    // repeat every tile, and both complete a whole number of cycles across
                    // the texture so it still wraps seamlessly.
                    float pulse = 0.72f
                                + 0.18f * Mathf.Sin(along * Mathf.PI * 2f * 3f)
                                + 0.10f * Mathf.Sin(along * Mathf.PI * 2f * 7f);
                    return band * pulse;
                }

                case BeamTextureKind.Packet:
                {
                    float band = Mathf.Pow(1f - d, Mathf.Lerp(5f, 2f, softness));

                    // U runs 0 (trailing end) to 1 (leading end) across the packet, ONCE.
                    // Nothing tiles and nothing scrolls here — this shape is stationary in
                    // texture space and the geometry is what moves. The first attempt did the
                    // opposite (scroll the UVs of a full-length line) and rendered completely
                    // static, because URP's particle shaders sample UV0 raw: they never apply
                    // _BaseMap_ST, and they have no _MainTex at all.
                    const float HEAD = 0.82f;

                    // Head: tight and bright, the leading edge of the charge.
                    const float HEAD_SIGMA = 0.075f;
                    float dh = along - HEAD;
                    float head = Mathf.Exp(-(dh * dh) / (2f * HEAD_SIGMA * HEAD_SIGMA));

                    // Tail: behind the head only. The asymmetry is what encodes a direction of
                    // travel — a symmetric blob reads as a throb, not as something moving.
                    const float TAIL_LENGTH = 0.30f;
                    float tail = along < HEAD ? Mathf.Exp((along - HEAD) / TAIL_LENGTH) * 0.6f : 0f;  // TAIL_WEIGHT

                    // Both ends must reach zero — this line has hard geometric ends, and
                    // residual alpha there is a cut edge travelling along the beam. Confined to
                    // the outer tenth: a sin^2 across the whole length also crushes the head,
                    // which sits at 0.82 and would keep only 29% of its brightness.
                    const float END_FADE = 0.10f;
                    float endFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(along / END_FADE))
                                  * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - along) / END_FADE));

                    // Normalised rather than clamped. Clamping flattens the top, where head and
                    // tail overlap, and a flat top erases the very asymmetry that tells the eye
                    // which way the charge is going.
                    const float TAIL_WEIGHT = 0.6f;
                    float shape = (head + tail) / (1f + TAIL_WEIGHT);

                    return band * Mathf.Clamp01(shape) * endFade;
                }

                default:
                    return 0f;
            }
        }
    }
}
