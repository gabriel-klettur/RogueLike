namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Lightweight deterministic value-noise sampler. Hashes the integer
    /// lattice points around (x, y), interpolates them with a
    /// smoothstep curve, and returns a float in [0, 1].
    ///
    /// Why hand-rolled (instead of <c>Mathf.PerlinNoise</c>):
    ///   - Unity's PerlinNoise is documented as "approximately in 0..1" and
    ///     not bit-stable across versions / platforms. Phase 4 lockstep
    ///     determinism cannot rely on it.
    ///   - This implementation uses only integer hashing + float
    ///     interpolation; same seed + same coord = same float on every
    ///     platform Unity targets (with the float caveat — see
    ///     CLAUDE.md "Determinism cross-platform" tradeoff).
    ///   - Cheap. Each sample is two hashes + four lookups + one lerp.
    /// </summary>
    public sealed class ValueNoise2D : INoiseSampler
    {
        private readonly int _seed;

        public ValueNoise2D(int seed) { _seed = seed; }

        public float Sample(float x, float y)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);
            float xf = x - xi;
            float yf = y - yi;

            float n00 = HashToUnit(xi,     yi);
            float n10 = HashToUnit(xi + 1, yi);
            float n01 = HashToUnit(xi,     yi + 1);
            float n11 = HashToUnit(xi + 1, yi + 1);

            float u = Smoothstep(xf);
            float v = Smoothstep(yf);

            float nx0 = n00 + (n10 - n00) * u;
            float nx1 = n01 + (n11 - n01) * u;
            return nx0 + (nx1 - nx0) * v;
        }

        private float HashToUnit(int xi, int yi)
        {
            // 32-bit integer hash adapted from Wang. Folds the seed in so
            // two ValueNoise2D instances with different seeds produce
            // independent streams.
            unchecked
            {
                uint h = (uint)(_seed ^ (xi * 374761393) ^ (yi * 668265263));
                h = (h ^ (h >> 13)) * 1274126177u;
                h = h ^ (h >> 16);
                return (h & 0x00FFFFFFu) / (float)0x01000000;
            }
        }

        private static int FastFloor(float v) => v >= 0 ? (int)v : (int)v - 1;
        private static float Smoothstep(float t) => t * t * (3f - 2f * t);
    }
}
