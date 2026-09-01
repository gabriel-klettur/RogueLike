using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Tests.EditMode.Game.World.Weather
{
    /// <summary>
    /// The grade half of the weather look, and the storm lightning that rides on it.
    ///
    /// Two properties matter more than any individual number here. First, NEUTRAL MUST BE
    /// NEUTRAL: <c>DayNightCycle.PublishScreenGrade</c> multiplies the weather's saturation
    /// modifier into its own value and hands the weather's Gain and Lift straight to the
    /// shader every frame, weather or not — so a non-identity value with nothing falling
    /// would grade every frame in the game. Second, lightning must not fire under a drizzle:
    /// a flash with no storm behind it reads as a rendering fault.
    /// </summary>
    [TestFixture]
    public class WeatherGradeTests
    {
        [SetUp]
        public void Reset()
        {
            // Statics, Domain Reload OFF: run any envelope in flight to completion, then
            // disarm the scheduler and return the modifiers to neutral.
            WeatherGrade.TickLightning(10f, 0f, false);
            WeatherGrade.TickLightning(0.016f, 0f, false);
            WeatherGrade.Compose(0f, 0f, 0f);
        }

        [TearDown]
        public void Restore() => Reset();

        [Test]
        public void NoWeather_LeavesTheGradeExactlyNeutral()
        {
            Assert.That(WeatherGrade.SaturationMultiplier, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(WeatherGrade.VignetteAdd, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Vector3.Distance(WeatherGrade.Gain, Vector3.one), Is.LessThan(1e-4f));
            Assert.That(WeatherGrade.Lift.magnitude, Is.LessThan(1e-4f));
            Assert.That(WeatherGrade.LightFlash01, Is.EqualTo(0f));
        }

        [Test]
        public void HeavyRain_DrainsColourAndClosesTheFrameIn()
        {
            WeatherGrade.Compose(1f, 0f, 0f);
            Assert.That(WeatherGrade.SaturationMultiplier, Is.LessThan(0.85f));
            Assert.That(WeatherGrade.SaturationMultiplier, Is.GreaterThanOrEqualTo(0.55f),
                "the saturation floor exists so a storm never renders the world monochrome");
            Assert.That(WeatherGrade.VignetteAdd, Is.GreaterThan(0f));
        }

        [Test]
        public void Snow_LiftsTheBlacks_WhereRainMostlyDoesNot()
        {
            WeatherGrade.Compose(0f, 1f, 0f);
            float snowLift = WeatherGrade.Lift.x;

            WeatherGrade.Compose(1f, 0f, 0f);
            float rainLift = WeatherGrade.Lift.x;

            Assert.That(snowLift, Is.GreaterThan(rainLift),
                "an overcast snow sky raises the shadows; rain mostly desaturates them");
        }

        [Test]
        public void EveryDensity_KeepsSaturationInsideItsLegalRange()
        {
            for (float r = 0f; r <= 1.001f; r += 0.25f)
            for (float s = 0f; s <= 1.001f; s += 0.25f)
            for (float w = 0f; w <= 1.001f; w += 0.5f)
            {
                WeatherGrade.Compose(r, s, w);
                Assert.That(WeatherGrade.SaturationMultiplier, Is.InRange(0.55f, 1f),
                    $"rain {r} snow {s} wind {w}");
            }
        }

        [Test]
        public void Lightning_NeverFiresBelowTheStormThreshold()
        {
            float drizzle = WeatherGrade.StormDensityThreshold - 0.05f;
            for (int i = 0; i < 20000; i++)   // ~5 simulated minutes
            {
                WeatherGrade.TickLightning(0.016f, drizzle, enabled: true);
                Assert.That(WeatherGrade.LightFlash01, Is.EqualTo(0f),
                    "a flash under a drizzle reads as a rendering fault, not as weather");
            }
        }

        [Test]
        public void Lightning_FiresUnderAHeavyStorm_AndTheEnvelopeReturnsToZero()
        {
            bool sawFlash = false;
            float peak = 0f;

            for (int i = 0; i < 20000; i++)   // ~5 simulated minutes
            {
                WeatherGrade.TickLightning(0.016f, 1f, enabled: true);
                float f = WeatherGrade.LightFlash01;
                Assert.That(f, Is.InRange(0f, 1f));
                if (f > 0f) sawFlash = true;
                if (f > peak) peak = f;
            }

            Assert.That(sawFlash, Is.True, "no strike in five simulated minutes of heavy rain");
            Assert.That(peak, Is.GreaterThan(0.8f), "the return stroke never reached full strength");
        }

        [Test]
        public void Lightning_DoesNotFireOnTheFrameTheStormStarts()
        {
            // Arming and firing are separate steps on purpose: a strike on the same frame as
            // the click that raised the rain reads as a side effect of the UI.
            WeatherGrade.TickLightning(0.016f, 1f, enabled: true);
            Assert.That(WeatherGrade.LightFlash01, Is.EqualTo(0f));
        }

        [Test]
        public void Strike_RunsAFullEnvelopeAndThenClearsItself()
        {
            WeatherGrade.Strike();
            Assert.That(WeatherGrade.IsFlashing, Is.True);

            float peak = 0f;
            for (int i = 0; i < 200; i++)
            {
                WeatherGrade.TickLightning(0.016f, 0f, enabled: false);
                peak = Mathf.Max(peak, WeatherGrade.LightFlash01);
            }

            Assert.That(peak, Is.GreaterThan(0.8f));
            Assert.That(WeatherGrade.IsFlashing, Is.False, "the envelope never finished");
            Assert.That(WeatherGrade.LightFlash01, Is.EqualTo(0f));
        }

        [Test]
        public void Strike_FinishesEvenWhenTheRainIsTurnedOffUnderIt()
        {
            // Cutting an envelope mid-stroke is a visible hard edge; the flash owns its own
            // completion regardless of what the weather does while it plays.
            WeatherGrade.Strike();
            WeatherGrade.TickLightning(0.016f, 0f, enabled: false);
            Assert.That(WeatherGrade.IsFlashing, Is.True);
        }
    }
}
