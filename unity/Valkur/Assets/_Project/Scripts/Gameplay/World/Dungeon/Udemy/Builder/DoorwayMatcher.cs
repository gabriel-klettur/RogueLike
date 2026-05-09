using System.Collections.Generic;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Builder
{
    /// <summary>
    /// Pure (no-Unity-runtime) helpers for matching doorways and computing
    /// the world position of a child room aligned to a parent doorway.
    /// Extracted into a separate static class so the geometry can be
    /// unit-tested without instantiating the full <see cref="DungeonBuilder"/>.
    /// </summary>
    public static class DoorwayMatcher
    {
        /// <summary>
        /// Find the first doorway in <paramref name="doorwayList"/> whose
        /// orientation is the opposite of <paramref name="parentDoorway"/>.
        /// Returns null when no compatible doorway exists.
        /// </summary>
        public static Doorway GetOppositeDoorway(Doorway parentDoorway, IList<Doorway> doorwayList)
        {
            if (parentDoorway == null || doorwayList == null) return null;

            for (int i = 0; i < doorwayList.Count; i++)
            {
                var doorway = doorwayList[i];
                if (doorway == null) continue;
                if (IsOpposite(parentDoorway.orientation, doorway.orientation))
                    return doorway;
            }
            return null;
        }

        /// <summary>
        /// Adjacency adjustment applied to the parent doorway's world position
        /// so the child room sits one tile outside the parent doorway in the
        /// child doorway's orientation. Mirrors Udemy's switch in
        /// <c>DungeonBuilder.PlaceTheRoom</c>.
        /// </summary>
        public static Vector2Int GetAdjacencyAdjustment(Orientation childOrientation)
        {
            switch (childOrientation)
            {
                case Orientation.North: return new Vector2Int(0, -1);
                case Orientation.East: return new Vector2Int(-1, 0);
                case Orientation.South: return new Vector2Int(0, 1);
                case Orientation.West: return new Vector2Int(1, 0);
                default: return Vector2Int.zero;
            }
        }

        /// <summary>
        /// Compute the child room's world-space lower-bounds tile so its
        /// <paramref name="childDoorway"/> aligns with the parent's
        /// <paramref name="parentDoorway"/> on the parent room placed at
        /// <paramref name="parentLowerBounds"/>.
        ///
        /// Formula (verbatim port of Udemy):
        ///   parentDoorwayPos = parent.lowerBounds + parentDoorway.position - parent.templateLowerBounds
        ///   adjustment       = orientation-dependent unit step
        ///   child.lowerBounds = parentDoorwayPos + adjustment + child.templateLowerBounds - childDoorway.position
        /// </summary>
        public static Vector2Int ComputeChildLowerBounds(
            Vector2Int parentLowerBounds,
            Vector2Int parentTemplateLowerBounds,
            Doorway parentDoorway,
            Vector2Int childTemplateLowerBounds,
            Doorway childDoorway)
        {
            var parentDoorwayWorldPos = parentLowerBounds
                + parentDoorway.position
                - parentTemplateLowerBounds;
            var adjustment = GetAdjacencyAdjustment(childDoorway.orientation);
            return parentDoorwayWorldPos + adjustment + childTemplateLowerBounds - childDoorway.position;
        }

        /// <summary>True if two AABB-aligned tile rectangles overlap (closed bounds).</summary>
        public static bool RoomsOverlap(
            Vector2Int aLowerBounds, Vector2Int aUpperBounds,
            Vector2Int bLowerBounds, Vector2Int bUpperBounds)
        {
            return IntervalOverlaps(aLowerBounds.x, aUpperBounds.x, bLowerBounds.x, bUpperBounds.x)
                && IntervalOverlaps(aLowerBounds.y, aUpperBounds.y, bLowerBounds.y, bUpperBounds.y);
        }

        // 1D inclusive overlap (Udemy: Mathf.Max(min) <= Mathf.Min(max)).
        private static bool IntervalOverlaps(int aMin, int aMax, int bMin, int bMax)
            => Mathf.Max(aMin, bMin) <= Mathf.Min(aMax, bMax);

        private static bool IsOpposite(Orientation a, Orientation b)
        {
            switch (a)
            {
                case Orientation.North: return b == Orientation.South;
                case Orientation.South: return b == Orientation.North;
                case Orientation.East: return b == Orientation.West;
                case Orientation.West: return b == Orientation.East;
                default: return false;
            }
        }
    }
}
