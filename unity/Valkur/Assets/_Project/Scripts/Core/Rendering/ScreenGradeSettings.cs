using UnityEngine;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// The live values <see cref="ScreenGradeFeature"/> pushes into its materials each frame.
    ///
    /// A static hand-off rather than a direct reference because of the assembly wall: the renderer
    /// feature has to live in <c>Valkur.Core</c> (it is referenced by a renderer asset, and Core is
    /// the only assembly URP-adjacent code may depend on downward), while the thing that decides
    /// what the grade should be — the day/night cycle — lives in <c>Valkur.Gameplay</c>. Gameplay
    /// may reference Core; Core may not reference Gameplay. So Gameplay writes and Core reads.
    ///
    /// Inert by default: with <see cref="Enabled"/> false the grade pass is not enqueued, and with
    /// <see cref="BloomEnabled"/> false neither is the bloom, so a scene with no day/night cycle —
    /// the main menu, whose title plate is contrast-measured against the raw frame — pays nothing
    /// and is graded by nothing.
    /// </summary>
    public static class ScreenGradeSettings
    {
        /// <summary>Domain Reload is OFF — a value left over from the previous Play session would
        /// grade the first frames of the next one before anything wrote to it.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            FeaturePresent   = false;
            Enabled          = false;
            Saturation       = 1f;
            Contrast         = 1f;
            VignetteIntensity = 0f;
            VignetteSmoothness = 1f;
            VignetteColor    = Color.black;
            DitherStrength   = 1f / 255f;
            Lift             = Vector3.zero;
            InverseGamma     = Vector3.one;
            Gain             = Vector3.one;

            BloomEnabled   = false;
            BloomIntensity = DefaultBloomIntensity;
            BloomThreshold = DefaultBloomThreshold;
            BloomSoftKnee  = DefaultBloomSoftKnee;
            BloomTint      = Color.white;
        }

        /// <summary>When false the grade pass is not enqueued at all.</summary>
        public static bool Enabled { get; set; }

        /// <summary>
        /// Set by <see cref="ScreenGradeFeature"/> whenever it is reached, i.e. the renderer asset
        /// actually carries it. The uGUI vignette reads this to stand down: with both drawing, the
        /// screen edges would be darkened twice.
        /// </summary>
        public static bool FeaturePresent { get; set; }

        /// <summary>1 = untouched. Below 1 drains colour; night wants roughly 0.75.</summary>
        public static float Saturation { get; set; } = 1f;

        /// <summary>1 = untouched. Applied in LogC around ACEScc mid-grey.</summary>
        public static float Contrast { get; set; } = 1f;

        /// <summary>0 = no vignette. Screen-edge falloff strength.</summary>
        public static float VignetteIntensity { get; set; }

        /// <summary>Falloff exponent; higher is a tighter, harder edge.</summary>
        public static float VignetteSmoothness { get; set; } = 1f;

        /// <summary>What the edges are tinted toward.</summary>
        public static Color VignetteColor { get; set; } = Color.black;

        /// <summary>Ordered-dither amplitude, in linear units. One 8-bit step is 1/255.</summary>
        public static float DitherStrength { get; set; } = 1f / 255f;

        /// <summary>Lift / gamma / gain, already through <c>ColorUtils.PrepareLiftGammaGain</c>.</summary>
        public static Vector3 Lift { get; set; } = Vector3.zero;

        /// <inheritdoc cref="Lift"/>
        public static Vector3 InverseGamma { get; set; } = Vector3.one;

        /// <inheritdoc cref="Lift"/>
        public static Vector3 Gain { get; set; } = Vector3.one;

        /// <summary>
        /// True when the current values would visibly change the frame. The feature skips the blit
        /// when they would not — a neutral grade is two full-screen passes for an identical image.
        /// </summary>
        public static bool WouldChangeTheFrame =>
            Enabled &&
            (!Mathf.Approximately(Saturation, 1f) ||
             !Mathf.Approximately(Contrast,   1f) ||
             VignetteIntensity > 0.001f ||
             DitherStrength    > 0.0001f ||
             Lift  != Vector3.zero ||
             Gain  != Vector3.one  ||
             InverseGamma != Vector3.one);

        // ── Bloom ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The intensity the game ships at. Measured on the shipped art: at 0.5 a torch's halo
        /// reads two tiles wide and a fireball's core turns the wall behind it orange; at 0.2
        /// nothing on screen says the layer exists. 0.35 is where an additive VFX blossoms and
        /// a lit stone wall stays a stone wall.
        /// </summary>
        public const float DefaultBloomIntensity = 0.35f;

        /// <summary>
        /// Linear luminance above which a pixel feeds the bloom. Exactly 1.0 on purpose: no texel
        /// of pixel art can reach it under a 1.0 ambient, so only additive light — which the HDR
        /// buffer keeps above white — ever blooms. Lowering it makes pale ground glow.
        /// </summary>
        public const float DefaultBloomThreshold = 1.0f;

        /// <summary>Softness of the threshold, in linear units. URP's own curve.</summary>
        public const float DefaultBloomSoftKnee = 0.5f;

        /// <summary>When false the bloom pass is not enqueued at all.</summary>
        public static bool BloomEnabled { get; set; }

        /// <summary>Multiplier on the finished pyramid. 0 disables the pass.</summary>
        public static float BloomIntensity { get; set; } = DefaultBloomIntensity;

        /// <summary>See <see cref="DefaultBloomThreshold"/>.</summary>
        public static float BloomThreshold { get; set; } = DefaultBloomThreshold;

        /// <summary>See <see cref="DefaultBloomSoftKnee"/>.</summary>
        public static float BloomSoftKnee { get; set; } = DefaultBloomSoftKnee;

        /// <summary>
        /// Multiplied into the bloom at composite time. The cycle leans it warm by day and cool
        /// by night so a torch's halo belongs to the hour it burns in.
        /// </summary>
        public static Color BloomTint { get; set; } = Color.white;

        /// <summary>True when the bloom would visibly change the frame.</summary>
        public static bool BloomWouldChangeTheFrame => BloomEnabled && BloomIntensity > 0.001f;
    }
}
