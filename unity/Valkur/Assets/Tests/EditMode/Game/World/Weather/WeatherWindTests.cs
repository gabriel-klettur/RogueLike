using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Tests.EditMode.Game.World.Weather
{
    /// <summary>
    /// The shared wind field is the single fact rain, snow and the wind effect all read, so
    /// its contract is worth pinning: it always blows a little, it never blows backwards
    /// relative to its own sign, its gust stays inside the envelope it promises, and the wind
    /// effect's contribution is additive on top of the ambient breeze rather than replacing it.
    ///
    /// The field is static and Domain Reload is OFF, so every test resets it first — otherwise
    /// a test that leaves a storm behind changes what the next one measures.
    /// </summary>
    [TestFixture]
    public class WeatherWindTests
    {
        [SetUp]
        public void Reset()
        {
            WeatherWind.WeatherSpeed = 0f;
            WeatherWind.SetDirection(-1f);
        }

        [TearDown]
        public void Restore() => Reset();

        [Test]
        public void AmbientBreeze_BlowsEvenWithNoWeatherActive()
        {
            // A mathematically vertical fall reads as a screen overlay rather than as weather;
            // the ambient term is what stops that, so it must survive "all weather off".
            Assert.That(WeatherWind.WeatherSpeed, Is.EqualTo(0f));
            Assert.That(WeatherWind.BaseSpeed, Is.EqualTo(WeatherWind.AmbientSpeed).Within(1e-4f));
            Assert.That(WeatherWind.Speed, Is.GreaterThan(0f));
        }

        [Test]
        public void WeatherSpeed_AddsOnTopOfTheAmbientBreeze()
        {
            WeatherWind.WeatherSpeed = 9f;
            Assert.That(WeatherWind.BaseSpeed, Is.EqualTo(WeatherWind.AmbientSpeed + 9f).Within(1e-4f));
        }

        [Test]
        public void Gust_StaysInsideItsEnvelopeAcrossALongRun()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 4000; i++)
            {
                WeatherWind.Tick(0.05f);   // 200 simulated seconds
                float g = WeatherWind.Gust01;
                Assert.That(g, Is.InRange(0f, 1f), "gust envelope left 0..1");
                if (g < min) min = g;
                if (g > max) max = g;
            }

            // Not a formality: the raw Perlin range is roughly 0.15..0.85, so without the
            // re-normalisation in Tick the envelope would never approach either end and Heavy
            // wind would never actually reach the speed it claims.
            Assert.That(max, Is.GreaterThan(0.75f), "gusts never reach a strong peak");
            Assert.That(min, Is.LessThan(0.25f), "gusts never fall to a real lull");
        }

        [Test]
        public void Speed_IsAlwaysPositive_AndDirectionCarriesTheSign()
        {
            for (int i = 0; i < 200; i++)
            {
                WeatherWind.Tick(0.1f);
                Assert.That(WeatherWind.Speed, Is.GreaterThan(0f));
                Assert.That(Mathf.Sign(WeatherWind.VelocityX), Is.EqualTo(WeatherWind.DirectionX));
            }
        }

        [Test]
        public void FlipDirection_ReversesTheBlow()
        {
            WeatherWind.SetDirection(-1f);
            Assert.That(WeatherWind.VelocityX, Is.LessThan(0f));

            Assert.That(WeatherWind.FlipDirection(), Is.EqualTo(1f));
            Assert.That(WeatherWind.VelocityX, Is.GreaterThan(0f));
        }

        [Test]
        public void SetDirection_TreatsZeroAsLeft_SoARestingAxisDoesNotStallTheWind()
        {
            WeatherWind.SetDirection(0f);
            Assert.That(WeatherWind.DirectionX, Is.EqualTo(-1f));
            Assert.That(WeatherWind.Speed, Is.GreaterThan(0f));
        }
    }
}
