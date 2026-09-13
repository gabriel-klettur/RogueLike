using UnityEngine;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// What a shadow on the ground looks like at a given time of day, as a pure function.
    ///
    /// The world is drawn top-down with +Y "up the screen", which is AWAY from the camera, so
    /// a shadow lies on the ground as a SHEARED, SQUASHED copy of the silhouette anchored at
    /// the feet: <see cref="SunShadow.SkewX"/> tips it sideways as the sun crosses the sky and
    /// <see cref="SunShadow.SquashY"/> stretches it "north" as the sun drops. The sun rises in
    /// the east (+X) — so at dawn the shadow points WEST (a negative skew) — and sets in the
    /// west, so at dusk it points east; at noon it is short and straight. That is the whole
    /// hour readout: a long shadow says morning or evening without a glance at the clock.
    ///
    /// Pure and in Core so <c>SunModelTests</c> can pin the direction and the day window with
    /// no scene, and so both the projected shadows and the cloud layer read one sun.
    /// </summary>
    public static class SunModel
    {
        /// <summary>The cycle's dawn ramp start, normalized. Mirrors <c>DayNightCycle.DAWN_START</c>.</summary>
        public const float DefaultSunrise = 0.18f;

        /// <summary>The cycle's night band start, normalized. Mirrors <c>DayNightCycle.NIGHT_START</c>.</summary>
        public const float DefaultSunset = 0.84f;

        /// <summary>
        /// 0 with the sun below the horizon, 1 with it overhead: <c>sin</c> of the sun's
        /// progress across the day window, so it rises and falls smoothly at both ends.
        /// </summary>
        public static float Daylight01(float timeNormalized, float sunrise = DefaultSunrise, float sunset = DefaultSunset)
        {
            if (!TryProgress(timeNormalized, sunrise, sunset, out float p)) return 0f;
            return Mathf.Sin(p * Mathf.PI);
        }

        /// <summary>The shadow for <paramref name="timeNormalized"/>. Strength is 0 at night.</summary>
        public static SunShadow ShadowAt(float timeNormalized,
                                         float skewMax, float squashNoon, float squashHorizon,
                                         float sunrise = DefaultSunrise, float sunset = DefaultSunset)
        {
            if (!TryProgress(timeNormalized, sunrise, sunset, out float p))
                return new SunShadow(0f, squashNoon, 0f, 0f);

            float elevation = Mathf.Sin(p * Mathf.PI);
            // -1 at sunrise (shadow west), 0 at noon, +1 at sunset (shadow east).
            float skew = -Mathf.Cos(p * Mathf.PI) * skewMax;
            // Long at the horizon, short at noon.
            float squash = Mathf.Lerp(squashHorizon, squashNoon, elevation);
            // A shadow needs some sun behind it: fully formed once the sun clears ~15% of its
            // arc, so it fades in over the dawn ramp instead of snapping on at the first tick.
            float strength = Mathf.Clamp01(elevation / StrengthFullAtElevation);
            return new SunShadow(skew, squash, strength, elevation);
        }

        /// <summary>Elevation at which the shadow reaches full strength. See <see cref="ShadowAt"/>.</summary>
        private const float StrengthFullAtElevation = 0.25f;

        private static bool TryProgress(float t, float sunrise, float sunset, out float progress)
        {
            progress = 0f;
            float span = sunset - sunrise;
            if (span <= 0f) return false;
            t = Mathf.Repeat(t, 1f);
            if (t < sunrise || t >= sunset) return false;
            progress = (t - sunrise) / span;
            return true;
        }
    }

    /// <summary>One sample of <see cref="SunModel"/>.</summary>
    public readonly struct SunShadow
    {
        /// <summary>Horizontal shear per unit of height. Negative points the shadow west.</summary>
        public readonly float SkewX;

        /// <summary>Vertical scale of the silhouette lying on the ground. Larger is longer.</summary>
        public readonly float SquashY;

        /// <summary>0..1, how much sun there is to cast it. 0 at night.</summary>
        public readonly float Strength;

        /// <summary>0..1, how high the sun is. 0 at the horizon and at night.</summary>
        public readonly float Elevation;

        public SunShadow(float skewX, float squashY, float strength, float elevation)
        {
            SkewX     = skewX;
            SquashY   = squashY;
            Strength  = strength;
            Elevation = elevation;
        }
    }
}
