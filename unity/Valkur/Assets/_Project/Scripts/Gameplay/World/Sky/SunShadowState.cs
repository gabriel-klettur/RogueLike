using UnityEngine;
using Valkur.Core.Rendering;
using Valkur.Data;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.World.Sky
{
    /// <summary>
    /// The one sun every shadow in the world reads, sampled once per frame.
    ///
    /// Computed by <see cref="CloudShadowLayer"/>'s Update (the sky owns the sun) and read by
    /// every <see cref="SunShadowCaster"/> in its LateUpdate, so a thousand casters cost one
    /// evaluation of the model and one pair of shader globals rather than a thousand. The
    /// globals are what the shear shader consumes; the properties are for casters and tests.
    ///
    /// Domain Reload is OFF: without the reset a Play session would open with the previous
    /// session's sun until the first tick, one frame of shadows pointing the wrong way.
    /// </summary>
    public static class SunShadowState
    {
        private static readonly int SunShadowId      = Shader.PropertyToID("_ValkurSunShadow");
        private static readonly int SunShadowColorId = Shader.PropertyToID("_ValkurSunShadowColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SkewX     = 0f;
            SquashY   = 0.22f;
            Alpha     = 0f;
            BlobAlpha = 0f;
            Daylight  = 0f;
            _ticked   = false;
            Publish(Color.black);
        }

        private static bool _ticked;

        /// <summary>Horizontal shear per unit of height. Negative points west.</summary>
        public static float SkewX { get; private set; }

        /// <summary>Vertical scale of the silhouette on the ground.</summary>
        public static float SquashY { get; private set; } = 0.22f;

        /// <summary>Alpha of the projected shadow, weather and indoors already folded in.</summary>
        public static float Alpha { get; private set; }

        /// <summary>Alpha of the contact blob. Independent of the sun: it is never zero outdoors.</summary>
        public static float BlobAlpha { get; private set; }

        /// <summary>0..1 how much daylight there is. The cloud layer scales with it.</summary>
        public static float Daylight { get; private set; }

        /// <summary>True once something has evaluated the sun this session.</summary>
        public static bool HasTicked => _ticked;

        /// <summary>
        /// Evaluate the sun for this frame. <paramref name="overcast01"/> is the weather's
        /// densest precipitation; <paramref name="indoors"/> removes the sun but keeps the blob.
        /// </summary>
        public static void Tick(SkyStyle style, float timeNormalized, float overcast01, bool indoors)
        {
            _ticked = true;
            if (style == null) style = SkyStyle.Active;

            var sun = SunModel.ShadowAt(timeNormalized, style.skewMax, style.squashNoon, style.squashHorizon,
                                        style.sunrise, style.sunset);

            bool on = WorldLookSettings.SunShadows;
            float weather = 1f - Mathf.Clamp01(overcast01) * style.weatherDimming;

            SkewX     = sun.SkewX;
            SquashY   = sun.SquashY;
            Daylight  = sun.Elevation;
            Alpha     = on && !indoors ? style.sunShadowAlpha * sun.Strength * weather : 0f;
            BlobAlpha = on ? style.blobAlpha : 0f;

            Publish(style.sunShadowColor);
        }

        private static void Publish(Color colour)
        {
            Shader.SetGlobalVector(SunShadowId, new Vector4(SkewX, SquashY, Alpha, 0f));
            Shader.SetGlobalColor(SunShadowColorId, colour);
        }
    }
}
