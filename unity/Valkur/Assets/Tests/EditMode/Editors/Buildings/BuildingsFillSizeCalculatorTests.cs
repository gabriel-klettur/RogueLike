using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Buildings;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Unit tests for <see cref="BuildingsFillSizeCalculator"/>.
    ///
    /// Covers the per-instance scale computation used by the FILL TOOL — OPTIONS dialog
    /// when the "Random size per building" checkbox is enabled, with and without a
    /// cluster-proximity hint from the Groves placement strategy.
    /// </summary>
    [TestFixture]
    public class BuildingsFillSizeCalculatorTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Build a deterministic RNG for repeatable assertions.</summary>
        private static System.Random Rng(int seed) => new System.Random(seed);

        // ── randomSize == false ───────────────────────────────────────────────────

        [Test]
        public void ComputeScaleFactor_RandomSizeOff_ReturnsOne()
        {
            float s = BuildingsFillSizeCalculator.ComputeScaleFactor(
                randomSize: false, sizeMinPct: 50, sizeMaxPct: 200, clusterHint: null, rng: Rng(0));
            Assert.That(s, Is.EqualTo(1f));
        }

        [Test]
        public void ComputeScaleOverride_RandomSizeOff_ReturnsVectorZero()
        {
            var v = BuildingsFillSizeCalculator.ComputeScaleOverride(
                randomSize: false, sizeMinPct: 50, sizeMaxPct: 200,
                templateOriginalScale: new Vector2Int(64, 96),
                clusterHint: null, rng: Rng(0));
            Assert.That(v, Is.EqualTo(Vector2Int.zero),
                "When randomSize is false, calculator must return zero so the caller " +
                "uses the template's original scale (BuildingObject.Apply convention).");
        }

        // ── No cluster hint: uniform random in [min, max] ─────────────────────────

        [Test]
        public void ComputeScaleFactor_NoHint_AlwaysWithinRange()
        {
            int min = 60, max = 140;
            for (int seed = 0; seed < 200; seed++)
            {
                float s = BuildingsFillSizeCalculator.ComputeScaleFactor(
                    randomSize: true, sizeMinPct: min, sizeMaxPct: max,
                    clusterHint: null, rng: Rng(seed));
                Assert.That(s, Is.InRange(min / 100f, max / 100f),
                    $"seed={seed} produced {s}, outside [{min/100f}, {max/100f}]");
            }
        }

        [Test]
        public void ComputeScaleFactor_NoHint_DeterministicWithSameSeed()
        {
            float a = BuildingsFillSizeCalculator.ComputeScaleFactor(
                true, 50, 200, null, Rng(42));
            float b = BuildingsFillSizeCalculator.ComputeScaleFactor(
                true, 50, 200, null, Rng(42));
            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void ComputeScaleFactor_NoHint_DistributedAcrossRange()
        {
            // With 100 samples uniformly distributed in [0.5, 1.5], we expect both
            // halves of the range to be hit — proves we're not collapsing to one end.
            int min = 50, max = 150;
            int below1 = 0, above1 = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                float s = BuildingsFillSizeCalculator.ComputeScaleFactor(
                    true, min, max, null, Rng(seed));
                if (s < 1f) below1++;
                else        above1++;
            }
            Assert.That(below1, Is.GreaterThan(20),  "Distribution skewed: too few values < 1.0");
            Assert.That(above1, Is.GreaterThan(20),  "Distribution skewed: too few values >= 1.0");
        }

        // ── With cluster hint: lerp + jitter ──────────────────────────────────────

        [Test]
        public void ComputeScaleFactor_HintOne_StaysNearMax()
        {
            // hint = 1.0 → baseS = max. Jitter is up to ±10% of (max-min).
            // For min=50, max=150 (range 100%): jitter ∈ [-10pp, +10pp] → factor in [1.4, 1.5] (clamped).
            for (int seed = 0; seed < 50; seed++)
            {
                float s = BuildingsFillSizeCalculator.ComputeScaleFactor(
                    true, 50, 150, clusterHint: 1f, rng: Rng(seed));
                Assert.That(s, Is.InRange(1.4f, 1.5f),
                    $"seed={seed}: expected near-max, got {s}");
            }
        }

        [Test]
        public void ComputeScaleFactor_HintZero_StaysNearMin()
        {
            // hint = 0.0 → baseS = min. Jitter is up to ±10% of (max-min).
            // For min=50, max=150: factor in [0.5, 0.6] (clamped at lower end).
            for (int seed = 0; seed < 50; seed++)
            {
                float s = BuildingsFillSizeCalculator.ComputeScaleFactor(
                    true, 50, 150, clusterHint: 0f, rng: Rng(seed));
                Assert.That(s, Is.InRange(0.5f, 0.6f),
                    $"seed={seed}: expected near-min, got {s}");
            }
        }

        [Test]
        public void ComputeScaleFactor_HintHalf_AroundMidpoint()
        {
            // hint = 0.5 → baseS = (min+max)/2. Jitter ±10% of (max-min) range.
            // For min=50, max=150: midpoint=1.0, jitter window [-10pp, +10pp] → [0.9, 1.1].
            for (int seed = 0; seed < 50; seed++)
            {
                float s = BuildingsFillSizeCalculator.ComputeScaleFactor(
                    true, 50, 150, clusterHint: 0.5f, rng: Rng(seed));
                Assert.That(s, Is.InRange(0.9f, 1.1f),
                    $"seed={seed}: expected mid+jitter, got {s}");
            }
        }

        [Test]
        public void ComputeScaleFactor_HintBelowZero_ClampedTo01()
        {
            // hint = -0.5 should be treated like 0.0.
            float withNegative = BuildingsFillSizeCalculator.ComputeScaleFactor(
                true, 50, 150, clusterHint: -0.5f, rng: Rng(7));
            float withZero = BuildingsFillSizeCalculator.ComputeScaleFactor(
                true, 50, 150, clusterHint: 0f, rng: Rng(7));
            Assert.That(withNegative, Is.EqualTo(withZero).Within(1e-5f));
        }

        [Test]
        public void ComputeScaleFactor_HintAboveOne_ClampedTo01()
        {
            // hint = 1.7 should be treated like 1.0.
            float withSuper = BuildingsFillSizeCalculator.ComputeScaleFactor(
                true, 50, 150, clusterHint: 1.7f, rng: Rng(7));
            float withOne = BuildingsFillSizeCalculator.ComputeScaleFactor(
                true, 50, 150, clusterHint: 1f, rng: Rng(7));
            Assert.That(withSuper, Is.EqualTo(withOne).Within(1e-5f));
        }

        [Test]
        public void ComputeScaleFactor_HintDeterministic_WithSameSeed()
        {
            float a = BuildingsFillSizeCalculator.ComputeScaleFactor(
                true, 50, 150, clusterHint: 0.5f, rng: Rng(99));
            float b = BuildingsFillSizeCalculator.ComputeScaleFactor(
                true, 50, 150, clusterHint: 0.5f, rng: Rng(99));
            Assert.That(b, Is.EqualTo(a));
        }

        // ── Min > Max: silent swap ────────────────────────────────────────────────

        [Test]
        public void ComputeScaleFactor_MinGreaterThanMax_SilentlySwapped()
        {
            // Caller passed (200, 50) — we still produce a factor in [0.5, 2.0].
            for (int seed = 0; seed < 50; seed++)
            {
                float s = BuildingsFillSizeCalculator.ComputeScaleFactor(
                    true, 200, 50, null, Rng(seed));
                Assert.That(s, Is.InRange(0.5f, 2.0f));
            }
        }

        // ── Pixel-space output ────────────────────────────────────────────────────

        [Test]
        public void ComputeScaleOverride_ProducesPositiveDimensions()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var v = BuildingsFillSizeCalculator.ComputeScaleOverride(
                    randomSize: true, sizeMinPct: 50, sizeMaxPct: 200,
                    templateOriginalScale: new Vector2Int(128, 256),
                    clusterHint: null, rng: Rng(seed));
                Assert.That(v.x, Is.GreaterThanOrEqualTo(1), "Width must be >= 1");
                Assert.That(v.y, Is.GreaterThanOrEqualTo(1), "Height must be >= 1");
            }
        }

        [Test]
        public void ComputeScaleOverride_ScalesProportionally()
        {
            // Forced factor at the minimum: hint=0, jitter still applies.
            // For min=100, max=100 (zero range), factor must be exactly 1.0 → output = template.
            var v = BuildingsFillSizeCalculator.ComputeScaleOverride(
                randomSize: true, sizeMinPct: 100, sizeMaxPct: 100,
                templateOriginalScale: new Vector2Int(64, 96),
                clusterHint: null, rng: Rng(1));
            Assert.That(v, Is.EqualTo(new Vector2Int(64, 96)),
                "Zero-range min/max should produce exactly the template's original scale.");
        }

        [Test]
        public void ComputeScaleOverride_TinyTemplate_ClampsToOnePixelMin()
        {
            // 1×1 template at 50% scale would round to 0×0 — must be clamped to 1×1.
            var v = BuildingsFillSizeCalculator.ComputeScaleOverride(
                randomSize: true, sizeMinPct: 50, sizeMaxPct: 50,
                templateOriginalScale: new Vector2Int(1, 1),
                clusterHint: null, rng: Rng(1));
            Assert.That(v.x, Is.GreaterThanOrEqualTo(1));
            Assert.That(v.y, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void ComputeScaleOverride_NullRng_ReturnsTemplateOnRandomSizeOn()
        {
            // Defensive path: a null rng must not crash. The factor is 1.0 → output equals template.
            var v = BuildingsFillSizeCalculator.ComputeScaleOverride(
                randomSize: true, sizeMinPct: 50, sizeMaxPct: 200,
                templateOriginalScale: new Vector2Int(80, 80),
                clusterHint: null, rng: null);
            Assert.That(v, Is.EqualTo(new Vector2Int(80, 80)));
        }
    }
}
