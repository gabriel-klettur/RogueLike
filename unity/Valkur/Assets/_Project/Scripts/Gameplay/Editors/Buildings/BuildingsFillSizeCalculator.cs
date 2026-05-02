using UnityEngine;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Pure-static helper that computes the per-instance scale override for a
    /// building placed by the Fill tool's "Random size per building" feature.
    ///
    /// Two paths:
    ///   • No cluster hint  → scale factor uniformly random in [minPct, maxPct] / 100.
    ///   • With cluster hint → scale factor = lerp(min, max, hint) ± jitter
    ///                        where hint=1 means cluster center (max), hint=0 means
    ///                        spread fringe (min). Jitter is ±10% of the (max - min) range.
    ///
    /// All randomness comes from the supplied <see cref="System.Random"/> instance,
    /// making outputs deterministic for a given seed.
    /// </summary>
    public static class BuildingsFillSizeCalculator
    {
        /// <summary>Maximum jitter fraction (relative to the min..max range).</summary>
        public const float JITTER_FRACTION = 0.10f;

        /// <summary>
        /// Compute the scalar scale factor (1.0 = template original size).
        /// Returns 1.0 when <paramref name="randomSize"/> is false.
        ///
        /// • <paramref name="sizeMinPct"/> / <paramref name="sizeMaxPct"/> are clamped at
        ///   [1, 1000] for safety; the caller (UI) typically passes [20, 300].
        /// • If min &gt; max they are silently swapped.
        /// • <paramref name="clusterHint"/> is clamped to [0, 1] when provided.
        /// </summary>
        public static float ComputeScaleFactor(
            bool randomSize,
            int sizeMinPct,
            int sizeMaxPct,
            float? clusterHint,
            System.Random rng)
        {
            if (!randomSize) return 1f;
            if (rng == null) return 1f;

            // Sanitize inputs.
            int minPct = Mathf.Clamp(sizeMinPct, 1, 1000);
            int maxPct = Mathf.Clamp(sizeMaxPct, 1, 1000);
            if (minPct > maxPct) { int tmp = minPct; minPct = maxPct; maxPct = tmp; }

            float minF = minPct / 100f;
            float maxF = maxPct / 100f;
            float range = maxF - minF;

            if (clusterHint.HasValue)
            {
                float hint   = Mathf.Clamp01(clusterHint.Value);
                float baseS  = Mathf.Lerp(minF, maxF, hint);
                // Jitter is in [-0.5, +0.5] * (range * 2 * JITTER_FRACTION) → [-J*range, +J*range].
                float jitter = (float)(rng.NextDouble() * (2.0 * JITTER_FRACTION) - JITTER_FRACTION) * range;
                return Mathf.Clamp(baseS + jitter, minF, maxF);
            }

            return Mathf.Lerp(minF, maxF, (float)rng.NextDouble());
        }

        /// <summary>
        /// Compute the integer pixel-space scale override that should be passed to
        /// <c>BuildingObject.Apply(template, scaleOverride, splitRatioOverride)</c>.
        ///
        /// • Returns <see cref="Vector2Int.zero"/> when <paramref name="randomSize"/>
        ///   is false — the caller-side convention for "use the template's original scale".
        /// • Each axis is rounded to nearest integer and clamped to a minimum of 1
        ///   so a degenerate (0×0) building is never produced.
        /// </summary>
        public static Vector2Int ComputeScaleOverride(
            bool randomSize,
            int sizeMinPct,
            int sizeMaxPct,
            Vector2Int templateOriginalScale,
            float? clusterHint,
            System.Random rng)
        {
            if (!randomSize) return Vector2Int.zero;

            float s = ComputeScaleFactor(randomSize, sizeMinPct, sizeMaxPct, clusterHint, rng);

            int w = Mathf.Max(1, Mathf.RoundToInt(templateOriginalScale.x * s));
            int h = Mathf.Max(1, Mathf.RoundToInt(templateOriginalScale.y * s));
            return new Vector2Int(w, h);
        }
    }
}
