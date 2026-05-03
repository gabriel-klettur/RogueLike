using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Pure geometry helper used by the Spawner Editor (F3) Alt-toggle hover
    /// and centre-click selection. Lives outside any MonoBehaviour so the
    /// distance test is trivially unit-testable without a Unity scene.
    /// </summary>
    public static class SpawnerHitTester
    {
        /// <summary>
        /// Returns the index of the position closest to <paramref name="cursor"/>
        /// within <paramref name="maxDist"/> world units. Returns -1 when no
        /// position lies inside the radius. Ties are resolved by lowest index.
        /// Null or empty input returns -1.
        /// </summary>
        public static int FindClosestWithinRadius(IReadOnlyList<Vector2> positions,
                                                  Vector2 cursor,
                                                  float maxDist)
        {
            if (positions == null || positions.Count == 0) return -1;
            if (maxDist <= 0f) return -1;

            int   bestIndex = -1;
            float bestDist  = maxDist;

            for (int i = 0; i < positions.Count; i++)
            {
                float d = Vector2.Distance(positions[i], cursor);
                // Strict less-than keeps tie behaviour stable: the first matching
                // position wins, later equidistant ones are skipped.
                if (d < bestDist)
                {
                    bestDist  = d;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }
    }
}
