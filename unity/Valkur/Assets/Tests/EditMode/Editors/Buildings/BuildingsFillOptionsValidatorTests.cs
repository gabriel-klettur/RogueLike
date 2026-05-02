using NUnit.Framework;
using Valkur.Gameplay.Buildings;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Unit tests for <see cref="BuildingsFillOptionsValidator"/>.
    ///
    /// These tests describe and lock the input-clamping contract used by the FILL
    /// TOOL — OPTIONS dialog: every text-field value entered by the user is forced
    /// into a documented valid range before being applied.
    /// </summary>
    [TestFixture]
    public class BuildingsFillOptionsValidatorTests
    {
        // ── Spacing ───────────────────────────────────────────────────────────────

        [Test]
        public void ClampSpacing_BelowMin_ReturnsMin()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampSpacing(-5),
                Is.EqualTo(BuildingsFillOptionsValidator.SPACING_MIN));
        }

        [Test]
        public void ClampSpacing_AboveMax_ReturnsMax()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampSpacing(999),
                Is.EqualTo(BuildingsFillOptionsValidator.SPACING_MAX));
        }

        [Test]
        public void ClampSpacing_Within_ReturnsAsIs()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampSpacing(7), Is.EqualTo(7));
        }

        [Test]
        public void ClampSpacing_AtBoundaries_ReturnsBoundary()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampSpacing(
                BuildingsFillOptionsValidator.SPACING_MIN),
                Is.EqualTo(BuildingsFillOptionsValidator.SPACING_MIN));
            Assert.That(BuildingsFillOptionsValidator.ClampSpacing(
                BuildingsFillOptionsValidator.SPACING_MAX),
                Is.EqualTo(BuildingsFillOptionsValidator.SPACING_MAX));
        }

        // ── Size range ────────────────────────────────────────────────────────────

        [Test]
        public void ClampSizeRange_NormalInputs_PassThrough()
        {
            var (lo, hi) = BuildingsFillOptionsValidator.ClampSizeRange(80, 120);
            Assert.That(lo, Is.EqualTo(80));
            Assert.That(hi, Is.EqualTo(120));
        }

        [Test]
        public void ClampSizeRange_MinGreaterThanMax_SwapsValues()
        {
            var (lo, hi) = BuildingsFillOptionsValidator.ClampSizeRange(150, 80);
            Assert.That(lo, Is.EqualTo(80));
            Assert.That(hi, Is.EqualTo(150));
        }

        [Test]
        public void ClampSizeRange_BothBelowMin_BothBecomeMin()
        {
            var (lo, hi) = BuildingsFillOptionsValidator.ClampSizeRange(5, 10);
            Assert.That(lo, Is.EqualTo(BuildingsFillOptionsValidator.SIZE_PCT_MIN));
            Assert.That(hi, Is.EqualTo(BuildingsFillOptionsValidator.SIZE_PCT_MIN));
        }

        [Test]
        public void ClampSizeRange_BothAboveMax_BothBecomeMax()
        {
            var (lo, hi) = BuildingsFillOptionsValidator.ClampSizeRange(500, 1000);
            Assert.That(lo, Is.EqualTo(BuildingsFillOptionsValidator.SIZE_PCT_MAX));
            Assert.That(hi, Is.EqualTo(BuildingsFillOptionsValidator.SIZE_PCT_MAX));
        }

        [Test]
        public void ClampSizeRange_MinBelowAndMaxAbove_ClampsBoth()
        {
            var (lo, hi) = BuildingsFillOptionsValidator.ClampSizeRange(-50, 9999);
            Assert.That(lo, Is.EqualTo(BuildingsFillOptionsValidator.SIZE_PCT_MIN));
            Assert.That(hi, Is.EqualTo(BuildingsFillOptionsValidator.SIZE_PCT_MAX));
        }

        [Test]
        public void ClampSizeRange_EqualValues_ReturnedEqual()
        {
            var (lo, hi) = BuildingsFillOptionsValidator.ClampSizeRange(100, 100);
            Assert.That(lo, Is.EqualTo(100));
            Assert.That(hi, Is.EqualTo(100));
        }

        [Test]
        public void ClampSizeRange_AtBoundariesInverted_StillSwapsCleanly()
        {
            // Caller enters Min=300, Max=20 (invalid order, but at exact boundaries).
            var (lo, hi) = BuildingsFillOptionsValidator.ClampSizeRange(
                BuildingsFillOptionsValidator.SIZE_PCT_MAX,
                BuildingsFillOptionsValidator.SIZE_PCT_MIN);
            Assert.That(lo, Is.EqualTo(BuildingsFillOptionsValidator.SIZE_PCT_MIN));
            Assert.That(hi, Is.EqualTo(BuildingsFillOptionsValidator.SIZE_PCT_MAX));
        }

        // ── Grove count / spread ──────────────────────────────────────────────────

        [Test]
        public void ClampGroveCount_OutOfRange_Clamps()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampGroveCount(0),
                Is.EqualTo(BuildingsFillOptionsValidator.GROVE_COUNT_MIN));
            Assert.That(BuildingsFillOptionsValidator.ClampGroveCount(99),
                Is.EqualTo(BuildingsFillOptionsValidator.GROVE_COUNT_MAX));
        }

        [Test]
        public void ClampGroveCount_Within_PassThrough()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampGroveCount(5), Is.EqualTo(5));
        }

        [Test]
        public void ClampGroveSpread_OutOfRange_Clamps()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampGroveSpread(1),
                Is.EqualTo(BuildingsFillOptionsValidator.GROVE_SPREAD_MIN));
            Assert.That(BuildingsFillOptionsValidator.ClampGroveSpread(100),
                Is.EqualTo(BuildingsFillOptionsValidator.GROVE_SPREAD_MAX));
        }

        [Test]
        public void ClampGroveSpread_Within_PassThrough()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampGroveSpread(8), Is.EqualTo(8));
        }

        // ── Noise scale / threshold ───────────────────────────────────────────────

        [Test]
        public void ClampNoiseScale_BelowFloor_ReturnsFloor()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampNoiseScale(0f),
                Is.EqualTo(BuildingsFillOptionsValidator.NOISE_SCALE_MIN).Within(1e-6f));
            Assert.That(BuildingsFillOptionsValidator.ClampNoiseScale(-1f),
                Is.EqualTo(BuildingsFillOptionsValidator.NOISE_SCALE_MIN).Within(1e-6f));
        }

        [Test]
        public void ClampNoiseScale_AboveCeiling_ReturnsCeiling()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampNoiseScale(5f),
                Is.EqualTo(BuildingsFillOptionsValidator.NOISE_SCALE_MAX).Within(1e-6f));
        }

        [Test]
        public void ClampNoiseScale_AtExactBounds_PassesThrough()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampNoiseScale(
                BuildingsFillOptionsValidator.NOISE_SCALE_MIN),
                Is.EqualTo(BuildingsFillOptionsValidator.NOISE_SCALE_MIN).Within(1e-6f));
            Assert.That(BuildingsFillOptionsValidator.ClampNoiseScale(
                BuildingsFillOptionsValidator.NOISE_SCALE_MAX),
                Is.EqualTo(BuildingsFillOptionsValidator.NOISE_SCALE_MAX).Within(1e-6f));
        }

        [Test]
        public void ClampNoiseThreshold_BelowZero_ReturnsZero()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampNoiseThreshold(-2f),
                Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void ClampNoiseThreshold_AboveOne_ReturnsOne()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampNoiseThreshold(1.5f),
                Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void ClampNoiseThreshold_AtBounds_PassesThrough()
        {
            Assert.That(BuildingsFillOptionsValidator.ClampNoiseThreshold(0f),
                Is.EqualTo(0f).Within(1e-6f));
            Assert.That(BuildingsFillOptionsValidator.ClampNoiseThreshold(1f),
                Is.EqualTo(1f).Within(1e-6f));
        }

        // ── Constants sanity ──────────────────────────────────────────────────────

        [Test]
        public void Constants_HaveSaneRelationships()
        {
            // Defensive — if anyone reorders these constants in the future, this will catch it.
            Assert.That(BuildingsFillOptionsValidator.SPACING_MIN,
                Is.LessThan(BuildingsFillOptionsValidator.SPACING_MAX));
            Assert.That(BuildingsFillOptionsValidator.SIZE_PCT_MIN,
                Is.LessThan(BuildingsFillOptionsValidator.SIZE_PCT_MAX));
            Assert.That(BuildingsFillOptionsValidator.GROVE_COUNT_MIN,
                Is.LessThan(BuildingsFillOptionsValidator.GROVE_COUNT_MAX));
            Assert.That(BuildingsFillOptionsValidator.GROVE_SPREAD_MIN,
                Is.LessThan(BuildingsFillOptionsValidator.GROVE_SPREAD_MAX));
            Assert.That(BuildingsFillOptionsValidator.NOISE_SCALE_MIN,
                Is.LessThan(BuildingsFillOptionsValidator.NOISE_SCALE_MAX));
            Assert.That(BuildingsFillOptionsValidator.NOISE_THRESH_MIN,
                Is.LessThan(BuildingsFillOptionsValidator.NOISE_THRESH_MAX));
        }
    }
}
