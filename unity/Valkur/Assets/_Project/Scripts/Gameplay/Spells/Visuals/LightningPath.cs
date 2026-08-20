using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Fractal midpoint displacement for a lightning arc.
    ///
    /// The old bolt jittered every intermediate point independently around the straight
    /// line, which produces a uniform saw-tooth: every kink is the same size and the whole
    /// thing reads as a zig-zag ribbon rather than electricity. Real lightning is
    /// self-similar — a few large deviations, each carrying progressively smaller ones. That
    /// is exactly what recursive midpoint displacement gives, for the same cost.
    ///
    /// The buffer is caller-owned and rewritten in place, so a bolt can re-roll its shape
    /// every other frame without allocating.
    /// </summary>
    internal static class LightningPath
    {
        /// <summary>Subdivision levels. Point count is 2^levels + 1.</summary>
        public const int LEVELS = 5;

        /// <summary>Points a buffer must hold for <see cref="LEVELS"/> subdivisions.</summary>
        public const int POINT_COUNT = (1 << LEVELS) + 1;

        /// <summary>Fraction of the displacement that survives into the next level.</summary>
        private const float ROUGHNESS = 0.62f;

        /// <summary>
        /// Fills <paramref name="points"/> with a bolt from <paramref name="from"/> to
        /// <paramref name="to"/>. <paramref name="displacement"/> is the half-width of the
        /// largest possible deviation, as a fraction of the bolt's length.
        /// </summary>
        public static void Generate(Vector3[] points, Vector3 from, Vector3 to, float displacement)
        {
            points[0] = from;
            points[POINT_COUNT - 1] = to;

            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.0001f) length = 0.0001f;
            Vector3 perpendicular = new Vector3(-delta.y, delta.x, 0f) / length;

            float offset = displacement * length;
            int stride = POINT_COUNT - 1;

            while (stride > 1)
            {
                int half = stride / 2;
                for (int i = half; i < POINT_COUNT; i += stride)
                {
                    Vector3 midpoint = (points[i - half] + points[i + half]) * 0.5f;
                    points[i] = midpoint + perpendicular * Random.Range(-offset, offset);
                }
                stride = half;
                offset *= ROUGHNESS;
            }

            // Both ends are anchored: the arc leaves the caster's hand and lands on its
            // target, and a deviation there would read as the bolt missing.
            points[0] = from;
            points[POINT_COUNT - 1] = to;
        }

        /// <summary>
        /// Fills <paramref name="points"/> with a fork leaving the main bolt at
        /// <paramref name="origin"/>. Forks are short, angled away from the parent
        /// direction, and never reach the target — they are the discharge that failed.
        /// </summary>
        public static void GenerateFork(Vector3[] points, Vector3 origin, Vector3 parentDirection,
                                        float length, float displacement)
        {
            float angle = Random.Range(18f, 46f) * (Random.value < 0.5f ? -1f : 1f);
            Vector3 direction = Quaternion.Euler(0f, 0f, angle) * parentDirection;
            Generate(points, origin, origin + direction * length, displacement);
        }
    }
}
