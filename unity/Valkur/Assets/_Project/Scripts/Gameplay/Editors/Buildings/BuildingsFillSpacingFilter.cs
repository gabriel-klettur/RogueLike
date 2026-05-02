using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Pure-static, testable implementation of the greedy row-major spacing filter
    /// used by the Buildings Fill tool.
    ///
    /// Extracted from <see cref="BuildingsRuntimeEditor.ApplySpacingFilter"/> so that the
    /// algorithm can be unit-tested without a live editor session.
    ///
    /// Rules (mirrors Python building_fill_tool.py spacing logic):
    ///   • Sort candidates in row-major order: Y descending (top rows first), X ascending.
    ///   • Greedily accept each candidate whose world-center is at Euclidean distance
    ///     ≥ spacingTiles from EVERY already-accepted candidate AND from every existing
    ///     building position supplied by the caller.
    ///   • 1 tile = 1 world unit (tilemap cellSize assumed to be 1×1).
    ///   • The distance check is strict-less-than: a cell at exactly spacingTiles distance
    ///     is accepted (distance ≥ spacing).
    /// </summary>
    public static class BuildingsFillSpacingFilter
    {
        /// <summary>
        /// Apply the greedy row-major spacing filter to a set of candidate cells.
        /// </summary>
        /// <param name="candidates">
        /// The raw candidate cell positions (e.g. from a flood-fill). May be empty or null.
        /// </param>
        /// <param name="spacingTiles">
        /// Minimum distance in world units (tiles) between any two accepted placements.
        /// </param>
        /// <param name="tilemap">
        /// Used only to convert cell coordinates to world-space centers via
        /// <see cref="Tilemap.GetCellCenterWorld"/>. Must not be null when candidates is non-empty.
        /// </param>
        /// <param name="existingPositions">
        /// World-space XY positions of buildings that are already placed in the scene.
        /// Pass an empty collection (not null) when there are none.
        /// </param>
        /// <returns>
        /// Accepted cells in the order they were processed (row-major: Y desc, X asc).
        /// </returns>
        public static List<Vector3Int> Apply(
            IEnumerable<Vector3Int> candidates,
            int spacingTiles,
            Tilemap tilemap,
            IEnumerable<Vector2> existingPositions)
        {
            var result = new List<Vector3Int>();
            if (candidates == null) return result;

            float minDist = (float)spacingTiles; // 1 tile = 1 world unit

            // Materialise existing positions into a list for fast indexed access.
            var existing = existingPositions != null
                ? new List<Vector2>(existingPositions)
                : new List<Vector2>();

            // Sort candidates: Y descending (top rows first), then X ascending.
            var sorted = new List<Vector3Int>(candidates);
            sorted.Sort((a, b) =>
            {
                if (b.y != a.y) return b.y.CompareTo(a.y);
                return a.x.CompareTo(b.x);
            });

            var placedPositions = new List<Vector2>(sorted.Count);

            foreach (var cell in sorted)
            {
                Vector3 worldCenter = tilemap.GetCellCenterWorld(cell);
                var wc = new Vector2(worldCenter.x, worldCenter.y);

                bool tooClose = false;

                // Check against pre-existing buildings.
                for (int i = 0; i < existing.Count; i++)
                {
                    if (Vector2.Distance(wc, existing[i]) < minDist)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    // Check against already-accepted candidates in this batch.
                    for (int i = 0; i < placedPositions.Count; i++)
                    {
                        if (Vector2.Distance(wc, placedPositions[i]) < minDist)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                }

                if (!tooClose)
                {
                    result.Add(cell);
                    placedPositions.Add(wc);
                }
            }

            return result;
        }
    }
}
