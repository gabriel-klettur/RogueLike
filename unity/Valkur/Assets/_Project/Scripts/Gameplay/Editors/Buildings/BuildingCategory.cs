using System;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Editorial grouping for the Buildings Editor (F10) template picker.
    ///
    /// The catalog holds 506 templates in one flat grid, and roughly 340 of them are one of
    /// three things nobody is browsing for at the same time: 137 tree variants, 106 pieces of
    /// ground flora, and 105 street dressing (lights, signs, market). Scrolling past all of
    /// them to reach a temple is the problem these tabs solve.
    ///
    /// Classification keys off the template's <c>assetPath</c> — the Resources folder the
    /// sprite lives in — rather than a field on the ScriptableObject, so importing a new
    /// sprite into an existing folder files it correctly with no data edit. Rules are ordered
    /// and the first match wins, which is what lets a handful of specific files override
    /// their folder (the three assets dumped in <c>Buildings/others/</c> are a portal, a
    /// fountain and a guillotine — no single tab is right for all three).
    ///
    /// A path that matches nothing lands in <see cref="Category.Structures"/> rather than
    /// vanishing from the picker. <see cref="BuildingCategoryTests"/> asserts that no
    /// shipped template relies on that fallback.
    /// </summary>
    public static class BuildingCategory
    {
        public enum Category
        {
            /// <summary>Trees, the single largest family in the catalog.</summary>
            Trees,
            /// <summary>Ground cover: bushes, flowers, grass, rocks, mushrooms, forest litter.</summary>
            Flora,
            /// <summary>Anything a character enters or a town is built from: houses, shops, temples, castles, arenas.</summary>
            Structures,
            /// <summary>Street and yard furniture: benches, barrels, carts, wells, fountains, fences.</summary>
            Props,
            /// <summary>Market stalls, counters, produce crates, sacks, baskets, merchant tools.</summary>
            Market,
            /// <summary>Lamp posts, lanterns, braziers, torches, wall sconces.</summary>
            Lights,
            /// <summary>Signposts, shop signs, heraldic banners, notice boards.</summary>
            Signs,
            /// <summary>Portals, totems, statues — the ritual set dressing.</summary>
            Arcane,
        }

        /// <summary>Tab order in the picker. Explicit so it never depends on enum order.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable tab order built once from enum literals. " +
            "Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        public static readonly Category[] TabOrder =
        {
            Category.Trees, Category.Flora, Category.Structures, Category.Props,
            Category.Market, Category.Lights, Category.Signs, Category.Arcane,
        };

        /// <summary>
        /// Short label. The Buildings panel gives its content 368 px and the tab block splits
        /// that three to a row, so a label has ~121 px at font size 10 — comfortable for
        /// "Structures", the longest name here, and not much more.
        /// </summary>
        public static string Label(Category c)
        {
            switch (c)
            {
                case Category.Trees:      return "Trees";
                case Category.Flora:      return "Flora";
                case Category.Structures: return "Structures";
                case Category.Props:      return "Props";
                case Category.Market:     return "Market";
                case Category.Lights:     return "Lights";
                case Category.Signs:      return "Signs";
                case Category.Arcane:     return "Arcane";
                default:                  return c.ToString();
            }
        }

        // Ordered rules over the full Resources path; the first prefix that matches decides.
        // File-level rules must precede the folder rule they override.
        [Valkur.Core.SelfHealingStatic("Immutable prefix table built once from string literals. " +
            "Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly (string Prefix, Category Cat)[] Rules =
        {
            // ── Prop sheets imported in 2026-08 ───────────────────────────────────
            ("buildings/lights/",  Category.Lights),
            ("buildings/signs/",   Category.Signs),
            ("buildings/market/",  Category.Market),
            ("buildings/props/",   Category.Props),
            // nature/ is ground cover except for the seven tree sprites in it, which belong
            // with the other 137 trees rather than with the grass tufts.
            ("buildings/nature/tree_", Category.Trees),
            ("buildings/nature/",      Category.Flora),

            // ── Legacy folders ────────────────────────────────────────────────────
            ("buildings/vegetation/",        Category.Trees),
            ("buildings/gardens/",           Category.Flora),
            ("buildings/forest_decoration/", Category.Flora),

            ("buildings/portals/", Category.Arcane),
            ("buildings/totems/",  Category.Arcane),
            ("buildings/statues/", Category.Arcane),

            // The three assets dumped straight into others/ have nothing in common.
            ("buildings/others/portal", Category.Arcane),
            ("buildings/others/fuente", Category.Props),
            ("buildings/others/",       Category.Props),

            ("buildings/houses/",      Category.Structures),
            ("buildings/castles/",     Category.Structures),
            ("buildings/temples/",     Category.Structures),
            ("buildings/shops/",       Category.Structures),
            ("buildings/combat/",      Category.Structures),
            ("buildings/backgrounds/", Category.Structures),

            // Two sprites sit loose at the root of Buildings/ rather than in a folder.
            ("buildings/dummy", Category.Structures),
            ("buildings/mine",  Category.Structures),
        };

        /// <summary>
        /// Category for a template's Resources path. Never throws; a null, empty or
        /// unmatched path is <see cref="Category.Structures"/>.
        /// </summary>
        public static Category Of(string assetPath)
            => TryMatch(assetPath, out Category c) ? c : Category.Structures;

        /// <summary>
        /// Same classification as <see cref="Of"/>, but reports whether a rule actually
        /// matched. Exists so the test suite can prove no shipped template is riding the
        /// silent fallback — the failure mode of any prefix table is that a renamed folder
        /// quietly drains into the default bucket.
        /// </summary>
        public static bool TryMatch(string assetPath, out Category category)
        {
            category = Category.Structures;
            if (string.IsNullOrEmpty(assetPath)) return false;

            string path = assetPath.ToLowerInvariant();
            for (int i = 0; i < Rules.Length; i++)
            {
                if (path.StartsWith(Rules[i].Prefix, StringComparison.Ordinal))
                {
                    category = Rules[i].Cat;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Convenience overload; a null template is <see cref="Category.Structures"/>.</summary>
        public static Category Of(Valkur.Data.BuildingTemplateData template)
            => Of(template != null ? template.assetPath : null);
    }
}
