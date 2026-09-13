using Valkur.Data;
using Valkur.Gameplay.Buildings;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Whether a building's canopy sways in the wind. One answer, derived from the template:
    /// an explicit <see cref="BuildingTemplateData.windSway"/> wins, and otherwise the picker's
    /// own category decides — trees and ground flora sway, everything built by hands does not.
    ///
    /// Derived from the category rather than from a flag on all 1176 templates because the
    /// category is already keyed off the sprite's folder: a new tree imported into
    /// <c>Buildings/trees/</c> sways with no data edit, exactly as it files itself under the
    /// Trees tab with none. The override exists for the exceptions — a stone tree, a flag on
    /// a house — and is a tri-state so "not set" stays distinguishable from "no".
    /// </summary>
    public static class BuildingWindSway
    {
        public static bool Resolve(BuildingTemplateData template)
        {
            if (template == null) return false;
            if (template.windSway > 0) return true;
            if (template.windSway < 0) return false;
            var category = BuildingCategory.Of(template);
            return category == BuildingCategory.Category.Trees || category == BuildingCategory.Category.Flora;
        }
    }
}
