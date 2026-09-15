using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Tests.EditMode.Gameplay.World.Weather
{
    /// <summary>
    /// The wind the canopies read: small at rest, larger in a storm, zero when switched off,
    /// and published as the one shader global every swaying material samples.
    /// </summary>
    [TestFixture]
    public class WindSwayTests
    {
        private bool _swayWas;

        [SetUp] public void Snapshot() { _swayWas = WorldLookSettings.WindSway; WorldLookSettings.WindSway = true; }
        [TearDown] public void Restore() { WorldLookSettings.WindSway = _swayWas; }

        [Test]
        public void AtRest_TheCrownMovesLessThanATexel()
        {
            float rest = WindSway.AmplitudeFor(0f, 0.5f, true);
            Assert.That(rest, Is.GreaterThan(0f), "A still forest is a photograph.");
            Assert.That(rest, Is.LessThan(1f / 16f), "But a whole texel in a breeze reads as jelly.");
        }

        [Test]
        public void AStorm_MovesItMore_AndNeverPastAFewTexels()
        {
            float rest  = WindSway.AmplitudeFor(0f, 0.5f, true);
            float storm = WindSway.AmplitudeFor(12f, 1f, true);
            Assert.That(storm, Is.GreaterThan(rest * 3f));
            Assert.That(storm, Is.LessThan(4f / 16f), "Four texels is a tree falling over.");
        }

        [Test]
        public void TheGust_BreathesTheAmplitude_WithoutFreezingIt()
        {
            float lull = WindSway.AmplitudeFor(6f, 0f, true);
            float peak = WindSway.AmplitudeFor(6f, 1f, true);
            Assert.That(peak, Is.GreaterThan(lull));
            Assert.That(lull, Is.GreaterThan(0f), "A gust that took the amplitude to zero would freeze every crown between puffs.");
        }

        [Test]
        public void SwitchedOff_ItIsExactlyZero()
        {
            Assert.That(WindSway.AmplitudeFor(12f, 1f, false), Is.EqualTo(0f));
        }

        [Test]
        public void Publish_WritesTheGlobalTheShadersRead()
        {
            WindSway.Publish(0.5f);
            var v = Shader.GetGlobalVector("_ValkurWind");
            Assert.That(v.x, Is.EqualTo(WindSway.Amplitude).Within(1e-5f));
            Assert.That(v.x, Is.GreaterThan(0f));

            WorldLookSettings.WindSway = false;
            WindSway.Publish(0.5f);
            Assert.That(Shader.GetGlobalVector("_ValkurWind").x, Is.EqualTo(0f), "'look sway off' stills every crown.");
        }

        [Test]
        public void TheSwayingMaterial_IsAVariantOfTheSnowMaterial()
        {
            var plain = WorldSpriteMaterials.WorldWithSnow(WorldSpriteMaterials.SnowRole.Cap, sway: false);
            var sway  = WorldSpriteMaterials.WorldWithSnow(WorldSpriteMaterials.SnowRole.Cap, sway: true);
            Assert.That(sway, Is.Not.SameAs(plain), "A variant is a material.");
            Assert.That(sway.shader, Is.SameAs(plain.shader), "Same shader: it still collects snow and takes the hit flash.");
            Assert.IsTrue(sway.IsKeywordEnabled(WorldSpriteMaterials.SwayKeyword));
            Assert.IsFalse(plain.IsKeywordEnabled(WorldSpriteMaterials.SwayKeyword),
                "The thousand renderers that never sway must pay no vertex work.");
        }
    }
}
