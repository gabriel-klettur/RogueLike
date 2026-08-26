using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Pure geometry for building doorways: turns a normalized anchor authored on
    /// <c>BuildingTemplateData</c> into a world-space rect, given the building's own
    /// world rect from <c>BuildingObject.TryGetWorldRect</c>.
    ///
    /// Deliberately free of MonoBehaviour, ScriptableObject and scene state so the whole
    /// contract — scale invariance, clamping, the minimum size — is unit-testable without
    /// standing up a building. Every consumer (the runtime trigger, the F10 overlay in
    /// Phase 2, validation) routes through here, which is what stops those three from
    /// drifting apart the way the collision-cell geometry once did before
    /// <c>BuildingObject.TryGetWorldCellRect</c> became its single owner.
    /// </summary>
    public static class BuildingDoorGeometry
    {
        /// <summary>
        /// Smallest world-unit extent a doorway is allowed to have on either axis.
        /// A door authored at a few percent of a large sprite resolves to a rect a
        /// couple of pixels across, which the player can brush past without the trigger
        /// ever firing — the door then reads as broken rather than small. Capped by the
        /// building's own size, so a genuinely tiny prop is not inflated past its bounds.
        /// </summary>
        public const float MIN_DOOR_EXTENT_WORLD = 0.2f;

        /// <summary>
        /// How far BELOW the doorway the player is placed when they come back out.
        /// A building's transform sits at its bottom-centre and its footprint is solid,
        /// so "below the door" is the one direction guaranteed to be outdoors.
        /// </summary>
        public const float DEFAULT_EXIT_MARGIN_WORLD = 0.75f;

        /// <summary>
        /// Resolve the doorway's world rect. Returns false when the building rect is
        /// degenerate (zero width or height), which is the state a building is in before
        /// its renderers exist — callers must treat that as "not ready", not as "no door".
        ///
        /// The result is always fully contained by <paramref name="buildingWorldRect"/>:
        /// an offset of (0, 0) means the doorway's centre is pushed just far enough inward
        /// that its bottom-left corner lands on the building's bottom-left corner, rather
        /// than half the door hanging outside the sprite.
        /// </summary>
        public static bool TryGetDoorRect(Rect buildingWorldRect,
                                          Vector2 offsetNormalized,
                                          Vector2 sizeNormalized,
                                          out Rect doorWorldRect)
        {
            doorWorldRect = default;
            if (buildingWorldRect.width <= 0f || buildingWorldRect.height <= 0f)
                return false;

            float w = ResolveExtent(buildingWorldRect.width,  sizeNormalized.x);
            float h = ResolveExtent(buildingWorldRect.height, sizeNormalized.y);

            float ox = Mathf.Clamp01(offsetNormalized.x);
            float oy = Mathf.Clamp01(offsetNormalized.y);

            float cx = buildingWorldRect.xMin + buildingWorldRect.width  * ox;
            float cy = buildingWorldRect.yMin + buildingWorldRect.height * oy;

            // Keep the whole rect inside the sprite. Min/Max are already valid because
            // ResolveExtent never returns more than the building's own extent.
            cx = Mathf.Clamp(cx, buildingWorldRect.xMin + w * 0.5f, buildingWorldRect.xMax - w * 0.5f);
            cy = Mathf.Clamp(cy, buildingWorldRect.yMin + h * 0.5f, buildingWorldRect.yMax - h * 0.5f);

            doorWorldRect = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
            return true;
        }

        /// <summary>
        /// Where the player is put down when they leave through this doorway: bottom-centre
        /// of the door, pushed <paramref name="margin"/> further out so they do not
        /// re-enter the trigger they just came through on the same frame.
        /// </summary>
        public static Vector2 ResolveExitPoint(Rect doorWorldRect,
                                               float margin = DEFAULT_EXIT_MARGIN_WORLD)
            => new Vector2(doorWorldRect.center.x, doorWorldRect.yMin - Mathf.Max(0f, margin));

        /// <summary>
        /// Which cell of a (rows x cols) collision grid the doorway's centre falls in,
        /// row 0 = TOP, matching the authored JSON order and
        /// <c>BuildingObject.TryGetWorldCellRect</c>. Used by the load-time validation that
        /// warns when a doorway was anchored over a painted-solid cell — a door the player
        /// can see and bounces off reads as a physics bug, not as authoring left undone.
        /// </summary>
        public static bool TryGetDoorCell(Rect buildingWorldRect, Rect doorWorldRect,
                                          int rows, int cols, out int row, out int col)
        {
            row = 0;
            col = 0;
            if (rows <= 0 || cols <= 0) return false;
            if (buildingWorldRect.width <= 0f || buildingWorldRect.height <= 0f) return false;

            float u = (doorWorldRect.center.x - buildingWorldRect.xMin) / buildingWorldRect.width;
            float v = (doorWorldRect.center.y - buildingWorldRect.yMin) / buildingWorldRect.height;

            col = Mathf.Clamp(Mathf.FloorToInt(u * cols), 0, cols - 1);
            // v runs bottom-up; grid rows run top-down.
            row = Mathf.Clamp(rows - 1 - Mathf.FloorToInt(v * rows), 0, rows - 1);
            return true;
        }

        private static float ResolveExtent(float buildingExtent, float normalized)
        {
            float raw = buildingExtent * Mathf.Clamp01(normalized);
            float floor = Mathf.Min(MIN_DOOR_EXTENT_WORLD, buildingExtent);
            return Mathf.Clamp(raw, floor, buildingExtent);
        }
    }
}
