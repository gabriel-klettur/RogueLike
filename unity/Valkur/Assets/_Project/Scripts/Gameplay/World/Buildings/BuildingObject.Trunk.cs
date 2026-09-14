using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Trunk aspect of <see cref="BuildingObject"/>: the world bounds a blow lands on and the
    /// interact key measures to. Derived from <see cref="TryGetWorldRect"/> through
    /// <see cref="BuildingTrunkGeometry"/>, like the doorway, so no second copy of the sprite
    /// size, scale or split lives here.
    /// </summary>
    public partial class BuildingObject
    {
        /// <summary>
        /// World-space bounds of the trunk. False when the template draws no trunk, when the
        /// renderers are not built yet, or while the REMAINS are showing: a stump is a different
        /// sprite and the box was drawn on the tree, so it would point at empty ground.
        /// Callers fall back to the footprint renderer on false.
        /// </summary>
        public bool TryGetTrunkBounds(out Bounds bounds)
        {
            bounds = default;
            if (_template == null || !_template.HasTrunk) return false;
            if (_hasPristineSnapshot) return false;
            if (!TryGetWorldRect(out var rect)) return false;
            if (!BuildingTrunkGeometry.TryGetTrunkRect(rect, _template.trunkNormalized, out var trunk)) return false;

            bounds = new Bounds(new Vector3(trunk.center.x, trunk.center.y, 0f),
                                new Vector3(trunk.width, trunk.height, 0f));
            return true;
        }
    }
}
