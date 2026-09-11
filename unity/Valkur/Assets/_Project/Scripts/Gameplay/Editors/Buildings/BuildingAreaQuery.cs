using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// The geometry of a marquee selection, kept pure so it can be proved without a scene:
    /// a rectangle from two dragged corners, and which buildings TOUCH it.
    ///
    /// <para>"Touch" is <see cref="Rect.Overlaps(Rect)"/> against the building's whole drawn
    /// rect (footprint AND canopy, the same rect the hover test uses), not containment. An
    /// author boxing a row of houses drags across their fronts, not around their roofs — a
    /// containment rule would select nothing until the box swallowed every canopy, which on a
    /// tall building is most of the screen.</para>
    /// </summary>
    public static class BuildingAreaQuery
    {
        /// <summary>A rect from any two opposite corners. A drag up-and-left has its second
        /// corner below and left of its first; <see cref="Rect"/> with a negative size
        /// answers Overlaps wrongly, so the corners are sorted first.</summary>
        public static Rect FromCorners(Vector2 a, Vector2 b)
        {
            float xMin = Mathf.Min(a.x, b.x), xMax = Mathf.Max(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y), yMax = Mathf.Max(a.y, b.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>
        /// Every ACTIVE building whose drawn rect overlaps <paramref name="area"/>, in the
        /// order given. Deactivated ones are skipped — that is this editor's delete — and so
        /// is anything whose rect cannot be resolved yet.
        /// </summary>
        public static List<BuildingObject> Overlapping(IReadOnlyList<BuildingObject> candidates, Rect area)
        {
            var hits = new List<BuildingObject>();
            if (candidates == null) return hits;
            for (int i = 0; i < candidates.Count; i++)
            {
                var b = candidates[i];
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                if (!b.TryGetWorldRect(out var r)) continue;
                if (r.Overlaps(area)) hits.Add(b);
            }
            return hits;
        }
    }
}
