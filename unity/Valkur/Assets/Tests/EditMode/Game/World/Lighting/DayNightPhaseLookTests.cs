using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.World.Lighting
{
    /// <summary>
    /// Pins the LOOK of each phase on the ramp that actually ships.
    ///
    /// <see cref="DayNightProfileTests"/> exercises a profile built in memory from
    /// <c>LoadShippedRamp</c>; this fixture loads <c>Resources/DayNightProfile.asset</c>, the file
    /// the game reads at runtime. The distinction matters: someone can drag a gradient key in the
    /// Inspector and flatten the dawn without touching a line of code, and only a test that reads
    /// the ASSET will notice.
    ///
    /// Everything here is a characteristic, not a literal — "dawn leads with red", not
    /// "dawn is exactly (0.94, 0.62, 0.54)". Pinning literals would fail on every deliberate
    /// retune and get deleted within a week; pinning characteristics fails only when the phase
    /// stops being that phase.
    ///
    /// The times are the five the F2 editor's Phases panel jumps to, so this fixture and that
    /// panel describe the same five moments.
    /// </summary>
    [TestFixture]
    public class DayNightPhaseLookTests
    {
        private const float Dawn     = 5.5f  / 24f;   // 05:30
        private const float Morning  = 9f    / 24f;   // 09:00
        private const float Noon     = 12f   / 24f;   // 12:00
        private const float Dusk     = 18.5f / 24f;   // 18:30
        private const float Midnight = 0f;            // 00:00

        private DayNightProfile _shipped;

        [SetUp]
        public void SetUp()
        {
            _shipped = Resources.Load<DayNightProfile>("DayNightProfile");
            Assert.IsNotNull(_shipped,
                "Resources/DayNightProfile.asset is missing. Without it DayNightCycle silently " +
                "falls back to two hardcoded keyframes that cannot produce a warm dawn at all.");
            Assert.IsTrue(_shipped.IsUsable,
                "The shipped profile has no usable ramp, so the cycle would fall back to the " +
                "two-keyframe model and every phase below would quietly stop existing.");
        }

        private (Color color, float intensity, float vignette) At(float t)
        {
            _shipped.Sample(t, out var c, out var i, out var v);
            return (c, i, v);
        }

        // ── The five phases the F2 panel offers ────────────────────────────────────

        [Test]
        public void Dawn_LeadsWithRed_IsWarmerThanNight_AndDimmerThanNoon()
        {
            var (c, intensity, _)     = At(Dawn);
            var (night, _, _)         = At(Midnight);
            var (_, noonIntensity, _) = At(Noon);

            Assert.Greater(c.r, c.g, "Dawn must lead with red…");
            Assert.Greater(c.r, c.b, "…on both axes.");

            // Deliberately NOT r > g > b. At 05:30 the ramp is still climbing out of the blue
            // hour, so blue legitimately sits above green — that is the mauve of civil twilight,
            // and it is authored on purpose. What must hold is that dawn is warmer than the night
            // it came from.
            Assert.Greater(c.r - c.b, night.r - night.b,
                "Dawn must be warmer than midnight, or the sunrise is not happening.");

            Assert.Less(intensity, noonIntensity,
                "Dawn must be dimmer than noon. Light that warms without weakening is the sepia " +
                "wash that got the previous cinematic model deleted.");
        }

        [Test]
        public void Morning_HasRecoveredMostOfItsLight_AndIsNearlyNeutral()
        {
            var (c, intensity, _) = At(Morning);

            Assert.Greater(intensity, 0.85f, "By 09:00 the sun is up.");
            Assert.Less(c.r - c.b, 0.20f,
                "Morning is the tail of the dawn ramp, not a second golden hour — a strong warm " +
                "cast here means the ramp is arriving late and the whole morning reads orange.");
        }

        [Test]
        public void Noon_IsTheIdentity()
        {
            var (c, intensity, vignette) = At(Noon);

            Assert.AreEqual(1f, intensity, 1e-3f,
                "Noon must be full strength: on a Multiply Light2D that is the identity.");
            Assert.Less(Mathf.Abs(c.r - c.b), 0.06f, "Noon must not carry a visible tint…");
            Assert.Less(Mathf.Abs(c.r - c.g), 0.06f, "…on either axis.");
            Assert.Less(vignette, 0.10f,
                "Midday must not be framed by a vignette; the world reads as authored.");
        }

        [Test]
        public void Dusk_IsWarmerThanNoon_AndDimmerThanMorning()
        {
            var (c, intensity, _) = At(Dusk);
            var (_, morningIntensity, _) = At(Morning);
            var (noon, _, _) = At(Noon);

            Assert.Greater(c.r - c.b, noon.r - noon.b,
                "Dusk must be visibly warmer than noon — this is the golden hour.");
            Assert.Less(intensity, morningIntensity, "Dusk must be past the peak.");
        }

        [Test]
        public void Midnight_IsCoolAndDark()
        {
            var (c, intensity, vignette) = At(Midnight);

            Assert.Greater(c.b, c.r, "Night must lead with blue.");
            Assert.Less(intensity, 0.5f, "Night must be well below daylight.");
            Assert.Greater(vignette, 0.10f, "Night must close the screen edges in.");
        }

        // ── The shape of the whole day, not just the five samples ──────────────────

        [Test]
        public void Intensity_RisesThroughDawn_AndFallsThroughDusk()
        {
            // Sampled rather than asserted at the endpoints: a ramp can have the right endpoints
            // and still dip in the middle, which reads as the sun flickering.
            AssertMonotonic(0.19f, 0.34f, rising: true,
                "Dawn must brighten without ever dipping — a non-monotonic sunrise reads as a flicker.");
            AssertMonotonic(0.72f, 0.88f, rising: false,
                "Dusk must darken without ever brightening again.");
        }

        private void AssertMonotonic(float from, float to, bool rising, string because)
        {
            const int steps = 60;
            const float tolerance = 1e-3f;   // absorbs curve-evaluation noise, not a real reversal
            float previous = float.NaN;

            for (int i = 0; i <= steps; i++)
            {
                float t = Mathf.Lerp(from, to, i / (float)steps);
                var (_, intensity, _) = At(t);

                if (!float.IsNaN(previous))
                {
                    bool ok = rising
                        ? intensity >= previous - tolerance
                        : intensity <= previous + tolerance;
                    Assert.IsTrue(ok,
                        $"{because} At t={t:F3} intensity went {previous:F4} -> {intensity:F4}.");
                }
                previous = intensity;
            }
        }

        [Test]
        public void TheDayHasARealWarmPeak_NotJustAWhiteToBlueLerp()
        {
            // The single property the two-keyframe model could not have. Interpolating between
            // day-white and night-blue can only travel down the straight RGB segment between
            // them, so r - b can never exceed its value at white (zero). If this fails, the ramp
            // has collapsed back into a lerp and every warm phase is gone.
            float warmest = float.MinValue;
            float warmestAt = 0f;
            for (int i = 0; i <= 1000; i++)
            {
                float t = i / 1000f;
                var (c, _, _) = At(t);
                float warmth = c.r - c.b;
                if (warmth > warmest) { warmest = warmth; warmestAt = t; }
            }

            Assert.Greater(warmest, 0.25f,
                $"The warmest moment of the whole day is only r-b={warmest:F3} (at t={warmestAt:F2}). " +
                "The ramp has flattened into a white-to-blue interpolation and the golden hour is gone.");
        }

        [Test]
        public void NightAndDay_AreClearlyDifferentBrightnesses()
        {
            var (_, night, _) = At(Midnight);
            var (_, day, _)   = At(Noon);

            Assert.Greater(day / Mathf.Max(0.001f, night), 2f,
                "Day must be at least twice night. Below that the cycle stops reading as a cycle.");
        }

        [Test]
        public void Vignette_IsWeakestAtMidday_AndStrongestAtNight()
        {
            var (_, _, noon)  = At(Noon);
            var (_, _, night) = At(Midnight);
            var (_, _, dusk)  = At(Dusk);

            Assert.Less(noon, dusk,  "The vignette must open up at midday…");
            Assert.Less(dusk, night, "…and close in as the light goes.");
        }

        // ── The grade half, which the ambient light cannot express ─────────────────

        [Test]
        public void Saturation_IsFullAtNoon_AndDrainedAtNight()
        {
            _shipped.SampleGrade(Noon,     out float noonSat,  out _);
            _shipped.SampleGrade(Midnight, out float nightSat, out _);

            Assert.AreEqual(1f, noonSat, 1e-2f,
                "Midday must read at the art's own saturation.");
            Assert.Less(nightSat, 0.85f,
                "Night must lose chroma. A night that keeps daytime saturation reads as a blue " +
                "filter over a daytime scene — which is exactly what it looked like before the " +
                "screen grade existed.");
        }

        [Test]
        public void Saturation_NeverExceedsOne()
        {
            for (int i = 0; i <= 1000; i++)
            {
                _shipped.SampleGrade(i / 1000f, out float sat, out _);
                Assert.LessOrEqual(sat, 1.001f,
                    $"Saturation overshot to {sat:F3} at t={i / 1000f:F3}; a smoothed curve " +
                    "overshooting past its top key oversaturates the frame.");
            }
        }

        [Test]
        public void Contrast_IsNeutralAtNoon_AndLiftedAtNight()
        {
            _shipped.SampleGrade(Noon,     out _, out float noonContrast);
            _shipped.SampleGrade(Midnight, out _, out float nightContrast);

            Assert.AreEqual(1f, noonContrast, 1e-2f, "Midday must not be recontrasted.");
            Assert.Greater(nightContrast, 1f,
                "Night lifts contrast to keep the frame from going flat once saturation is gone.");
        }
    }
}
