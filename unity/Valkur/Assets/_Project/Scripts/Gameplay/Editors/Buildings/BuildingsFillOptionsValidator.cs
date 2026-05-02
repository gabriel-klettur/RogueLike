using UnityEngine;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Pure-static helper that clamps user-supplied input from the Fill tool's
    /// OPTIONS dialog into the canonical valid ranges used by the rest of the
    /// pipeline (placement strategy, spacing filter, size calculator).
    ///
    /// Centralising the bounds here makes the dialog code declarative and lets
    /// us unit-test the boundary behaviour without instantiating the runtime
    /// editor or any TMP_InputField.
    /// </summary>
    public static class BuildingsFillOptionsValidator
    {
        // Spacing (tiles)
        public const int   SPACING_MIN        = 1;
        public const int   SPACING_MAX        = 20;

        // Size variance (% of template.originalScale)
        public const int   SIZE_PCT_MIN       = 20;
        public const int   SIZE_PCT_MAX       = 300;

        // Groves placement
        public const int   GROVE_COUNT_MIN    = 1;
        public const int   GROVE_COUNT_MAX    = 10;
        public const int   GROVE_SPREAD_MIN   = 2;
        public const int   GROVE_SPREAD_MAX   = 20;

        // Noise placement
        public const float NOISE_SCALE_MIN    = 0.05f;
        public const float NOISE_SCALE_MAX    = 1.0f;
        public const float NOISE_THRESH_MIN   = 0f;
        public const float NOISE_THRESH_MAX   = 1f;

        // ── Spacing ───────────────────────────────────────────────────────────────

        public static int ClampSpacing(int v) => Mathf.Clamp(v, SPACING_MIN, SPACING_MAX);

        // ── Size variance ────────────────────────────────────────────────────────

        /// <summary>
        /// Clamp both ends of the size-variance range to <see cref="SIZE_PCT_MIN"/>..<see cref="SIZE_PCT_MAX"/>.
        /// If after clamping <c>min</c> exceeds <c>max</c>, the two values are swapped — this matches
        /// the dialog's tolerant behavior when a user enters Min=120, Max=80.
        /// </summary>
        public static (int min, int max) ClampSizeRange(int min, int max)
        {
            int lo = Mathf.Clamp(min, SIZE_PCT_MIN, SIZE_PCT_MAX);
            int hi = Mathf.Clamp(max, SIZE_PCT_MIN, SIZE_PCT_MAX);
            if (lo > hi) { int tmp = lo; lo = hi; hi = tmp; }
            return (lo, hi);
        }

        // ── Groves ────────────────────────────────────────────────────────────────

        public static int ClampGroveCount(int v)
            => Mathf.Clamp(v, GROVE_COUNT_MIN, GROVE_COUNT_MAX);

        public static int ClampGroveSpread(int v)
            => Mathf.Clamp(v, GROVE_SPREAD_MIN, GROVE_SPREAD_MAX);

        // ── Noise ─────────────────────────────────────────────────────────────────

        public static float ClampNoiseScale(float v)
            => Mathf.Clamp(v, NOISE_SCALE_MIN, NOISE_SCALE_MAX);

        public static float ClampNoiseThreshold(float v)
            => Mathf.Clamp(v, NOISE_THRESH_MIN, NOISE_THRESH_MAX);
    }
}
