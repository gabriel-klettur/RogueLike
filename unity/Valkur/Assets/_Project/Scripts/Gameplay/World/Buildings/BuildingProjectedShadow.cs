using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Whether a building casts a PROJECTED sun shadow at all.
    ///
    /// Most do. The exception is a family that only becomes obvious when you look at the art:
    /// a good part of the catalogue is not a thing standing on the ground, it IS the ground —
    /// gardens, flower beds, plazas, training yards, the coliseum floor, a well seen from
    /// straight above. Those are drawn in plan, so they have no silhouette and no base, and
    /// shearing them across the floor produces a second copy of the garden lying beside the
    /// first. Padding has nothing to do with it: they would be wrong trimmed to the pixel.
    ///
    /// Resolved from the sprite's own folder, like <see cref="BuildingWindSway"/>, so a new
    /// garden imported into <c>Buildings/gardens/</c> is excluded with no data edit — and
    /// overridable per template for the exceptions, as a tri-state so "not set" stays
    /// distinguishable from "no".
    ///
    /// The folder list comes from a visual review of every building sprite whose ink does not
    /// reach its bottom row (98 of 1256); see <c>.github/building_trim_verdicts.csv</c>.
    /// </summary>
    public static class BuildingProjectedShadow
    {
        /// <summary>Folders whose art is drawn in plan: it is floor, not a standing object.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of literal folder prefixes, written once at type " +
                                        "initialisation and never mutated; nothing a Play session does can stale it.")]
        private static readonly string[] FlatPrefixes =
        {
            "buildings/gardens/",
            "buildings/combat/training",
            "buildings/combat/combat_training",
            "buildings/combat/coliseo",
        };

        /// <summary>Individual sprites that are drawn in plan outside those folders.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of literal asset paths, written once at type " +
                                        "initialisation and never mutated; nothing a Play session does can stale it.")]
        private static readonly string[] FlatAssets =
        {
            "buildings/others/fuente",
            "buildings/portals/portal_well_closed",
            "buildings/portals/portal_runes_inactive",
        };

        public static bool Resolve(BuildingTemplateData template)
        {
            if (template == null) return false;
            if (template.projectedShadow > 0) return true;
            if (template.projectedShadow < 0) return false;
            return !IsFlatArt(template.assetPath);
        }

        /// <summary>True when the path names art drawn in plan. Pure, so the fixture can walk it.</summary>
        public static bool IsFlatArt(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            string p = assetPath.ToLowerInvariant();
            foreach (var prefix in FlatPrefixes)
                if (p.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
            foreach (var asset in FlatAssets)
                if (p == asset) return true;
            return false;
        }
    }
}
