using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Feel;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins the constant that makes every authored shake amplitude mean what it says.
    ///
    /// <c>Mathf.PerlinNoise</c> is documented to return [0,1] but in practice never reaches
    /// either end — its realised range is roughly ±0.7 after the standard
    /// <c>2·n − 1</c> remap. Without a normalisation constant measured against the real
    /// generator, an authored "0.42 world units of shake at full trauma" silently delivers
    /// about 0.29, and every amplitude in the profile is a guess about a guess.
    ///
    /// This fixture measures the realised peak and fails with the corrected constant in the
    /// message, so calibrating is a copy-paste rather than an investigation.
    /// </summary>
    [TestFixture]
    public class CameraFeelNoiseCalibrationTests
    {
        /// <summary>
        /// The value shipped in <c>CameraFeelProfile</c>. Change both together.
        ///
        /// Measured, not chosen: Perlin's 99th-percentile magnitude on this generator is
        /// 0.8084, so 1/0.8084 is what makes an authored amplitude arrive at full strength.
        /// The design estimate was 1.35, which would have run every shake in the game 9%
        /// hot and pushed 5% of samples into the clamp.
        /// </summary>
        private const float SHIPPED_NORMALISATION = 1.2370f;

        private const float SEED_X = 11.3f;
        private const float SEED_Y = 47.9f;
        private const int SAMPLES = 20000;
        private const float SPAN_SECONDS = 400f;
        private const float FREQUENCY_HZ = 24f;

        [Test]
        public void ShakeSample_IsCalibratedToTheGenerator()
        {
            // Calibrating against the absolute maximum is the wrong target: Perlin's extreme
            // is rare, so normalising to it leaves the shake quiet almost all of the time,
            // while normalising past it clips the signal into a square wave. The 99th
            // percentile is the value that makes a typical peak land at full amplitude and
            // costs about one sample in a hundred to the clamp.
            var magnitudes = new System.Collections.Generic.List<float>(SAMPLES * 2);
            for (int i = 0; i < SAMPLES; i++)
            {
                float t = i * (SPAN_SECONDS / SAMPLES);
                Vector2 raw = CameraFeelMath.ShakeSample(SEED_X, SEED_Y, t, FREQUENCY_HZ, 1f);
                magnitudes.Add(Mathf.Abs(raw.x));
                magnitudes.Add(Mathf.Abs(raw.y));
            }
            magnitudes.Sort();

            float p99 = magnitudes[Mathf.FloorToInt(magnitudes.Count * 0.99f)];
            float corrected = p99 > 0.0001f ? 1f / p99 : 1f;

            Assert.AreEqual(corrected, SHIPPED_NORMALISATION, corrected * 0.01f,
                $"Perlin's 99th-percentile magnitude is {p99:F4}, so noiseNormalisation should " +
                $"be {corrected:F4}. Set it in both CameraFeelProfile and " +
                "SHIPPED_NORMALISATION here, then re-run. Do not tune any amplitude until " +
                "this is green — every one of them is measured against this constant.");
        }

        [Test]
        public void ShakeSample_IsNotClippedIntoASquareWave()
        {
            int clipped = 0;
            for (int i = 0; i < SAMPLES; i++)
            {
                float t = i * (SPAN_SECONDS / SAMPLES);
                Vector2 s = CameraFeelMath.ShakeSample(SEED_X, SEED_Y, t, FREQUENCY_HZ,
                                                       SHIPPED_NORMALISATION);
                if (Mathf.Abs(s.x) >= 0.999f || Mathf.Abs(s.y) >= 0.999f) clipped++;
            }

            float fraction = clipped / (float)SAMPLES;
            Assert.Less(fraction, 0.03f,
                $"{fraction:P1} of samples are clamped. Normalising hard enough to hit a peak " +
                "of 1 by flattening the top turns smooth shake back into the harsh, uniform " +
                "rattle this system exists to replace.");
        }
    }
}
