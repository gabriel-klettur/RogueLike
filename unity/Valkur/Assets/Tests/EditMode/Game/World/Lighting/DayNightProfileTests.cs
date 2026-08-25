using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.World.Lighting
{
    /// <summary>
    /// Pins <see cref="DayNightProfile"/> — the authored 24-hour ramp that replaced the two
    /// hardcoded keyframes the cycle used to carry.
    ///
    /// The properties worth pinning are the ones that were BUGS when the ramp was first authored:
    /// a smoothed curve overshooting past 1.0 (which clips flat to white on a Multiply Light2D
    /// with HDREmulationScale 1), and a plateau writer that used hardcoded band literals instead
    /// of the profile's own serialized bands.
    /// </summary>
    [TestFixture]
    public class DayNightProfileTests
    {
        private DayNightProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<DayNightProfile>();
            _profile.LoadShippedRamp();
        }

        [TearDown]
        public void TearDown()
        {
            if (_profile != null) Object.DestroyImmediate(_profile);
        }

        [Test]
        public void ShippedRamp_IsUsable()
        {
            Assert.IsTrue(_profile.IsUsable,
                "LoadShippedRamp must produce a ramp the cycle will actually bind to; " +
                "an unusable profile silently falls back to the two-keyframe model.");
        }

        [Test]
        public void Intensity_NeverExceedsOne_AcrossTheWholeDay()
        {
            // Regression: AnimationCurve.SmoothTangents carries the slope of the incoming ramp
            // past the key that is meant to be the ceiling, so 0.80 -> 1.00 overshot to 1.05 at
            // noon. Above 1 on a Multiply Light2D with HDREmulationScale 1 does not bloom — it
            // clips flat to white and bleaches the frame.
            float max = 0f;
            for (int i = 0; i <= 2000; i++)
            {
                _profile.Sample(i / 2000f, out _, out float intensity, out _);
                if (intensity > max) max = intensity;
            }

            Assert.LessOrEqual(max, DayNightProfile.MaxIntensity + 1e-4f,
                $"Sampled intensity peaked at {max:F4}; anything above " +
                $"{DayNightProfile.MaxIntensity} clips to white instead of blooming.");
        }

        [Test]
        public void VignetteAlpha_StaysNormalised_AcrossTheWholeDay()
        {
            for (int i = 0; i <= 2000; i++)
            {
                _profile.Sample(i / 2000f, out _, out _, out float vignette);
                Assert.That(vignette, Is.InRange(0f, 1f),
                    "A smoothed curve can dip below its lowest key; the vignette alpha must " +
                    "still be a valid opacity.");
            }
        }

        [Test]
        public void Sample_WrapsPastMidnight()
        {
            _profile.Sample(0.25f, out var atQuarter, out var iQuarter, out _);
            _profile.Sample(1.25f, out var wrapped,   out var iWrapped, out _);

            Assert.AreEqual(atQuarter.r, wrapped.r, 1e-4f, "t and t+1 must be the same moment.");
            Assert.AreEqual(atQuarter.g, wrapped.g, 1e-4f);
            Assert.AreEqual(atQuarter.b, wrapped.b, 1e-4f);
            Assert.AreEqual(iQuarter,    iWrapped,  1e-4f);
        }

        [Test]
        public void Dawn_IsWarm_WhichTheTwoKeyframeModelCouldNotBe()
        {
            // The whole reason this asset exists. Interpolating between day-white and night-blue
            // can only ever travel down the straight RGB segment between them, so red can never
            // lead green and blue. See .github/DAY_NIGHT_AUDIT_AND_ROADMAP.md section 2.
            _profile.Sample(0.25f, out var dawn, out _, out _);

            Assert.Greater(dawn.r, dawn.g, "Dawn must lead with red to read as warm.");
            Assert.Greater(dawn.g, dawn.b, "Dawn must sit above blue to read as warm.");
        }

        [Test]
        public void GoldenHour_IsWarm_AndDimmerThanNoon()
        {
            _profile.Sample(0.50f, out _,     out float noonIntensity,   out _);
            _profile.Sample(0.76f, out var g, out float goldenIntensity, out _);

            Assert.Greater(g.r, g.b, "Golden hour must be warm.");
            Assert.Less(goldenIntensity, noonIntensity,
                "A warm frame must also be a dimmer frame — light that gets warmer without " +
                "getting weaker is the sepia wash that sank the previous attempt.");
        }

        [Test]
        public void Noon_IsEssentiallyNeutral()
        {
            _profile.Sample(0.50f, out var noon, out float intensity, out _);

            Assert.AreEqual(1f, intensity, 1e-3f, "Noon must be full strength.");
            Assert.That(noon.r - noon.b, Is.LessThan(0.06f),
                "Noon is the identity for a Multiply light; a visible tint here means the " +
                "world never reads at its native texture colours.");
        }

        [Test]
        public void Night_IsCool()
        {
            _profile.Sample(0.92f, out var night, out float intensity, out _);

            Assert.Greater(night.b, night.r, "Night must lead with blue.");
            Assert.Less(intensity, 0.5f, "Night must be visibly dimmer than day.");
        }

        [Test]
        public void WritePlateau_MovesTheTargetBand_AndLeavesTheOtherAlone()
        {
            _profile.ReadPlateau(night: false, out var dayBefore, out float dayIBefore, out _);

            var wanted = new Color(0.10f, 0.55f, 0.32f);
            _profile.WritePlateau(night: true, wanted, 0.44f, 0.55f);

            _profile.ReadPlateau(night: true,  out var nightAfter, out float nightI, out float nightV);
            _profile.ReadPlateau(night: false, out var dayAfter,   out float dayI,   out _);

            Assert.AreEqual(wanted.r, nightAfter.r, 1e-2f, "The night plateau must take the written colour.");
            Assert.AreEqual(wanted.g, nightAfter.g, 1e-2f);
            Assert.AreEqual(wanted.b, nightAfter.b, 1e-2f);
            Assert.AreEqual(0.44f, nightI, 1e-2f);
            Assert.AreEqual(0.55f, nightV, 1e-2f);

            Assert.AreEqual(dayBefore.r, dayAfter.r, 1e-3f, "Editing Night must not disturb Day.");
            Assert.AreEqual(dayBefore.g, dayAfter.g, 1e-3f);
            Assert.AreEqual(dayBefore.b, dayAfter.b, 1e-3f);
            Assert.AreEqual(dayIBefore,  dayI,       1e-3f);
        }

        [Test]
        public void WritePlateau_ClampsIntensityToTheSampleCeiling()
        {
            _profile.WritePlateau(night: false, Color.white, intensity: 9f, vignette: 0.5f);
            _profile.ReadPlateau(night: false, out _, out float intensity, out _);

            Assert.LessOrEqual(intensity, DayNightProfile.MaxIntensity + 1e-4f,
                "A runtime slider must not be able to push the ambient past the clipping point.");
        }

        [Test]
        public void Bands_AreOrdered()
        {
            Assert.Less(_profile.DawnStart, _profile.DayStart,   "Dawn must precede Day.");
            Assert.Less(_profile.DayStart,  _profile.DuskStart,  "Day must precede Dusk.");
            Assert.Less(_profile.DuskStart, _profile.NightStart, "Dusk must precede Night.");
        }
    }
}
