using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>One entity the CERCA section lists.</summary>
    public struct DebugNearbyEntry
    {
        public GameObject Go;
        public float Distance;
        public FactionSide Side;
    }

    /// <summary>
    /// Picks what the CERCA section shows: the nearest entities inside a radius, sorted by
    /// distance, each with the side it is actually on, plus a count per side.
    ///
    /// <para><b>The two defects this replaced.</b> The old panel walked
    /// <c>EntityRegistry.Monsters</c> in REGISTRATION order and kept the first five inside 15
    /// units, so the list was "whoever spawned first", not "whoever is closest". And it painted
    /// every entry in the monster colour — but that registry holds every entity that went
    /// through <c>ConfigureMonster</c>, and measured live six of its eleven entries were
    /// neutral vendors. The side comes from <see cref="EntityFaction.SideOf"/>, the same answer
    /// the minimap and the threat table use (H8: read the system's own seam).</para>
    ///
    /// <para>The classifier is a parameter so a test can drive the sort without building
    /// factions; production passes <see cref="EntityFaction.SideOf"/>.</para>
    /// </summary>
    public static class DebugHudNearby
    {
        /// <summary>
        /// Fills <paramref name="into"/> with at most <paramref name="max"/> entries, nearest
        /// first, and counts every candidate inside <paramref name="radius"/> by side (the
        /// counts cover everyone in range, not only the rows shown).
        /// </summary>
        public static void Collect(IReadOnlyList<GameObject> candidates, Vector2 origin, float radius, int max,
                                   Func<GameObject, FactionSide> classify, List<DebugNearbyEntry> into,
                                   out int hostiles, out int neutrals, out int allies)
        {
            into.Clear();
            hostiles = neutrals = allies = 0;
            // max 0 still COUNTS: the bug report wants the per-side totals without any rows.
            if (candidates == null) return;

            float r2 = radius * radius;
            for (int i = 0; i < candidates.Count; i++)
            {
                var go = candidates[i];
                if (go == null || !go.activeInHierarchy) continue;
                Vector2 d = (Vector2)go.transform.position - origin;
                float sq = d.sqrMagnitude;
                if (sq > r2) continue;

                var side = classify != null ? classify(go) : FactionSide.Hostile;
                switch (side)
                {
                    case FactionSide.Hostile: hostiles++; break;
                    case FactionSide.Neutral: neutrals++; break;
                    default: allies++; break;
                }

                var entry = new DebugNearbyEntry { Go = go, Distance = Mathf.Sqrt(sq), Side = side };
                InsertSorted(into, entry, max);
            }
        }

        /// <summary>Insertion into a list kept sorted and capped: N is 4, so this beats a sort.</summary>
        private static void InsertSorted(List<DebugNearbyEntry> list, DebugNearbyEntry e, int max)
        {
            int at = list.Count;
            while (at > 0 && list[at - 1].Distance > e.Distance) at--;
            if (at >= max) return;
            list.Insert(at, e);
            if (list.Count > max) list.RemoveAt(list.Count - 1);
        }
    }
}
