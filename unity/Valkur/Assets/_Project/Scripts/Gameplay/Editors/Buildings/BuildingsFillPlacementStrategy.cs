using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Pure-static placement-strategy helpers for the Buildings Fill tool.
    ///
    /// Three strategies (mirrors the planned Python building_fill_tool.py extension):
    ///   • Uniform — pass-through; all flood-fill cells are candidates (existing behavior).
    ///   • Groves  — Gaussian cluster acceptance: pick K random centers, accept each cell
    ///               with probability proportional to its proximity to the nearest center.
    ///               Returns size-hint values (0..1, 1=cluster center) alongside accepted cells
    ///               for use by the per-tree size-correlation feature.
    ///   • Noise   — Perlin noise density mask: accept cells whose Perlin sample > threshold.
    ///               Random offset derived from seed for repeatable-but-varied results.
    ///
    /// All methods use <see cref="System.Random"/> seeded deterministically so the same
    /// seed always produces the same output (preview is stable when the cursor doesn't move).
    /// No Unity MonoBehaviour or scene dependencies — fully unit-testable.
    /// </summary>
    public static class BuildingsFillPlacementStrategy
    {
        // ── Result types ─────────────────────────────────────────────────────────

        /// <summary>
        /// Output from <see cref="ApplyGroves"/>: accepted cells plus per-cell size hints.
        /// </summary>
        public struct GrovesResult
        {
            /// <summary>Accepted cell positions (subset of the input).</summary>
            public HashSet<Vector3Int> cells;

            /// <summary>
            /// Per-cell proximity hint: 1.0 = at cluster center, 0.0 = at spread radius.
            /// Only populated for cells in <see cref="cells"/>; absent keys have no hint.
            /// </summary>
            public Dictionary<Vector3Int, float> sizeHints;
        }

        // ── Uniform ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Pass-through: returns a new HashSet containing all input cells.
        /// Returns empty when <paramref name="cells"/> is null or empty.
        /// </summary>
        public static HashSet<Vector3Int> ApplyUniform(IEnumerable<Vector3Int> cells)
        {
            var result = new HashSet<Vector3Int>();
            if (cells == null) return result;
            foreach (var c in cells) result.Add(c);
            return result;
        }

        // ── Groves ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Gaussian cluster (groves) placement.
        ///
        /// Algorithm:
        ///   1. Pick <paramref name="clusterCount"/> random centers from within the bounding
        ///      box of the input cells using <see cref="System.Random"/>.
        ///   2. For each input cell, find the nearest center and compute distance d.
        ///   3. Accept the cell with probability p = exp(-(d²) / (2 · σ²))
        ///      where σ = spreadTiles / 2.
        ///   4. For accepted cells, record hint = clamp(1 - d / spreadTiles, 0, 1).
        ///
        /// Edge cases:
        ///   • null/empty input → empty result.
        ///   • clusterCount ≤ 0 → empty result.
        ///   • spreadTiles ≤ 0 → empty result.
        ///   • 1–2 input cells → return all with hint = 1 (degenerate, no meaningful spread).
        /// </summary>
        public static GrovesResult ApplyGroves(
            IEnumerable<Vector3Int> cells,
            int   clusterCount,
            float spreadTiles,
            int   seed)
        {
            var result = new GrovesResult
            {
                cells     = new HashSet<Vector3Int>(),
                sizeHints = new Dictionary<Vector3Int, float>()
            };

            if (cells == null)         return result;
            if (clusterCount <= 0)     return result;
            if (spreadTiles   <= 0f)   return result;

            // Materialise input so we can measure it and index it.
            var cellList = new List<Vector3Int>(cells);
            if (cellList.Count == 0)   return result;

            // Degenerate: too few cells to form meaningful clusters — return all with hint = 1.
            if (cellList.Count <= 2)
            {
                foreach (var c in cellList)
                {
                    result.cells.Add(c);
                    result.sizeHints[c] = 1f;
                }
                return result;
            }

            // Compute bounding box.
            int minX = cellList[0].x, maxX = cellList[0].x;
            int minY = cellList[0].y, maxY = cellList[0].y;
            for (int i = 1; i < cellList.Count; i++)
            {
                if (cellList[i].x < minX) minX = cellList[i].x;
                if (cellList[i].x > maxX) maxX = cellList[i].x;
                if (cellList[i].y < minY) minY = cellList[i].y;
                if (cellList[i].y > maxY) maxY = cellList[i].y;
            }

            var rng = new System.Random(seed);

            // Pick cluster centers (as float world-space x/y, using cell integer coords directly).
            float rangeX = (float)(maxX - minX);
            float rangeY = (float)(maxY - minY);

            var centers = new Vector2[clusterCount];
            for (int k = 0; k < clusterCount; k++)
            {
                centers[k] = new Vector2(
                    minX + (float)(rng.NextDouble() * rangeX),
                    minY + (float)(rng.NextDouble() * rangeY));
            }

            // σ for the Gaussian.
            float sigma  = spreadTiles / 2f;
            float twoSig2 = 2f * sigma * sigma;

            foreach (var cell in cellList)
            {
                // Find nearest center distance.
                float cx = cell.x, cy = cell.y;
                float minDist = float.MaxValue;
                for (int k = 0; k < clusterCount; k++)
                {
                    float dx = cx - centers[k].x;
                    float dy = cy - centers[k].y;
                    float d  = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < minDist) minDist = d;
                }

                // Gaussian acceptance probability.
                float prob = Mathf.Exp(-(minDist * minDist) / twoSig2);
                if ((float)rng.NextDouble() < prob)
                {
                    result.cells.Add(cell);
                    float hint = Mathf.Clamp01(1f - minDist / spreadTiles);
                    result.sizeHints[cell] = hint;
                }
            }

            return result;
        }

        // ── Noise ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Perlin noise density mask.
        ///
        /// For each cell, samples <see cref="Mathf.PerlinNoise"/>(cell.x * scale + ox, cell.y * scale + oy)
        /// where ox/oy are pseudo-random offsets derived from <paramref name="seed"/>.
        /// Cells whose sample value exceeds <paramref name="threshold"/> are accepted.
        ///
        /// Edge cases:
        ///   • null/empty input → empty result.
        ///   • noiseScale ≤ 0 → empty result (degenerate frequency).
        ///   • threshold ≥ 1 → all cells rejected (empty result).
        /// </summary>
        public static HashSet<Vector3Int> ApplyNoise(
            IEnumerable<Vector3Int> cells,
            float noiseScale,
            float threshold,
            int   seed)
        {
            var result = new HashSet<Vector3Int>();
            if (cells == null)      return result;
            if (noiseScale <= 0f)   return result;
            if (threshold  >= 1f)   return result;

            // Derive pseudo-random offsets from seed.
            var rng = new System.Random(seed);
            float ox = (float)(rng.NextDouble() * 1000.0);
            float oy = (float)(rng.NextDouble() * 1000.0);

            foreach (var cell in cells)
            {
                float sample = Mathf.PerlinNoise(
                    cell.x * noiseScale + ox,
                    cell.y * noiseScale + oy);
                if (sample > threshold)
                    result.Add(cell);
            }

            return result;
        }
    }
}
