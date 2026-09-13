using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Rendering;

namespace Valkur.Tests.EditMode.Game.World.Sky
{
    /// <summary>
    /// The sun as a pure function: where a shadow points at each hour, and when there is none.
    ///
    /// The direction is the half that cannot be verified from a screenshot without knowing the
    /// hour, and the half that silently shipped backwards once already in this project (the
    /// player art faced away from the cursor with every frame individually correct). The sun
    /// rises in the east, so a DAWN shadow points WEST — a negative skew — and a dusk shadow
    /// points east. If that ever flips, the whole world reads the wrong hour and nothing logs.
    /// </summary>
    [TestFixture]
    public class SunModelTests
    {
        private const float SkewMax = 1.15f, Noon = 0.22f, Horizon = 0.62f;

        private static SunShadow At(float t) => SunModel.ShadowAt(t, SkewMax, Noon, Horizon);

        [Test]
        public void Night_CastsNoShadow()
        {
            Assert.That(At(0.0f).Strength, Is.EqualTo(0f), "Midnight.");
            Assert.That(At(0.10f).Strength, Is.EqualTo(0f), "Before sunrise.");
            Assert.That(At(0.90f).Strength, Is.EqualTo(0f), "After sunset.");
            Assert.That(SunModel.Daylight01(0.0f), Is.EqualTo(0f));
        }

        [Test]
        public void DawnShadow_PointsWest_DuskShadow_PointsEast()
        {
            float dawn = SunModel.DefaultSunrise + 0.02f;
            float dusk = SunModel.DefaultSunset  - 0.02f;
            Assert.That(At(dawn).SkewX, Is.LessThan(-0.8f), "The sun rises in the east: a dawn shadow tips west.");
            Assert.That(At(dusk).SkewX, Is.GreaterThan(0.8f), "It sets in the west: a dusk shadow tips east.");
        }

        [Test]
        public void NoonShadow_IsShortAndStraight_HorizonShadow_IsLong()
        {
            float noon = (SunModel.DefaultSunrise + SunModel.DefaultSunset) * 0.5f;
            var s = At(noon);
            Assert.That(Mathf.Abs(s.SkewX), Is.LessThan(0.05f), "Straight under the sun.");
            Assert.That(s.SquashY, Is.EqualTo(Noon).Within(0.02f), "Short at noon.");
            Assert.That(s.Strength, Is.EqualTo(1f).Within(1e-4f), "Full sun.");
            Assert.That(s.Elevation, Is.EqualTo(1f).Within(1e-4f));

            var low = At(SunModel.DefaultSunrise + 0.01f);
            Assert.That(low.SquashY, Is.GreaterThan(s.SquashY * 2f), "Long at the horizon.");
        }

        [Test]
        public void TheShadow_FadesInOverDawn_RatherThanSnappingOn()
        {
            var first = At(SunModel.DefaultSunrise + 0.001f);
            Assert.That(first.Strength, Is.GreaterThan(0f).And.LessThan(0.2f),
                "One tick after sunrise the shadow exists and is faint: it must not pop to full.");
            Assert.That(At(SunModel.DefaultSunrise + 0.08f).Strength, Is.EqualTo(1f).Within(1e-3f),
                "And it is fully formed well before the day band.");
        }

        [Test]
        public void TheWindow_IsTheCyclesOwn()
        {
            // The sun's window mirrors the cycle's ramps. If those constants move, this is the
            // test that says the shadows are now lying about the hour.
            Assert.That(SunModel.DefaultSunrise, Is.EqualTo(Valkur.Gameplay.World.DayNightCycle.DAWN_START));
            Assert.That(SunModel.DefaultSunset,  Is.EqualTo(Valkur.Gameplay.World.DayNightCycle.NIGHT_START));
        }
    }
}
