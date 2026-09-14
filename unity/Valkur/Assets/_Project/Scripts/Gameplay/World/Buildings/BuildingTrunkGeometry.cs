using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Pure geometry for a tree's trunk box: turns the normalized rect authored on
    /// <c>BuildingTemplateData.trunkNormalized</c> into world space, given the building's own
    /// world rect from <c>BuildingObject.TryGetWorldRect</c>.
    ///
    /// Same shape and same reason as <see cref="BuildingDoorGeometry"/>: free of scene state so
    /// the contract (bottom-left origin, clamped inside the sprite, never degenerate) is
    /// unit-testable, and one owner so the interaction range, the blow contact and the trunk
    /// mark cannot disagree about where the trunk is.
    /// </summary>
    public static class BuildingTrunkGeometry
    {
        /// <summary>
        /// Smallest world extent a trunk box may have on either axis. A thin palm trunk drawn at
        /// a few percent of a wide sprite would otherwise be a sliver a blow slides past, which
        /// reads as a tree that cannot be hit. Capped by the building's own size.
        /// </summary>
        public const float MIN_TRUNK_EXTENT_WORLD = 0.3f;

        /// <summary>
        /// Resolve the trunk's world rect. False when the building rect is degenerate or the
        /// normalized box has no area — callers fall back to the footprint.
        /// </summary>
        public static bool TryGetTrunkRect(Rect buildingWorldRect, Rect trunkNormalized, out Rect trunkWorldRect)
        {
            trunkWorldRect = default;
            if (buildingWorldRect.width <= 0f || buildingWorldRect.height <= 0f) return false;
            if (trunkNormalized.width <= 0f || trunkNormalized.height <= 0f) return false;

            float x0 = Mathf.Clamp01(trunkNormalized.xMin);
            float x1 = Mathf.Clamp01(trunkNormalized.xMax);
            float y0 = Mathf.Clamp01(trunkNormalized.yMin);
            float y1 = Mathf.Clamp01(trunkNormalized.yMax);
            if (x1 <= x0 || y1 <= y0) return false;

            float w = Widen(buildingWorldRect.width * (x1 - x0), buildingWorldRect.width);
            float h = Widen(buildingWorldRect.height * (y1 - y0), buildingWorldRect.height);

            float cx = buildingWorldRect.xMin + buildingWorldRect.width * (x0 + x1) * 0.5f;
            float cy = buildingWorldRect.yMin + buildingWorldRect.height * (y0 + y1) * 0.5f;
            cx = Mathf.Clamp(cx, buildingWorldRect.xMin + w * 0.5f, buildingWorldRect.xMax - w * 0.5f);
            cy = Mathf.Clamp(cy, buildingWorldRect.yMin + h * 0.5f, buildingWorldRect.yMax - h * 0.5f);

            trunkWorldRect = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
            return true;
        }

        private static float Widen(float extent, float buildingExtent) =>
            Mathf.Clamp(extent, Mathf.Min(MIN_TRUNK_EXTENT_WORLD, buildingExtent), buildingExtent);
    }
}
