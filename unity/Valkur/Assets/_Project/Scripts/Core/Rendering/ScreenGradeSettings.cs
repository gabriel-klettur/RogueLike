using UnityEngine;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// The live values <see cref="ScreenGradeFeature"/> pushes into its material each frame.
    ///
    /// A static hand-off rather than a direct reference because of the assembly wall: the renderer
    /// feature has to live in <c>Valkur.Core</c> (it is referenced by a renderer asset, and Core is
    /// the only assembly URP-adjacent code may depend on downward), while the thing that decides
    /// what the grade should be — the day/night cycle — lives in <c>Valkur.Gameplay</c>. Gameplay
    /// may reference Core; Core may not reference Gameplay. So Gameplay writes and Core reads.
    ///
    /// Inert by default: with <see cref="Enabled"/> false the feature skips its pass entirely, so a
    /// scene with no day/night cycle pays nothing.
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
        }

        /// <summary>When false the pass is not enqueued at all.</summary>
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
    }
}
