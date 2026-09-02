using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The silhouette of one shard variant, sampled once per texture row.
    ///
    /// <para>Four maps (body, rim, facet, crack) have to agree on the SAME outline or the
    /// rim light floats off the edge and the cracks run outside the crystal. Sampling the
    /// profile once and handing the arrays to every builder is what guarantees that; each
    /// builder deriving the curve from the same formula would be four places to keep in
    /// step.</para>
    ///
    /// <para>The profile is deliberately not a smooth taper. A cone reads as a traffic
    /// cone; what reads as a crystal is a taper broken by two or three SHOULDERS where the
    /// width steps in, because that is where facets meet.</para>
    /// </summary>
    internal sealed class ShardShape
    {
        /// <summary>Half-width per row, as a fraction of texture width (0..0.5).</summary>
        public float[] HalfWidth;

        /// <summary>Centre offset per row, as a fraction of texture width. Gives the lean.</summary>
        public float[] Centre;

        /// <summary>Where the inner specular streak sits, in units of the row's half-width.</summary>
        public float StreakOffset;

        /// <summary>Offsets of the two baked fracture lines, in units of the row's half-width.</summary>
        public float FractureA, FractureB;

        public int Rows => HalfWidth.Length;

        public static ShardShape For(int variant, int rows)
        {
            // A fixed seed per variant: the five silhouettes must be identical in every
            // session, or a wall rebuilt after a reload would be made of different crystals.
            var rng = new System.Random(0x1CE + variant * 7919);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            float baseHalf = Range(0.30f, 0.44f);
            float taper = Range(0.70f, 1.20f);
            float lean = Range(-0.16f, 0.16f);
            float tipSharpness = Range(1.5f, 3.2f);

            // Two or three shoulders where the crystal steps inward.
            int shoulderCount = 2 + (variant % 2);
            var shoulderAt = new float[shoulderCount];
            var shoulderCut = new float[shoulderCount];
            for (int i = 0; i < shoulderCount; i++)
            {
                shoulderAt[i] = Mathf.Lerp(0.22f, 0.86f, (i + Range(0.15f, 0.85f)) / shoulderCount);
                shoulderCut[i] = Range(0.10f, 0.24f);
            }

            var shape = new ShardShape
            {
                HalfWidth = new float[rows],
                Centre = new float[rows],
                StreakOffset = Range(-0.45f, -0.15f),
                FractureA = Range(-0.55f, -0.10f),
                FractureB = Range(0.12f, 0.58f),
            };

            for (int y = 0; y < rows; y++)
            {
                float v = (y + 0.5f) / rows;                    // 0 at the base, 1 at the tip

                // Base taper, sharpened near the tip so the point is a point.
                float hw = baseHalf * Mathf.Pow(1f - v, taper);
                hw *= Mathf.Lerp(1f, Mathf.Pow(1f - v, tipSharpness * 0.35f), Mathf.SmoothStep(0f, 1f, (v - 0.7f) / 0.3f));

                // Shoulders: a smooth step inward at each facet junction.
                for (int i = 0; i < shoulderCount; i++)
                {
                    float step = Mathf.SmoothStep(0f, 1f, (v - shoulderAt[i]) / 0.06f);
                    hw *= 1f - shoulderCut[i] * step;
                }

                // The very bottom flares slightly: ice grows out of the ground, it is not
                // planted in it. Without this the shards read as posts.
                hw *= 1f + 0.22f * Mathf.Pow(Mathf.Clamp01(1f - v / 0.14f), 2f);

                shape.HalfWidth[y] = Mathf.Max(0f, hw);
                shape.Centre[y] = lean * Mathf.Pow(v, 1.35f);
            }

            return shape;
        }

        /// <summary>Signed inside-ness of a texel: &gt;0 inside the crystal, in width fractions.</summary>
        public float Inside(int row, float u, out float halfWidth, out float centre)
        {
            halfWidth = HalfWidth[row];
            centre = Centre[row];
            return halfWidth - Mathf.Abs(u - centre);
        }
    }
}
