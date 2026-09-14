using Valkur.Data.Chunks;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Fractal (fBm) noise: several octaves of <see cref="ValueNoise2D"/>, each at double the
    /// frequency and half the amplitude of the last.
    ///
    /// <para>Built on the project's own value noise rather than <c>Mathf.PerlinNoise</c>, which
    /// Unity does not promise to keep stable between versions — a seed that stopped producing
    /// the same world after an engine upgrade would be a broken seed.</para>
    ///
    /// <para><b>The output is re-stretched.</b> Summing octaves pulls values towards 0.5 (the
    /// central limit theorem), so raw fBm rarely leaves 0.25..0.75 and a "sea level 0.3" dial
    /// would do almost nothing. <see cref="Contrast"/> spreads it back over 0..1.</para>
    /// </summary>
    public sealed class FractalNoise2D : INoiseSampler
    {
        private const float Lacunarity = 2f;
        private const float Gain = 0.5f;

        /// <summary>
        /// Spread applied around 0.5. <c>FractalNoise2DTests</c> pins that the stretched output
        /// really does cover most of 0..1, which is the only property the dials depend on.
        /// </summary>
        public const float Contrast = 1.9f;

        private readonly ValueNoise2D[] _octaves;
        private readonly float _inverseAmplitude;

        public int Octaves => _octaves.Length;

        public FractalNoise2D(int seed, int octaves)
        {
            if (octaves < 1) octaves = 1;
            _octaves = new ValueNoise2D[octaves];

            float amplitude = 1f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                // A seed per octave, or every octave would repeat the same lattice at a new scale
                // and the self-similarity shows as a visible grid.
                _octaves[i] = new ValueNoise2D(WorldSeed.Derive(seed, i + 101));
                total += amplitude;
                amplitude *= Gain;
            }
            _inverseAmplitude = 1f / total;
        }

        /// <summary>In 0..1 (clamped after the contrast stretch).</summary>
        public float Sample(float x, float y)
        {
            float sum = 0f, amplitude = 1f, frequency = 1f;
            for (int i = 0; i < _octaves.Length; i++)
            {
                sum += _octaves[i].Sample(x * frequency, y * frequency) * amplitude;
                amplitude *= Gain;
                frequency *= Lacunarity;
            }

            float v = (sum * _inverseAmplitude - 0.5f) * Contrast + 0.5f;
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
