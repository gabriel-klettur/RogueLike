using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Gameplay.World.Lighting
{
    /// <summary>
    /// The bloom's contract with the rest of the look, pinned on the pure half.
    ///
    /// Two properties carry the layer. The threshold sits at exactly 1.0 linear, so no texel
    /// of pixel art under a 1.0 ambient can ever feed the pyramid — only the additive light the
    /// HDR buffer keeps above white blooms, which is what stops pale ground from glowing. And
    /// the bloom is OFF until something publishes it: the main menu has no day/night cycle and
    /// its title plate is contrast-measured against the raw frame, so a bloom that defaulted on
    /// would resolve that plate against a frame the player never sees.
    /// </summary>
    [TestFixture]
    public class ScreenBloomSettingsTests
    {
        private bool  _enabled;
        private float _intensity, _threshold, _knee;
        private Color _tint;

        [SetUp]
        public void Snapshot()
        {
            _enabled   = ScreenGradeSettings.BloomEnabled;
            _intensity = ScreenGradeSettings.BloomIntensity;
            _threshold = ScreenGradeSettings.BloomThreshold;
            _knee      = ScreenGradeSettings.BloomSoftKnee;
            _tint      = ScreenGradeSettings.BloomTint;
        }

        [TearDown]
        public void Restore()
        {
            ScreenGradeSettings.BloomEnabled   = _enabled;
            ScreenGradeSettings.BloomIntensity = _intensity;
            ScreenGradeSettings.BloomThreshold = _threshold;
            ScreenGradeSettings.BloomSoftKnee  = _knee;
            ScreenGradeSettings.BloomTint      = _tint;
        }

        [Test]
        public void TheThreshold_SitsAtWhite_SoPixelArtNeverBlooms()
        {
            Assert.That(ScreenGradeSettings.DefaultBloomThreshold, Is.EqualTo(1f),
                "A threshold below 1.0 lets lit pixel art feed the pyramid and pale ground glows.");
        }

        [Test]
        public void TheIntensity_IsAnAccent_NotAFog()
        {
            Assert.That(ScreenGradeSettings.DefaultBloomIntensity, Is.InRange(0.2f, 0.5f),
                "Measured: under 0.2 nothing says the layer exists, over 0.5 a fireball paints the wall.");
        }

        [Test]
        public void Bloom_IsOffUntilSomethingPublishesIt()
        {
            ScreenGradeSettings.BloomEnabled   = false;
            ScreenGradeSettings.BloomIntensity = ScreenGradeSettings.DefaultBloomIntensity;
            Assert.IsFalse(ScreenGradeSettings.BloomWouldChangeTheFrame,
                "With nothing publishing it the bloom must not be enqueued: the menu is measured raw.");
        }

        [Test]
        public void Bloom_AtZeroIntensity_IsNotEnqueued()
        {
            ScreenGradeSettings.BloomEnabled   = true;
            ScreenGradeSettings.BloomIntensity = 0f;
            Assert.IsFalse(ScreenGradeSettings.BloomWouldChangeTheFrame,
                "'look bloom off' sets the intensity to zero; a zero bloom is a full-screen pass for nothing.");

            ScreenGradeSettings.BloomIntensity = ScreenGradeSettings.DefaultBloomIntensity;
            Assert.IsTrue(ScreenGradeSettings.BloomWouldChangeTheFrame);
        }

        /// <summary>
        /// The threshold stays at white at every hour, so the world never blooms at night.
        /// Following the ambient down made torch-lit ground count as emissive and lifted the
        /// whole night out of its darkness (measured +8 % mean luminance from the bloom alone).
        /// </summary>
        [Test]
        public void BloomThreshold_StaysWhiteAtEveryHour_SoTheNightIsNotLifted()
        {
            Assert.That(DayNightCycle.BloomThresholdFor(Color.white, 1f), Is.EqualTo(1f).Within(1e-4f), "noon");
            Assert.That(DayNightCycle.BloomThresholdFor(new Color(0.55f, 0.65f, 1f), 0.35f), Is.EqualTo(1f).Within(1e-4f),
                "midnight: a lit surface under a torch must not bloom");
            Assert.That(DayNightCycle.BloomThresholdFor(Color.black, 0f), Is.EqualTo(1f).Within(1e-4f), "a pitch-black cave");
        }

        [Test]
        public void BloomTint_IsNeutralAtNoon_AndLeansWithTheNight_WithoutSaturating()
        {
            var noon = DayNightCycle.BloomTintFor(Color.white);
            Assert.That(noon.r, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(noon.g, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(noon.b, Is.EqualTo(1f).Within(1e-4f));

            // A deep blue night ambient leans the halo cool, at full value — a dark tint would
            // multiply the bloom away exactly when the torches it exists for are lit.
            var night = DayNightCycle.BloomTintFor(new Color(0.25f, 0.32f, 0.70f));
            Assert.That(night.b, Is.EqualTo(1f).Within(1e-4f), "The bloom tint keeps full value.");
            Assert.That(night.r, Is.LessThan(night.b), "A blue ambient leans the tint blue.");
            Color.RGBToHSV(night, out _, out float s, out _);
            Assert.That(s, Is.LessThan(0.5f),
                "Half the ambient's saturation at most, or a red fireball's halo turns grey at midnight.");
        }
    }
}
