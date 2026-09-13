using UnityEngine;
using Valkur.Core.Rendering;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// The one wind every swaying sprite reads, published as a shader global once per frame
    /// from <see cref="WeatherManager"/> right after <see cref="WeatherWind"/> ticks — the same
    /// gust sample the rain and the snow slant with, so the leaves and the drops lean together.
    ///
    /// The amplitude is small on purpose and stated in TEXELS of the 16-PPU art: a canopy that
    /// moves a whole tile in a breeze reads as jelly. At rest the ambient breeze moves a crown
    /// by about a third of a texel — enough that a still forest is not a photograph — and the
    /// heaviest wind by about two.
    /// </summary>
    public static class WindSway
    {
        private static readonly int WindId = Shader.PropertyToID("_ValkurWind");

        /// <summary>World units per texel at the world's PPU.</summary>
        private const float Texel = 1f / 16f;

        /// <summary>Amplitude at the ambient breeze, in texels.</summary>
        public const float RestTexels = 0.35f;

        /// <summary>Amplitude at the heaviest weather wind, in texels.</summary>
        public const float StormTexels = 2.2f;

        /// <summary>
        /// The weather wind speed (world units/s) at which the amplitude reaches
        /// <see cref="StormTexels"/>. Heavy wind runs around 12 u/s.
        /// </summary>
        private const float StormSpeed = 12f;

        private static float _time;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _time      = 0f;
            Amplitude  = 0f;
            Shader.SetGlobalVector(WindId, Vector4.zero);
        }

        /// <summary>The amplitude last published, in world units. Test seam.</summary>
        public static float Amplitude { get; private set; }

        /// <summary>
        /// The amplitude for a given weather wind and gust, in world units. Pure, so the
        /// tests can pin the range without a scene.
        /// </summary>
        public static float AmplitudeFor(float weatherSpeed, float gust01, bool enabled)
        {
            if (!enabled) return 0f;
            float storm = Mathf.Clamp01(weatherSpeed / StormSpeed);
            float texels = Mathf.Lerp(RestTexels, StormTexels, storm);
            // The gust breathes the amplitude around its base rather than switching it: a
            // gust that took the amplitude to zero would freeze every crown between puffs.
            texels *= Mathf.Lerp(0.7f, 1.3f, gust01);
            return texels * Texel;
        }

        /// <summary>Advance the clock and push the wind to every swaying shader.</summary>
        public static void Publish(float deltaTime)
        {
            _time += Mathf.Max(0f, deltaTime);
            Amplitude = AmplitudeFor(WeatherWind.WeatherSpeed, WeatherWind.Gust01, WorldLookSettings.WindSway);
            Shader.SetGlobalVector(WindId, new Vector4(Amplitude, _time, WeatherWind.Gust01, WeatherWind.DirectionX));
        }
    }
}
