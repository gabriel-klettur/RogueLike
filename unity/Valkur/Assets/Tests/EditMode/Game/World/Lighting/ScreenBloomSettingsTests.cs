using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Lighting
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

        [Test]
        public void BloomThreshold_IsWhiteByDay_AndFollowsTheAmbientDownAtNight()
        {
            // Noon: a full white ambient at intensity 1 puts the threshold at white, so no lit
            // texel of pixel art can bloom.
            Assert.That(DayNightCycle.BloomThresholdFor(Color.white, 1f), Is.EqualTo(1f).Within(1e-4f));

            // Midnight: a blue ambient at a third of the intensity. The brightest lit surface is
            // now well under 0.4, and a torch flame drawn at 0.6 has to cross the line or the
            // bloom is invisible for the whole night — measured at a delta of 20/255 before this.
            float night = DayNightCycle.BloomThresholdFor(new Color(0.55f, 0.65f, 1f), 0.35f);
            Assert.That(night, Is.LessThan(0.6f), "A torch flame at 0.7 must bloom at night.");
            // And the ground under a lantern must NOT: ambient-lit ground plus a 0.26 light is a
            // lit surface, not an emissive one. Measured at a 0.30 floor: a white blob.
            Assert.That(night, Is.GreaterThan(0.4f), "A lantern's pool on the ground is not a light source.");
            Assert.That(DayNightCycle.BloomThresholdFor(Color.black, 0f), Is.EqualTo(0.45f).Within(1e-4f));
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
